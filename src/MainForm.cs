using System.Diagnostics;
using System.Drawing;
using LibreHardwareMonitor.Hardware;

namespace TuringLite;

/// <summary>Ventana de ajustes. Se crea al abrirla y se libera al cerrarla.</summary>
sealed class MainForm : Form
{
    static readonly (string Key, string Label)[] Orientations =
    {
        ("portrait", "Vertical"),
        ("landscape", "Horizontal"),
        ("reverse-portrait", "Vertical invertida"),
        ("reverse-landscape", "Horizontal invertida"),
    };

    readonly MonitorService _svc;
    readonly TrayApp _tray;
    readonly Label _status = new() { AutoSize = false };
    readonly Button _pause;
    readonly Button _service;
    bool _serviceInstalled, _serviceRunning, _probing;
    DateTime _lastProbe = DateTime.MinValue;
    readonly TrackBar _brightness = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 10 };
    readonly Label _brightnessValue = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleRight };
    readonly ComboBox _orientation = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly NumericUpDown _interval = new() { Minimum = 500, Maximum = 10000, Increment = 250 };
    readonly ComboBox _gpu = new() { DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 320 };
    const string GpuAuto = "Automática";
    readonly CheckBox _autostart = new() { Text = "Iniciar con Windows", AutoSize = true };
    readonly ListView _components = new() { View = View.Details, FullRowSelect = true, HideSelection = false };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    readonly bool _elevated = Elevation.IsElevated();

    public MainForm(MonitorService svc, TrayApp tray)
    {
        _svc = svc;
        _tray = tray;
        Text = "TuringLite";
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(500, 662);

        // --- Cabecera y estado ---
        Controls.Add(Ui.MakeLabel("TuringLite", 20, 14, font: Ui.TitleFont));
        _pause = Ui.MakeButton("Pausar", 360, 18, 120, 32, false, () => _tray.TogglePause());
        Controls.Add(_pause);
        _status.SetBounds(20, 74, 460, 20);
        Controls.Add(_status);

        // --- Pantalla: brillo, orientación, tema, intervalo ---
        var screen = Ui.MakeCard(20, 104, 460, 238, "Pantalla");
        screen.Controls.Add(Ui.MakeLabel("Brillo", 16, 52));
        _brightness.SetBounds(96, 42, 290, 40);
        _brightnessValue.SetBounds(394, 52, 50, 20);
        _brightness.ValueChanged += (_, _) =>
        {
            _brightnessValue.Text = $"{_brightness.Value}%";
            _svc.SetBrightness(_brightness.Value); // se aplica al instante
        };
        screen.Controls.Add(_brightness);
        screen.Controls.Add(_brightnessValue);

        screen.Controls.Add(Ui.MakeLabel("Orientación", 16, 94));
        _orientation.SetBounds(96, 90, 180, 26);
        foreach (var o in Orientations) _orientation.Items.Add(o.Label);
        screen.Controls.Add(_orientation);

        screen.Controls.Add(Ui.MakeLabel("Tema", 16, 130));
        _theme.SetBounds(96, 126, 348, 26);
        foreach (var t in Themes.All) _theme.Items.Add(t.Name);
        screen.Controls.Add(_theme);

        screen.Controls.Add(Ui.MakeLabel("Intervalo", 16, 166));
        _interval.SetBounds(96, 162, 90, 26);
        screen.Controls.Add(_interval);
        screen.Controls.Add(Ui.MakeLabel("ms", 192, 166, dim: true));
        screen.Controls.Add(Ui.MakeLabel("GPU", 236, 166));
        _gpu.SetBounds(278, 162, 166, 26);
        // Las GPU se leen al abrir la lista: al mostrar la ventana los sensores pueden no estar listos todavía.
        _gpu.DropDown += (_, _) => FillGpus(SelectedGpu());
        screen.Controls.Add(_gpu);

        _autostart.Location = new Point(16, 204);
        screen.Controls.Add(_autostart);
        var save = Ui.MakeButton("Guardar y aplicar", 296, 198, 148, 30, true, () => Save());
        screen.Controls.Add(save);
        Controls.Add(screen);

        // --- Componentes: lista con el valor actual ---
        var components = Ui.MakeCard(20, 350, 460, 140, "Componentes");
        components.Controls.Add(Ui.MakeLabel("Doble clic para editar", 248, 14, dim: true, font: Ui.SmallFont));
        _components.SetBounds(12, 38, 436, 90);
        _components.Columns.Add("Componente", 250);
        _components.Columns.Add("Valor", 160, HorizontalAlignment.Right);
        _components.DoubleClick += (_, _) => OpenComponentsEditor();
        components.Controls.Add(_components);
        Controls.Add(components);

        // --- Acciones ---
        AddButton("Editar componentes…", 20, 502, 460, 34, primary: true, OpenComponentsEditor);
        AddButton("Editar texto (status.txt)", 20, 544, 224, 30, primary: false, () => OpenInNotepad(StatusFilePath()));
        AddButton("Editar config.json (avanzado)", 256, 544, 224, 30, primary: false, () => OpenInNotepad(_svc.ConfigPath));
        AddButton("Ver todos los sensores", 20, 580, 224, 30, primary: false, ShowAllSensors);
        AddButton("Patrón de prueba", 256, 580, 224, 30, primary: false, () =>
        {
            if (MessageBox.Show("Se detendrá el monitoreo y se dibujarán filas de prueba con distintos anchos. " +
                                "Toma una foto de la pantalla y pulsa Reanudar para volver.",
                                "Patrón de prueba", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK)
                _svc.RunTestPattern();
        });
        _service = Ui.MakeButton("Instalar servicio de sensores…", 20, 620, 460, 30, false, OnServiceClick);
        Controls.Add(_service);

        LoadValues();
        RefreshStatus();
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();

        Ui.Dark(this);
    }

    void AddButton(string text, int x, int y, int w, int h, bool primary, Action onClick)
    {
        var b = Ui.MakeButton(text, x, y, w, h, primary, () =>
        {
            try { onClick(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "TuringLite", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        });
        Controls.Add(b);
    }

    void LoadValues()
    {
        var c = _svc.Config;
        _brightness.Value = Math.Clamp(c.Brightness, 0, 100);
        _brightnessValue.Text = $"{_brightness.Value}%";
        int idx = Array.FindIndex(Orientations, o => o.Key.Equals(c.Orientation, StringComparison.OrdinalIgnoreCase));
        _orientation.SelectedIndex = idx < 0 ? 0 : idx;
        _theme.SelectedIndex = Themes.IndexOf(c.Theme);
        _interval.Value = Math.Clamp(c.IntervalMs, (int)_interval.Minimum, (int)_interval.Maximum);
        FillGpus(c.Gpu);
        try { _autostart.Checked = AutoStart.IsEnabled(); } catch { _autostart.Enabled = false; }
    }

    /// <summary>
    /// Llena la lista de GPU con las que detectan los sensores. «Automática» guarda un filtro vacío (elige la
    /// dedicada). Un valor guardado que ya no se detecta se conserva para no perderlo al guardar otros ajustes.
    /// </summary>
    void FillGpus(string selected)
    {
        selected = selected.Trim();
        var names = (_svc.Sensors?.Catalog() ?? new List<SensorInfo>())
            .Where(i => i.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            .Select(i => i.HardwareName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selected.Length > 0 && !names.Any(n => n.Equals(selected, StringComparison.OrdinalIgnoreCase)))
            names.Add(selected);

        _gpu.BeginUpdate();
        _gpu.Items.Clear();
        _gpu.Items.Add(GpuAuto);
        foreach (var n in names) _gpu.Items.Add(n);
        int idx = selected.Length == 0 ? 0 : _gpu.Items.Cast<string>().ToList()
            .FindIndex(n => n.Equals(selected, StringComparison.OrdinalIgnoreCase));
        _gpu.SelectedIndex = Math.Max(0, idx);
        _gpu.EndUpdate();
    }

    /// <summary>Filtro de GPU para config.json: vacío = automática.</summary>
    string SelectedGpu() => _gpu.SelectedIndex <= 0 ? "" : (string)_gpu.SelectedItem!;

    public void RefreshStatus()
    {
        if (IsDisposed) return;
        ProbeServiceInBackground();
        string cpuNote = _serviceRunning ? " · servicio de sensores activo"
                       : _elevated ? ""
                       : " · sin servicio de sensores: sin temperatura de CPU";
        _status.Text = "● " + _svc.StatusText + cpuNote;
        _service.Text = _serviceInstalled ? "Quitar servicio de sensores" : "Instalar servicio de sensores…";
        _status.ForeColor = _svc.State switch
        {
            MonitorState.Running => Ui.Accent,
            MonitorState.Error => Ui.Danger,
            MonitorState.Paused => Ui.Warn,
            _ => Ui.Dim,
        };
        _pause.Text = _svc.State is MonitorState.Paused or MonitorState.Stopped ? "Reanudar" : "Pausar";
        RefreshComponents();
    }

    /// <summary>
    /// Comprueba el servicio fuera del hilo de la interfaz (una conexión a la tubería no debe congelar la ventana)
    /// y como mucho cada 5 s.
    /// </summary>
    void ProbeServiceInBackground()
    {
        if (_probing || DateTime.UtcNow - _lastProbe < TimeSpan.FromSeconds(5)) return;
        _probing = true;
        _lastProbe = DateTime.UtcNow;
        Task.Run(() =>
        {
            bool installed = ServiceInstaller.IsInstalled();
            bool running = RemoteSensors.IsRunning();
            if (IsDisposed || !IsHandleCreated)
            {
                _probing = false;
                return;
            }
            BeginInvoke((Action)(() =>
            {
                _serviceInstalled = installed;
                _serviceRunning = running;
                _probing = false;
                // Si el servicio aparece o desaparece, el monitor vuelve a leer de donde toca. Nunca reanuda una pausa.
                bool active = _svc.State is MonitorState.Running or MonitorState.Starting or MonitorState.Error;
                if (active && running != _svc.UsingRemoteSensors) Task.Run(() => _svc.Reload());
            }));
        });
    }

    /// <summary>
    /// Instala o quita el servicio. La app se eleva con UAC solo para esa operación; la ventana no espera al proceso,
    /// y el propio proceso elevado muestra el resultado.
    /// </summary>
    void OnServiceClick()
    {
        bool remove = _serviceInstalled;
        var question = remove
            ? "¿Quitar el servicio de sensores? La temperatura de CPU volverá a requerir abrir la app con administrador."
            : "Se instalará un servicio de Windows que lee los sensores con privilegios de sistema, en Program Files.\n\n" +
              "Windows pedirá permiso de administrador una sola vez. La app de bandeja seguirá sin privilegios.";
        if (MessageBox.Show(this, question, "Servicio de sensores", MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
            != DialogResult.OK) return;
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(Environment.ProcessPath!,
                remove ? "--uninstall-service" : "--install-service") { UseShellExecute = true, Verb = "runas" });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "Se canceló la petición de administrador. No se ha cambiado nada.", "TuringLite",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        _lastProbe = DateTime.MinValue; // revisar el estado en cuanto termine la operación
    }

    /// <summary>Lista de componentes con su valor actual (doble clic abre el editor).</summary>
    void RefreshComponents()
    {
        // Actualiza las filas en su sitio: reconstruir la lista cada 2 s haría perder un doble clic en curso.
        var widgets = _svc.Config.Widgets;
        var sensors = _svc.Sensors;
        _components.BeginUpdate();
        if (_components.Items.Count != widgets.Count)
        {
            _components.Items.Clear();
            for (int i = 0; i < widgets.Count; i++) _components.Items.Add(new ListViewItem(new[] { "", "" }));
        }
        for (int i = 0; i < widgets.Count; i++)
        {
            var c = widgets[i];
            var li = _components.Items[i];
            li.Text = TitleOf(c);
            li.ForeColor = c.Enabled ? Ui.Ink : Ui.Dim;
            li.SubItems[1].Text = c.Enabled ? DisplayLayout.ValueText(c, sensors) : "oculto";
        }
        _components.EndUpdate();
    }

    static string TitleOf(WidgetConfig c) => c.Type switch
    {
        "clock" => "Reloj",
        "text" => "Texto",
        _ => c.Label.Length > 0 ? c.Label : "(sin etiqueta)",
    };

    void OpenComponentsEditor()
    {
        using var dlg = new ComponentsForm(_svc);
        if (dlg.ShowDialog(this) == DialogResult.OK) RefreshComponents();
    }

    void Save()
    {
        var c = _svc.Config;
        c.Brightness = _brightness.Value;
        c.Orientation = Orientations[Math.Max(0, _orientation.SelectedIndex)].Key;
        c.Theme = Themes.All[Math.Max(0, _theme.SelectedIndex)].Key;
        c.IntervalMs = (int)_interval.Value;
        c.Gpu = SelectedGpu();

        bool wantAuto = _autostart.Checked;
        bool hasAuto = AutoStart.IsEnabled();
        if (wantAuto != hasAuto && !(wantAuto ? AutoStart.Enable() : AutoStart.Disable()))
            MessageBox.Show("No se pudo cambiar el inicio con Windows.", "TuringLite", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        Cursor = Cursors.WaitCursor;
        try { _svc.SaveAndReload(c); }
        finally { Cursor = Cursors.Default; }
        RefreshStatus();
    }

    string StatusFilePath()
    {
        var file = _svc.Config.Widgets.FirstOrDefault(w => w.Type == "text" && w.File.Length > 0)?.File ?? "status.txt";
        var path = Path.IsPathRooted(file) ? file : Path.Combine(AppContext.BaseDirectory, file);
        if (!File.Exists(path)) File.WriteAllText(path, "");
        return path;
    }

    /// <summary>Abre un archivo en el Bloc de notas. La ruta va como argumento aparte, nunca concatenada en una línea de comandos.</summary>
    static void OpenInNotepad(string path)
    {
        var psi = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
        psi.ArgumentList.Add(path);
        Process.Start(psi);
    }

    void ShowAllSensors()
    {
        var text = _svc.DumpSensors();
        using var mono = new Font("Consolas", 9f);
        using var dlg = new Form
        {
            Text = "Todos los sensores", Icon = AppIcon.Get(), StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(560, 460), MinimizeBox = false,
        };
        var box = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
            Font = mono, Text = text.Replace("\n", Environment.NewLine).Replace("\r\r", "\r"),
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
        };
        var copy = Ui.MakeButton("Copiar al portapapeles", 0, 0, 0, 32, true, () => Clipboard.SetText(text));
        copy.Dock = DockStyle.Bottom;
        copy.FlatAppearance.BorderSize = 0;
        dlg.Controls.Add(box);
        dlg.Controls.Add(copy);
        Ui.Dark(dlg);
        box.BackColor = Ui.Field;
        box.ForeColor = Ui.Ink;
        dlg.ShowDialog(this);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }
}
