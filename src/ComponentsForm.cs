using System.Drawing;
using LibreHardwareMonitor.Hardware;

namespace TuringLite;

/// <summary>
/// Editor visual de componentes: lista con casillas, propiedades del componente seleccionado,
/// vista previa interactiva del display, plantillas (disposición) y temas (aspecto).
/// Trabaja sobre una copia: nada cambia hasta "Guardar".
/// </summary>
sealed class ComponentsForm : Form
{
    /// <summary>Un componente de la copia de trabajo, con su widget de vista previa.</summary>
    sealed class Item
    {
        public readonly WidgetConfig Cfg;
        public Widget Visual;
        public Item(WidgetConfig cfg, DisplayTheme look) { Cfg = cfg; Visual = Widget.Create(cfg, look); }
    }

    /// <summary>Entrada del selector de sensores. Missing = el sensor no existe en este PC.</summary>
    sealed record SensorOption(string Key, string Text, bool Missing = false)
    {
        public override string ToString() => Text;
    }

    static readonly string[] ClockFormats = { "HH:mm", "HH:mm:ss", "dd/MM/yyyy HH:mm", "dddd d MMMM", "h:mm tt" };

    readonly MonitorService _svc;
    readonly List<Item> _items = new();
    readonly Dictionary<string, SensorInfo> _catalog = new();
    List<SensorInfo> _catalogList = new();
    Item? _sel;
    bool _loading;   // true mientras se rellenan controles por código: los eventos no deben reaccionar
    bool _dirty;
    bool _busy;      // hay una lectura de sensores en curso
    string _themeKey;
    DisplayTheme _look;

