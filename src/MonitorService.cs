using System.Drawing;
using System.Drawing.Imaging;

namespace TuringLite;

public enum MonitorState { Stopped, Starting, Running, Paused, Error }

/// <summary>
/// Bucle de monitoreo en un hilo de fondo. La interfaz solo lo controla (iniciar, pausar, recargar)
/// y lee su estado; nunca dibuja ni lee sensores desde el hilo de la UI.
/// </summary>
public sealed class MonitorService : IDisposable
{
    public string ConfigPath { get; }
    public AppConfig Config { get; private set; }
    public MonitorState State { get; private set; } = MonitorState.Stopped;
    public string StatusText { get; private set; } = "Detenido";
    public event Action? StatusChanged;

    readonly object _gate = new();
    DisplayTheme _theme = Themes.All[0];
    RevADisplay? _display;
    ISensorSource? _sensors;
    List<Widget> _widgets = new();
    CancellationTokenSource? _cts;
    Task? _loop;
    byte[] _buffer = Array.Empty<byte>();

    public MonitorService(string configPath)
    {
        ConfigPath = configPath;
        Config = AppConfig.Load(configPath);
    }

    void SetStatus(MonitorState state, string text)
    {
        State = state;
        StatusText = text;
        try { StatusChanged?.Invoke(); } catch { /* la UI no debe tumbar el servicio */ }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_loop != null) return;
            _theme = Themes.Resolve(Config);
            foreach (var old in _widgets) old.Dispose();
            _widgets = Config.Widgets.Where(w => w.Enabled).Select(w => Widget.Create(w, _theme)).ToList();
            _display = new RevADisplay(Config.Port) { MaxRegionWidth = Config.MaxRegionWidth };
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(() => Loop(token));
        }
    }

    /// <summary>Detiene el bucle y espera a que termine.</summary>
    public void Stop(bool screenOff = false)
    {
        Task? loop;
        lock (_gate)
        {
            loop = _loop;
            _cts?.Cancel();
        }
        try { loop?.Wait(TimeSpan.FromSeconds(5)); } catch { }
        lock (_gate)
        {
            if (screenOff) { try { _display?.ScreenOff(); } catch { } }
            _display?.Dispose();
            _display = null;
            _cts?.Dispose();
            _cts = null;
            _loop = null;
        }
        SetStatus(MonitorState.Stopped, "Detenido");
    }

    public void Pause()
    {
        Stop(screenOff: true);
        SetStatus(MonitorState.Paused, "En pausa (pantalla apagada)");
    }

    public void Resume() => Start();

    /// <summary>Relee config.json y reinicia. Los sensores se recrean por si cambió la GPU.</summary>
    public void Reload()
    {
        Stop();
        Config = AppConfig.Load(ConfigPath);
        lock (_gate) { _sensors?.Dispose(); _sensors = null; }
        Start();
    }

    public void SaveAndReload(AppConfig cfg)
    {
        cfg.Save(ConfigPath);
        Reload();
    }

    /// <summary>Cambio de brillo inmediato, sin reiniciar.</summary>
    public void SetBrightness(int level)
    {
        Config.Brightness = level;
        try { if (State == MonitorState.Running) _display?.SetBrightness(level); } catch { }
    }

    public string DumpSensors() => _sensors?.DumpAll() ?? "Los sensores todavía no se han inicializado.";

    /// <summary>Sensores en uso, o null si aún no se han leído.</summary>
    public ISensorSource? Sensors => _sensors;

    /// <summary>true si las lecturas llegan del servicio de sensores (y no de este proceso).</summary>
    public bool UsingRemoteSensors => _sensors is RemoteSensors;

    /// <summary>
    /// Crea los sensores si todavía no existen (también con el monitor en pausa, para el editor).
    /// Es lento (1-2 s): llamar desde un hilo de fondo.
    /// </summary>
    public ISensorSource EnsureSensors()
    {
        lock (_gate) { if (_sensors != null) return _sensors; }
        var created = CreateSensors();            // fuera del lock: no bloquea Pausar/Recargar
        lock (_gate)
        {
            if (_sensors == null) { _sensors = created; return created; }
            var existing = _sensors;
            created.Dispose();
            return existing;
        }
    }

    /// <summary>
    /// Con el servicio de sensores en marcha, las lecturas (también la CPU) llegan de él y esta app no necesita
    /// privilegios. Sin servicio, se leen en este proceso como antes (la CPU solo con administrador).
    /// </summary>
    ISensorSource CreateSensors()
    {
        var remote = RemoteSensors.TryConnect(Config.Gpu);
        if (remote != null) return remote;
        var keys = Config.Widgets.Where(w => w.Enabled && w.Type == "sensor").Select(w => w.Sensor).ToList();
        return new TuringLite.Sensors(keys, Config.Gpu); // TuringLite.Sensors: la propiedad Sensors tapa el nombre de la clase
    }

    void Loop(CancellationToken token)
    {
        bool ready = false;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_sensors == null) SetStatus(MonitorState.Starting, "Leyendo sensores...");
                var sensors = EnsureSensors();
                var display = _display ?? throw new InvalidOperationException("Sin pantalla.");
                if (!ready)
                {
                    SetStatus(MonitorState.Starting, "Conectando con la pantalla...");
                    Init(display);
                    ready = true;
                    SetStatus(MonitorState.Running, $"Funcionando en {display.PortName}");
                }

                sensors.Update();
                var now = DateTime.Now;
                foreach (var w in _widgets)
                {
                    if (token.IsCancellationRequested) break;
                    var sig = w.Signature(now, sensors);
                    bool stale = Config.RefreshSeconds > 0 && (now - w.LastSent).TotalSeconds >= Config.RefreshSeconds;
                    if (sig == w.LastSignature && !stale) continue; // sin cambios: nada que enviar
                    int n = Raster.Render(w, ref _buffer);
                    display.DrawRegion(w.Bounds.X, w.Bounds.Y, w.Bounds.Width, w.Bounds.Height, _buffer, n);
                    w.LastSignature = sig;
                    w.LastSent = now;
                    Thread.Sleep(15);
                }
                token.WaitHandle.WaitOne(Config.IntervalMs);
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                ready = false;
                try { _display?.Close(); } catch { }
                SetStatus(MonitorState.Error, $"Sin conexión: {ex.Message} Reintentando...");
                token.WaitHandle.WaitOne(3000);
            }
            catch { break; }
        }
    }

    void Init(RevADisplay display)
    {
        display.Open();
        display.ScreenOn();
        display.Clear();
        display.SetOrientation(Config.ParsedOrientation);
        display.SetBrightness(Config.Brightness);
        using var bg = new Bitmap(display.Width, display.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bg)) g.Clear(_theme.Background);
        int n = Raster.ToRgb565(bg, ref _buffer);
        display.DrawRegion(0, 0, display.Width, display.Height, _buffer, n);
        Thread.Sleep(400); // la pantalla necesita asimilar el fondo completo
        foreach (var w in _widgets) { w.LastSignature = null; w.LastSent = DateTime.MinValue; }
    }

    /// <summary>
    /// Detiene el monitoreo y dibuja filas de prueba con distintos anchos usando el mismo envío
    /// que el monitor (franjas incluidas). Si todas las filas se leen bien, los widgets también.
    /// </summary>
    public void RunTestPattern()
    {
        Stop();
        Task.Run(() =>
        {
            try
            {
                // Mismo envío que el monitor: sin franjas, el firmware corrompe ciertos anchos (p. ej. 250 px).
                using var display = new RevADisplay(Config.Port) { MaxRegionWidth = Config.MaxRegionWidth };
                display.Open();
                display.ScreenOn();
                display.Clear();
                display.SetOrientation(Config.ParsedOrientation);
                display.SetBrightness(Config.Brightness);
                int W = display.Width;
                byte[] buf = Array.Empty<byte>();

                // El comando "limpiar" no borra en esta pantalla: pintamos el fondo completo
                // para que no queden restos del monitor detrás de las filas.
                using (var bg = new Bitmap(W, display.Height, PixelFormat.Format24bppRgb))
                {
                    using (var g = Graphics.FromImage(bg)) g.Clear(Color.Black);
                    int nbg = Raster.ToRgb565(bg, ref buf);
                    display.DrawRegion(0, 0, W, display.Height, buf, nbg);
                }
                Thread.Sleep(400);

                // Un ancho por cada residuo al dividir entre 8 (0..7): cubre todos los casos del firmware.
                var rows = Enumerable.Range(0, 8).Select(r => (x: 0, w: 248 + r)).ToArray();
                int rh = display.Height / rows.Length;
                using var font = new Font("Segoe UI", 10f);
                for (int i = 0; i < rows.Length; i++)
                {
                    var (x, w) = rows[i];
                    w = Math.Min(w, W - x);
                    using var bmp = new Bitmap(w, rh, PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);
                        g.DrawRectangle(Pens.Red, 0, 0, w - 1, rh - 1);
                        string strips = string.Join(" + ", RevADisplay.StripWidths(w, display.MaxRegionWidth));
                        g.DrawString($"{i + 1}: w={w} ({strips})  ABCDEFGHIJKLMNOPQRSTUVWXYZ 0123456789", font, Brushes.Black,
                            new RectangleF(3, 3, w - 6, rh - 6));
                    }
                    int n = Raster.ToRgb565(bmp, ref buf);
                    display.DrawRegion(x, i * rh, w, rh, buf, n);
                    Thread.Sleep(100);
                }
                SetStatus(MonitorState.Paused, "Patrón de prueba en pantalla. Pulsa Reanudar para volver.");
            }
            catch (Exception ex)
            {
                SetStatus(MonitorState.Error, $"No se pudo dibujar el patrón: {ex.Message}");
            }
        });
    }

    public void Dispose()
    {
        Stop(screenOff: true);
        lock (_gate) { _sensors?.Dispose(); _sensors = null; }
    }
}
