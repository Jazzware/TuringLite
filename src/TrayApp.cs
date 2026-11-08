using System.Drawing;
using Microsoft.Win32;

namespace TuringLite;

/// <summary>
/// Vive en la bandeja del sistema. La ventana de ajustes se crea solo al abrirla
/// y se destruye al cerrarla, así no consume memoria mientras no se usa.
/// </summary>
sealed class TrayApp : ApplicationContext
{
    readonly MonitorService _svc;
    readonly NotifyIcon _icon;
    readonly ToolStripMenuItem _pauseItem;
    readonly SynchronizationContext _ui;
    MainForm? _form;

    public TrayApp(MonitorService svc, bool showWindow)
    {
        _svc = svc;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir TuringLite", null, (_, _) => ShowForm());
        _pauseItem = new ToolStripMenuItem("Pausar", null, (_, _) => TogglePause());
        menu.Items.Add(_pauseItem);
        menu.Items.Add("Recargar configuración", null, (_, _) => SafeRun(_svc.Reload));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitApp());

        _ui = new WindowsFormsSynchronizationContext();
        _icon = new NotifyIcon
        {
            Icon = AppIcon.Get(),
            Text = "TuringLite",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowForm();
        _svc.StatusChanged += () => _ui.Post(_ => OnStatus(), null);
        // Apagado, reinicio o cierre de sesión: Windows termina el proceso sin pasar por "Salir".
        SystemEvents.SessionEnded += OnSessionEnded;

        _svc.Start();
        if (showWindow) ShowForm();
    }

    void OnStatus()
    {
        string text = "TuringLite: " + _svc.StatusText;
        _icon.Text = text.Length > 63 ? text[..63] : text;
        _pauseItem.Text = _svc.State == MonitorState.Paused ? "Reanudar" : "Pausar";
        _form?.RefreshStatus();
    }

    public void TogglePause() => SafeRun(() =>
    {
        if (_svc.State == MonitorState.Paused || _svc.State == MonitorState.Stopped) _svc.Resume();
        else _svc.Pause();
    });

    static void SafeRun(Action a)
    {
        try { a(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "TuringLite", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    void ShowForm()
    {
        if (_form == null || _form.IsDisposed)
        {
            _form = new MainForm(_svc, this);
            _form.FormClosed += (_, _) => _form = null;
            _form.Show();
        }
        else
        {
            if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
            _form.Activate();
        }
    }

    /// <summary>
    /// Cierre de sesión o apagado. Apaga la pantalla igual que "Salir", porque después nadie la actualizará.
    /// Se ejecuta en el hilo de la interfaz, dentro del mensaje de cierre de Windows.
    /// </summary>
    void OnSessionEnded(object? sender, SessionEndedEventArgs e) => _svc.Dispose();

    void ExitApp()
    {
        SystemEvents.SessionEnded -= OnSessionEnded;
        _icon.Visible = false;
        _form?.Close();
        _svc.Dispose(); // apaga la pantalla para que no quede congelada con datos viejos
        _icon.Dispose();
        ExitThread();
    }
}

static class AppIcon
{
    static Icon? _cached;

    /// <summary>Usa el icono incrustado; si falla, dibuja uno sencillo.</summary>
    public static Icon Get()
    {
        if (_cached != null) return _cached;
        try
        {
            using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("app.ico");
            if (stream != null) return _cached = new Icon(stream);
        }
        catch { }
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(0x12, 0x16, 0x1C));
            using var b = new SolidBrush(Color.FromArgb(0x7D, 0xD3, 0xA8));
            g.FillRectangle(b, 5, 18, 5, 9);
            g.FillRectangle(b, 13, 11, 5, 16);
            g.FillRectangle(b, 21, 5, 5, 22);
        }
        return _cached = Icon.FromHandle(bmp.GetHicon());
    }
}
