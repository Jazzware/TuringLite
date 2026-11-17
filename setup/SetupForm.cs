using System.Diagnostics;
using System.Drawing;

namespace TuringLite.Setup;

/// <summary>Ventana del instalador: qué instalar, progreso y resultado. La instalación corre fuera del hilo de la ventana.</summary>
sealed class SetupForm : Form
{
    // Sin TabStop: si no, al abrir recibe el foco y la ruta aparece seleccionada.
    readonly TextBox _dir = new() { ReadOnly = true, Width = 400, TabStop = false };
    readonly CheckBox _shortcut = new() { Text = "Crear acceso directo en el menú Inicio", Checked = true, AutoSize = true };
    readonly CheckBox _autostart = new() { Text = "Iniciar TuringLite con Windows", Checked = true, AutoSize = true };
    readonly CheckBox _service = new()
    {
        Text = "Instalar el servicio de sensores (temperatura de CPU; pide permiso de administrador una vez)",
        // Tamaño fijo de dos líneas: en .NET Framework una casilla con AutoSize no parte el texto y lo corta.
        Checked = true, AutoSize = false, Size = new Size(420, 40),
        CheckAlign = ContentAlignment.TopLeft, TextAlign = ContentAlignment.TopLeft,
    };
    readonly CheckBox _launch = new() { Text = "Abrir TuringLite al terminar", Checked = true, AutoSize = true };
    readonly Label _status = new() { AutoSize = false, Size = new Size(400, 40) };
    readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Size = new Size(400, 12), Visible = false };
    readonly Button _install;
    readonly Button _cancel;
    bool _done;

    public SetupForm()
    {
        Text = "Instalar TuringLite " + Installer.Version;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(460, 410);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        _dir.Text = Installer.DefaultDir;
        var title = new Label { Text = "Instalar TuringLite " + Installer.Version, AutoSize = true, Font = new Font("Segoe UI Semibold", 15f), Location = new Point(20, 16) };
        var intro = new Label
        {
            Text = "Se instala para tu usuario, sin necesidad de administrador (salvo el servicio opcional).",
            AutoSize = false, Size = new Size(420, 36), Location = new Point(20, 56), ForeColor = SystemColors.GrayText,
        };
        var folderLabel = new Label { Text = "Carpeta:", AutoSize = true, Location = new Point(20, 100) };
        _dir.Location = new Point(20, 122);
        _shortcut.Location = new Point(20, 162);
        _autostart.Location = new Point(20, 190);
        _service.Location = new Point(20, 218);
        _launch.Location = new Point(20, 262);
        _status.Location = new Point(20, 300);
        _progress.Location = new Point(20, 346);

        _install = new Button { Text = "Instalar", Location = new Point(250, 362), Size = new Size(90, 30) };
        _cancel = new Button { Text = "Cancelar", Location = new Point(350, 362), Size = new Size(90, 30) };
        _install.Click += async (_, _) =>
        {
            if (_done) Close();
            else await InstallAsync();
        };
        _cancel.Click += (_, _) => Close();
        AcceptButton = _install;
        CancelButton = _cancel;

        Controls.AddRange(new Control[] { title, intro, folderLabel, _dir, _shortcut, _autostart, _service, _launch,
            _status, _progress, _install, _cancel });

        // Sin .NET 8 Desktop Runtime la app no arranca: no se instala nada y se ofrece la página oficial.
        if (!Installer.HasDesktopRuntime())
        {
            _status.Text = "Falta el .NET 8 Desktop Runtime (x64), necesario para TuringLite. " +
                           "Instálalo y vuelve a abrir este instalador.";
            _install.Enabled = false;
            var download = new Button { Text = "Descargar .NET 8", Location = new Point(20, 362), Size = new Size(160, 30) };
            download.Click += (_, _) =>
            {
                Process.Start(new ProcessStartInfo(Installer.RuntimeUrl) { UseShellExecute = true })?.Dispose();
            };
            Controls.Add(download);
        }
    }

    async Task InstallAsync()
    {
        var dir = _dir.Text;
        if (Installer.AppRunningFrom(dir))
        {
            MessageBox.Show(this, "TuringLite sigue abierto. Ciérralo desde la bandeja del sistema (Salir) y vuelve a intentarlo.",
                "Instalar TuringLite", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var options = new SetupOptions(dir, _shortcut.Checked, _autostart.Checked, _service.Checked, _launch.Checked);
        _install.Enabled = false;
        _shortcut.Enabled = _autostart.Enabled = _service.Enabled = _launch.Enabled = false;
        _progress.Visible = true;

        string? error = null;
        List<string> warnings = new();
        try
        {
            warnings = await Task.Run(() => Installer.Run(options, text => BeginInvoke((Action)(() => _status.Text = text))));
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        _progress.Visible = false;
        if (error != null)
        {
            _status.Text = "No se pudo instalar: " + error;
            _install.Enabled = true;
            _shortcut.Enabled = _autostart.Enabled = _service.Enabled = _launch.Enabled = true;
            return;
        }
        _status.Text = warnings.Count == 0
            ? "TuringLite se ha instalado correctamente."
            : "TuringLite se ha instalado, con avisos:\n" + string.Join("\n", warnings);
        _done = true;
        _install.Text = "Cerrar";
        _install.Enabled = true;
        _cancel.Enabled = false;
    }
}