    readonly ListView _list = new()
    {
        Location = new Point(12, 96), Size = new Size(300, 520),
        View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, MultiSelect = false,
    };
    readonly Label _caption = new() { Location = new Point(326, 98), Size = new Size(546, 18), AutoSize = false, Font = Ui.SmallFont, ForeColor = Ui.Dim };
    readonly LayoutCanvas _canvas = new() { Location = new Point(326, 118), Size = new Size(546, 498) };
    readonly Panel _props;
    readonly Panel _propsInner = new() { Location = new Point(14, 14), Size = new Size(272, 492), BackColor = Ui.Surface };
    readonly Label _empty = new()
    {
        Location = new Point(14, 14), Size = new Size(272, 40), AutoSize = false, ForeColor = Ui.Dim,
        Text = "Selecciona un componente en la lista o en la vista previa.",
    };
    readonly ComboBox _templateBox = new() { Location = new Point(74, 56), Size = new Size(240, 26), DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox _themeBox = new() { Location = new Point(968, 56), Size = new Size(220, 26), DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Label _status = new() { Location = new Point(12, 632), Size = new Size(900, 20), AutoSize = false, ForeColor = Ui.Dim };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    // --- Propiedades comunes ---
    readonly Label _typeLabel = new() { Location = new Point(0, 0), AutoSize = true, Font = Ui.SectionFont, ForeColor = Ui.Accent };
    readonly CheckBox _enabled = new() { Location = new Point(0, 40), Text = "Mostrar en pantalla", AutoSize = true };
    readonly NumericUpDown _font = Num(0, 200, 0, 326, 90);
    readonly NumericUpDown _x = Num(0, 10000, 0, 400, 90), _y = Num(0, 10000, 110, 400, 90);
    readonly NumericUpDown _w = Num(10, 10000, 0, 448, 90), _h = Num(10, 10000, 110, 448, 90);

    // --- Grupo: sensor ---
    readonly Panel _gSensor = new() { Location = new Point(0, 60), Size = new Size(272, 240) };
    readonly ComboBox _sensorBox = new() { Location = new Point(0, 18), Size = new Size(272, 25), DropDownStyle = ComboBoxStyle.DropDownList };
    readonly TextBox _label = new() { Location = new Point(0, 68), Size = new Size(272, 23) };
    readonly TextBox _unit = new() { Location = new Point(0, 118), Size = new Size(130, 23) };
    readonly NumericUpDown _decimals = Num(0, 3, 150, 118, 70);
    readonly NumericUpDown _warn = Num(0, 100000, 0, 168, 80), _crit = Num(0, 100000, 96, 168, 80), _max = Num(1, 100000, 192, 168, 80);
    readonly CheckBox _bar = new() { Location = new Point(0, 216), Text = "Barra de progreso", AutoSize = true };

    // --- Grupo: reloj ---
    readonly Panel _gClock = new() { Location = new Point(0, 60), Size = new Size(272, 100), Visible = false };
    readonly ComboBox _formatBox = new() { Location = new Point(0, 18), Size = new Size(272, 25) };

    // --- Grupo: texto ---
    readonly Panel _gText = new() { Location = new Point(0, 60), Size = new Size(272, 200), Visible = false };
    readonly TextBox _text = new() { Location = new Point(0, 18), Size = new Size(272, 80), Multiline = true, ScrollBars = ScrollBars.Vertical };
    readonly TextBox _file = new() { Location = new Point(0, 122), Size = new Size(190, 23) };
    readonly Button _browse;

    public ComponentsForm(MonitorService svc)
    {
        _svc = svc;
        _look = Themes.Resolve(svc.Config);
        _themeKey = _look.Key;
        Text = "Editor de componentes";
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(1200, 672);

        _browse = Ui.MakeButton("Examinar", 198, 121, 74, 25, false, BrowseFile);
        _props = Ui.MakeCard(888, 96, 300, 520, "");
        _props.Controls.Add(_empty);
        _props.Controls.Add(_propsInner);
        _propsInner.Visible = false;

        _canvas.Logical = DisplayLayout.LogicalSize(svc.Config.Orientation);
        foreach (var w in svc.Config.Widgets) _items.Add(new Item(w.Clone(), _look));

        BuildToolbar();
        BuildList();
        BuildProperties();
        Controls.Add(_list);
        // Crear el handle ahora (dentro de Quiet) hace que las inserciones posteriores de la lista notifiquen
        // de forma síncrona y cubiertas por Quiet(); si no, ItemChecked llega al mostrar el formulario.
        Quiet(() => _ = _list.Handle);
        Controls.Add(_caption);
        Controls.Add(_canvas);
        Controls.Add(_props);
        Controls.Add(_status);
        Btn("Cancelar", 958, 626, 94, Close);
        Btn("Guardar y aplicar", 1060, 626, 128, Save, primary: true);

        _canvas.SelectionChanged += c => Select(_items.FirstOrDefault(i => i.Cfg == c));
        _canvas.GeometryChanged += OnCanvasMoved;
        _canvas.DeleteRequested += RemoveSelected;

        _timer.Tick += OnTick;
        _timer.Start();

        Ui.Dark(this);
        RefreshList();
        UpdateCaption();
        UpdateStatus();
    }

    // ---------------------------------------------------------------- construcción de controles

    void BuildToolbar()
    {
        Controls.Add(Ui.MakeLabel("Añadir", 12, 21, dim: true));
        Btn("+ Sensor", 62, 14, 96, () => AddItem(NewSensor()));
        Btn("+ Reloj", 164, 14, 88, () => AddItem(new WidgetConfig { Type = "clock", Format = "HH:mm", X = 10, Y = 10, W = 200, H = 64 }));
        Btn("+ Texto", 258, 14, 88, () => AddItem(new WidgetConfig { Type = "text", Text = "Texto", X = 10, Y = 10, W = 250, H = 60 }));
        Controls.Add(Ui.MakeLabel("Editar", 366, 21, dim: true));
        Btn("Quitar", 414, 14, 84, RemoveSelected);
        Btn("Duplicar", 504, 14, 96, Duplicate);
        Btn("▲ Subir", 606, 14, 90, () => MoveItem(1));
        Btn("▼ Bajar", 702, 14, 90, () => MoveItem(-1));

        Controls.Add(Ui.MakeLabel("Plantilla", 12, 61, dim: true));
        foreach (var t in DisplayLayout.Templates) _templateBox.Items.Add(t.Name);
        _templateBox.SelectedIndex = 0;
        Controls.Add(_templateBox);
        Btn("Aplicar plantilla", 322, 54, 128, ApplyTemplate);

        Controls.Add(Ui.MakeLabel("Tema", 920, 61, dim: true));
        Quiet(() =>
        {
            foreach (var t in Themes.All) _themeBox.Items.Add(t.Name);
            _themeBox.SelectedIndex = Themes.IndexOf(_themeKey);
        });
        _themeBox.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _themeBox.SelectedIndex >= 0) SetTheme(Themes.All[_themeBox.SelectedIndex].Key);
        };
        Controls.Add(_themeBox);
    }

    void BuildList()
    {
        _list.Columns.Add("Componente", 208);
        _list.Columns.Add("Valor", 90, HorizontalAlignment.Right);
        // ListView notifica estos eventos de forma asíncrona: pueden llegar fuera de Quiet(), así que
        // cada handler comprueba si el valor cambió de verdad antes de actuar.
        _list.ItemChecked += (_, e) =>
        {
            if (_loading || e.Item.Tag is not Item it || it.Cfg.Enabled == e.Item.Checked) return;
            it.Cfg.Enabled = e.Item.Checked;
            MarkDirty();
            if (it == _sel) Quiet(() => _enabled.Checked = it.Cfg.Enabled);
            Recompose();
        };
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            var it = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as Item : null;
            if (it != _sel) Select(it);
        };
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
    }

    void BuildProperties()
    {
        var p = _propsInner;
        p.Controls.Add(_typeLabel);
        p.Controls.Add(_enabled);
        _enabled.CheckedChanged += (_, _) => Edit(c => c.Enabled = _enabled.Checked);

        // Sensor
        _gSensor.Controls.Add(Lbl("Sensor", 0, 0));
        _gSensor.Controls.Add(_sensorBox);
        _gSensor.Controls.Add(Lbl("Etiqueta", 0, 50));
        _gSensor.Controls.Add(_label);
        _gSensor.Controls.Add(Lbl("Unidad", 0, 100));
        _gSensor.Controls.Add(_unit);
        _gSensor.Controls.Add(Lbl("Decimales", 150, 100));
        _gSensor.Controls.Add(_decimals);
        _gSensor.Controls.Add(Lbl("Aviso", 0, 150));
        _gSensor.Controls.Add(_warn);
        _gSensor.Controls.Add(Lbl("Crítico", 96, 150));
        _gSensor.Controls.Add(_crit);
        _gSensor.Controls.Add(Lbl("Máx. barra", 192, 150));
        _gSensor.Controls.Add(_max);
        _gSensor.Controls.Add(Lbl("0 = sin umbral de aviso o crítico", 0, 196, dim: true));
        _gSensor.Controls.Add(_bar);
        p.Controls.Add(_gSensor);

        _sensorBox.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _sel == null || _sensorBox.SelectedItem is not SensorOption o || o.Missing) return;
            var it = _sel;
            _svc.Sensors?.Track(o.Key);
            Edit(c =>
            {
                c.Sensor = o.Key;
                if (_catalog.TryGetValue(o.Key, out var info))
                {
                    c.Label = info.Name;
                    c.Unit = info.Unit;
                    if (info.Type is SensorType.Load or SensorType.Control or SensorType.Level) { c.Bar = true; c.Max = 100; }
                }
            });
            LoadProps(it);
        };
        _label.TextChanged += (_, _) => Edit(c => c.Label = _label.Text);
        _unit.TextChanged += (_, _) => Edit(c => c.Unit = _unit.Text);
        _decimals.ValueChanged += (_, _) => Edit(c => c.Decimals = (int)_decimals.Value);
        _warn.ValueChanged += (_, _) => Edit(c => c.Warn = _warn.Value == 0 ? null : (double)_warn.Value);
        _crit.ValueChanged += (_, _) => Edit(c => c.Crit = _crit.Value == 0 ? null : (double)_crit.Value);
        _max.ValueChanged += (_, _) => Edit(c => c.Max = (double)_max.Value);
        _bar.CheckedChanged += (_, _) => Edit(c => c.Bar = _bar.Checked);

        // Reloj
        _gClock.Controls.Add(Lbl("Formato de hora", 0, 0));
        _formatBox.Items.AddRange(ClockFormats);
        _gClock.Controls.Add(_formatBox);
        _gClock.Controls.Add(Lbl("Ej.: HH:mm  ·  HH:mm:ss  ·  dd/MM/yyyy", 0, 50, dim: true));
        p.Controls.Add(_gClock);
        _formatBox.TextChanged += (_, _) =>
        {
            if (_loading || _sel == null) return;
            var f = _formatBox.Text.Trim();
            if (f.Length == 0) return;
            // Un formato inválido (p. ej. «HH:mm\») no se aplica: antes provocaba una excepción en la vista previa.
            if (!ClockFormat.IsValid(f))
            {
                _formatBox.ForeColor = Ui.Danger;
                _status.Text = $"Formato de hora no válido: «{f}». Se mantiene el anterior.";
                _status.ForeColor = Ui.Danger;
                return;
            }
            _formatBox.ForeColor = Ui.Ink;
            _status.ForeColor = Ui.Dim;
            Edit(c => c.Format = f);
        };

        // Texto
        _gText.Controls.Add(Lbl("Texto", 0, 0));
        _gText.Controls.Add(_text);
        _gText.Controls.Add(Lbl("Archivo (opcional; si existe, se muestra su contenido)", 0, 104, dim: true));
        _gText.Controls.Add(_file);
        _gText.Controls.Add(_browse);
        p.Controls.Add(_gText);
        _text.TextChanged += (_, _) => Edit(c => c.Text = _text.Text);
        _file.TextChanged += (_, _) => Edit(c => c.File = _file.Text.Trim());

        // Común
        p.Controls.Add(Lbl("Tamaño de fuente (0 = automático)", 0, 306));
        p.Controls.Add(_font);
        _font.ValueChanged += (_, _) => Edit(c => c.FontSize = (float)_font.Value);
        p.Controls.Add(Lbl("Posición y tamaño (píxeles)", 0, 360));
        p.Controls.Add(Lbl("X", 0, 380));
        p.Controls.Add(_x);
        p.Controls.Add(Lbl("Y", 110, 380));
        p.Controls.Add(_y);
        p.Controls.Add(Lbl("Ancho", 0, 428));
        p.Controls.Add(_w);
        p.Controls.Add(Lbl("Alto", 110, 428));
        p.Controls.Add(_h);
        _x.ValueChanged += (_, _) => Edit(c => c.X = (int)_x.Value);
        _y.ValueChanged += (_, _) => Edit(c => c.Y = (int)_y.Value);
        _w.ValueChanged += (_, _) => Edit(c => c.W = (int)_w.Value);
        _h.ValueChanged += (_, _) => Edit(c => c.H = (int)_h.Value);
    }

    Button Btn(string text, int x, int y, int w, Action onClick, bool primary = false)
    {
        var b = Ui.MakeButton(text, x, y, w, 30, primary, onClick);
        Controls.Add(b);
        return b;
    }

    static Label Lbl(string text, int x, int y, bool dim = false) => Ui.MakeLabel(text, x, y, dim);

    static NumericUpDown Num(int min, int max, int x, int y, int w) =>
        new() { Minimum = min, Maximum = max, Location = new Point(x, y), Width = w };

    // ---------------------------------------------------------------- carga inicial

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _status.Text = "Leyendo sensores del equipo...";
        try
        {
            // EnsureSensors tarda 1-2 s: se hace en segundo plano para no congelar la ventana.
            var sensors = await Task.Run(() => _svc.EnsureSensors());
            if (IsDisposed) return;
            _catalogList = sensors.Catalog();
            _catalog.Clear();
            foreach (var info in _catalogList) _catalog[info.Key] = info;
            BuildSensorChoices();
            Canonicalize();
            TrackAll();
            RefreshList();
            LoadProps(_sel);
        }
        catch (Exception ex)
        {
            if (!IsDisposed) _status.Text = "No se pudieron leer los sensores: " + ex.Message;
            return;
        }
        UpdateStatus();
    }

    void BuildSensorChoices()
    {
        Quiet(() =>
        {
            _sensorBox.Items.Clear();
            foreach (var info in _catalogList) _sensorBox.Items.Add(new SensorOption(info.Key, info.Display));
        });
    }

    /// <summary>Convierte alias antiguos ("cpu.temp") en el identificador real de este PC, para mostrar nombres reales.</summary>
    void Canonicalize()
    {
        foreach (var it in _items)
            if (it.Cfg.Type == "sensor") it.Cfg.Sensor = Canon(it.Cfg.Sensor);
    }

    string Canon(string key) => _svc.Sensors?.Canonical(key) ?? key;

    void TrackAll()
    {
        foreach (var it in _items)
            if (it.Cfg.Type == "sensor") _svc.Sensors?.Track(it.Cfg.Sensor);
    }

    // ---------------------------------------------------------------- selección y propiedades

    void Select(Item? it)
    {
        _sel = it;
        _canvas.Selected = it?.Cfg;
        Quiet(() =>
        {
            foreach (ListViewItem li in _list.Items)
            {
                li.Selected = li.Tag == it;
                if (li.Selected) li.EnsureVisible();
            }
        });
        LoadProps(it);
    }

    void LoadProps(Item? it)
    {
        Quiet(() =>
        {
            // Sin selección se muestra una pista, no un panel deshabilitado: los controles apagados se leen mal en oscuro.
            _empty.Visible = it == null;
            _propsInner.Visible = it != null;
            if (it == null) return;
            var c = it.Cfg;
            _typeLabel.Text = "Tipo: " + TypeName(c.Type);
            _gSensor.Visible = c.Type == "sensor";
            _gClock.Visible = c.Type == "clock";
            _gText.Visible = c.Type == "text";
            _enabled.Checked = c.Enabled;

            switch (c.Type)
            {
                case "clock":
                    _formatBox.Text = c.Format;
                    break;
                case "text":
                    _text.Text = c.Text;
                    _file.Text = c.File;
                    break;
                default:
                    SelectSensorOption(c.Sensor);
                    _label.Text = c.Label;
                    _unit.Text = c.Unit;
                    SetNum(_decimals, c.Decimals);
                    SetNum(_warn, c.Warn ?? 0);
                    SetNum(_crit, c.Crit ?? 0);
                    SetNum(_max, c.Max);
                    _bar.Checked = c.Bar;
                    break;
            }
            SetNum(_font, c.FontSize);
            LoadGeometry(it);
        });
    }

    void SelectSensorOption(string key)
    {
        for (int i = _sensorBox.Items.Count - 1; i >= 0; i--)
            if (_sensorBox.Items[i] is SensorOption { Missing: true }) _sensorBox.Items.RemoveAt(i);

        int idx = -1;
        for (int i = 0; i < _sensorBox.Items.Count; i++)
            if (_sensorBox.Items[i] is SensorOption o && o.Key == key) { idx = i; break; }
        if (idx < 0 && key.Length > 0)
        {
            // Sensor que no existe en este PC: se conserva para no perder la configuración, pero se avisa.
            _sensorBox.Items.Insert(0, new SensorOption(key, $"(no disponible en este PC) {key}", Missing: true));
            idx = 0;
        }
        _sensorBox.SelectedIndex = idx;
    }

    void LoadGeometry(Item it)
    {
        var c = it.Cfg;
        Quiet(() =>
        {
            SetNum(_x, c.X);
            SetNum(_y, c.Y);
            SetNum(_w, c.W);
            SetNum(_h, c.H);
        });
    }

    static void SetNum(NumericUpDown n, double v) => n.Value = (decimal)Math.Clamp(v, (double)n.Minimum, (double)n.Maximum);

    static string TypeName(string type) => type switch
    {
        "clock" => "Reloj",
        "text" => "Texto",
        _ => "Sensor",
    };

    /// <summary>Aplica un cambio al componente seleccionado y refresca la vista previa.</summary>
    void Edit(Action<WidgetConfig> change)
    {
        if (_loading || _sel == null) return;
        var it = _sel;
        change(it.Cfg);
        DisplayLayout.Clamp(it.Cfg, _canvas.Logical);
        Rebuild(it);
        MarkDirty();
        RefreshRow(it);
        LoadGeometry(it);   // por si el valor se ajustó a los límites de la pantalla
        Recompose();
    }

    void OnCanvasMoved()
    {
        if (_sel == null) return;
        var c = _sel.Cfg;
        _sel.Visual.Bounds = new Rectangle(c.X, c.Y, c.W, c.H);
        LoadGeometry(_sel);
        MarkDirty();
        Recompose();
    }

    void BrowseFile()
    {
        using var dlg = new OpenFileDialog { Filter = "Archivos de texto (*.txt)|*.txt|Todos los archivos|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var baseDir = AppContext.BaseDirectory;
        // Ruta relativa si está junto al programa, así el diseño sigue funcionando al moverlo.
        _file.Text = dlg.FileName.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(baseDir, dlg.FileName)
            : dlg.FileName;
    }

    // ---------------------------------------------------------------- lista

    void RefreshList()
    {
        Quiet(() =>
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var it in _items)
            {
                var li = new ListViewItem(Title(it.Cfg)) { Tag = it, Checked = it.Cfg.Enabled };
                li.SubItems.Add(ValueText(it.Cfg));
                if (it == _sel) li.Selected = true;
                _list.Items.Add(li);
            }
            _list.EndUpdate();
        });
        _canvas.Items = _items.Select(i => i.Cfg).ToList();
        Recompose();
        UpdateStatus();
    }

    void RefreshRow(Item it)
    {
        foreach (ListViewItem li in _list.Items)
        {
            if (li.Tag != it) continue;
            Quiet(() =>
            {
                li.Text = Title(it.Cfg);
                li.Checked = it.Cfg.Enabled;
                li.SubItems[1].Text = ValueText(it.Cfg);
            });
            return;
        }
    }

    void UpdateValues()
    {
        foreach (ListViewItem li in _list.Items)
            if (li.Tag is Item it) li.SubItems[1].Text = ValueText(it.Cfg);
    }

    string Title(WidgetConfig c) => c.Type switch
    {
        "clock" => "Reloj",
        "text" => c.File.Length > 0 ? "Texto: " + c.File : "Texto: " + Trunc(c.Text),
        _ => c.Label.Length > 0 ? c.Label : NameOf(c.Sensor),
    };

    string NameOf(string key) => _catalog.TryGetValue(key, out var info) ? info.Name : (key.Length > 0 ? key : "(sin sensor)");

    static string Trunc(string s) => s.Length > 24 ? s[..24] + "…" : s;

    string ValueText(WidgetConfig c) => DisplayLayout.ValueText(c, _svc.Sensors);

    // ---------------------------------------------------------------- acciones de la barra

    WidgetConfig NewSensor()
    {
        var c = new WidgetConfig { Type = "sensor", X = 10, Y = 10, W = 150, H = 100, FontSize = 26, Max = 100 };
        var pick = _catalogList.FirstOrDefault(i => i.Type == SensorType.Temperature) ?? _catalogList.FirstOrDefault();
        if (pick != null) { c.Sensor = pick.Key; c.Label = pick.Name; c.Unit = pick.Unit; }
        return c;
    }

    void AddItem(WidgetConfig c)
    {
        var it = new Item(c, _look);
        _items.Add(it);
        if (c.Type == "sensor") _svc.Sensors?.Track(c.Sensor);
        MarkDirty();
        RefreshList();
        Select(it);
    }

    void RemoveSelected()
    {
        if (_sel == null) return;
        var it = _sel;
        _items.Remove(it);
        it.Visual.Dispose();
        MarkDirty();
        Select(null);
        RefreshList();
    }

    void Duplicate()
    {
        if (_sel == null) return;
        var c = _sel.Cfg.Clone();
        c.X += 10;
        c.Y += 10;
        DisplayLayout.Clamp(c, _canvas.Logical);
        AddItem(c);
    }

    /// <summary>Cambia el orden de dibujo: lo último de la lista queda encima.</summary>
    void MoveItem(int delta)
    {
        if (_sel == null) return;
        int i = _items.IndexOf(_sel), j = i + delta;
        if (j < 0 || j >= _items.Count) return;
        (_items[i], _items[j]) = (_items[j], _items[i]);
        MarkDirty();
        RefreshList();
        Select(_sel);
    }

    void ApplyTemplate()
    {
        if (_templateBox.SelectedIndex < 0) return;
        var r = MessageBox.Show(this,
            "Se reemplazarán todos los componentes por la plantilla. Si cambias de idea, usa «Cancelar» para descartar.",
            "Plantilla", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (r != DialogResult.OK) return;

        var configs = DisplayLayout.Templates[_templateBox.SelectedIndex].Build(_canvas.Logical);
        foreach (var it in _items) it.Visual.Dispose();
        _items.Clear();
        foreach (var c in configs) _items.Add(new Item(c, _look));
        Canonicalize();
        TrackAll();
        MarkDirty();
        Select(null);
        RefreshList();
    }

    /// <summary>Cambia el tema de la vista previa. Los componentes se reconstruyen con el nuevo aspecto.</summary>
    void SetTheme(string key)
    {
        if (key == _themeKey) return;
        _themeKey = key;
        _look = Themes.Find(key, _svc.Config.Background);
        foreach (var it in _items) Rebuild(it);
        UpdateCaption();
        MarkDirty();
        Recompose();
    }

    // ---------------------------------------------------------------- vista previa, estado y guardado

    /// <summary>Redibuja la vista previa con los valores actuales (solo componentes visibles).</summary>
    void Recompose()
    {
        var visible = _items.Where(i => i.Cfg.Enabled).Select(i => i.Visual);
        _canvas.Frame = DisplayLayout.Compose(visible, _svc.Sensors, _canvas.Logical, _look);
    }

    void Rebuild(Item it)
    {
        it.Visual.Dispose();
        it.Visual = Widget.Create(it.Cfg, _look);
    }

    void UpdateCaption() =>
        _caption.Text = $"Vista previa · {_canvas.Logical.Width} × {_canvas.Logical.Height} px · {_look.Description}";

    void MarkDirty()
    {
        _dirty = true;
        UpdateStatus();
    }

    void UpdateStatus()
    {
        int visible = _items.Count(i => i.Cfg.Enabled);
        string sensors = _svc.Sensors == null ? "leyendo sensores" : $"{_catalogList.Count} sensores detectados";
        _status.ForeColor = Ui.Dim;
        _status.Text = (_dirty ? "● Cambios sin guardar · " : "")
                       + $"{visible} de {_items.Count} visibles · {sensors}";
    }

    async void OnTick(object? sender, EventArgs e)
    {
        if (_busy || IsDisposed) return;
        _busy = true;
        try
        {
            // Con el monitor en marcha, él actualiza los sensores; en pausa, los leemos desde aquí.
            var s = _svc.Sensors;
            if (s != null && _svc.State != MonitorState.Running) await Task.Run(s.Update);
        }
        catch (ObjectDisposedException) { }
        finally { _busy = false; }
        if (IsDisposed) return;
        UpdateValues();
        Recompose();
    }

    void Save()
    {
        foreach (var it in _items)
        {
            DisplayLayout.Clamp(it.Cfg, _canvas.Logical);
            if (it.Cfg.Type == "sensor") it.Cfg.Sensor = Canon(it.Cfg.Sensor);
        }

        int sinSensor = _items.Count(i => i.Cfg.Type == "sensor" && i.Cfg.Sensor.Length == 0);
        if (sinSensor > 0)
        {
            MessageBox.Show(this, $"{sinSensor} componente(s) de sensor no tienen sensor asignado.",
                "Componentes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int missing = _items.Count(i => i.Cfg.Enabled && i.Cfg.Type == "sensor" && _svc.Sensors?.Track(i.Cfg.Sensor) != true);
        if (missing > 0 && MessageBox.Show(this,
                $"{missing} sensor(es) visibles no existen en este PC y se mostrarán como «--». ¿Guardar de todos modos?",
                "Componentes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        var cfg = _svc.Config;
        cfg.Widgets = _items.Select(i => i.Cfg).ToList();
        cfg.Theme = _themeKey;
        Cursor = Cursors.WaitCursor;
        try
        {
            _svc.SaveAndReload(cfg);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Componentes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        finally { Cursor = Cursors.Default; }

        _dirty = false;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>Ejecuta una acción sin que los eventos de los controles reaccionen a los cambios por código.</summary>
    void Quiet(Action action)
    {
        bool prev = _loading;
        _loading = true;
        try { action(); }
        finally { _loading = prev; }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty && DialogResult != DialogResult.OK &&
            MessageBox.Show(this, "Hay cambios sin guardar. ¿Descartarlos?", "Componentes",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            e.Cancel = true;
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        foreach (var it in _items) it.Visual.Dispose();
        _canvas.Frame = null;
        base.OnFormClosed(e);
    }
}
