using System.Drawing;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TuringLite;

public sealed class WidgetConfig
{
    public string Type { get; set; } = "sensor";   // sensor | clock | text
    public bool Enabled { get; set; } = true;       // false = configurado pero oculto
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; } = 300;
    public int H { get; set; } = 60;
    public string Label { get; set; } = "";
    // Identificador de LHM ("/amdcpu/0/temperature/2") o alias antiguo (cpu.temp, gpu.load, ram.load...)
    public string Sensor { get; set; } = "";
    public string Unit { get; set; } = "";
    public int Decimals { get; set; } = 0;
    public double? Warn { get; set; }
    public double? Crit { get; set; }
    public bool Bar { get; set; }
    public double Max { get; set; } = 100;
    public string Format { get; set; } = "HH:mm";  // clock
    public string Text { get; set; } = "";          // text estático
    public string File { get; set; } = "";          // text desde archivo (se relee solo si cambia)
    public float FontSize { get; set; } = 0;        // 0 = automático

    public WidgetConfig Clone() => (WidgetConfig)MemberwiseClone();

    /// <summary>Deja el componente dentro de la pantalla y con valores que el dibujo puede usar.</summary>
    public void Normalize(Size screen)
    {
        var type = (Type ?? "").ToLowerInvariant();
        Type = type is "clock" or "text" ? type : "sensor";
        W = Math.Clamp(W, 10, screen.Width);
        H = Math.Clamp(H, 10, screen.Height);
        X = Math.Clamp(X, 0, screen.Width - W);
        Y = Math.Clamp(Y, 0, screen.Height - H);
        FontSize = Math.Clamp(FontSize, 0f, 200f);
        Decimals = Math.Clamp(Decimals, 0, 3);
        if (!(Max > 0)) Max = 100;           // también cubre NaN
        Label ??= "";
        Sensor ??= "";
        Unit ??= "";
        Format ??= "HH:mm";
        Text ??= "";
        File ??= "";
    }
}

public sealed class AppConfig
{
    /// <summary>Un config.json real pesa unos pocos KB; más de esto no es configuración.</summary>
    const long MaxFileBytes = 1024 * 1024;

    public string Port { get; set; } = "AUTO";
    public int Brightness { get; set; } = 40;
    public string Orientation { get; set; } = "portrait";
    public int IntervalMs { get; set; } = 2000;
    public int RefreshSeconds { get; set; } = 60;     // reenvía cada widget cada N s para corregir glitches (0 = nunca)
    public string Gpu { get; set; } = "";             // parte del nombre de la GPU a usar (vacío = dedicada automática)
    public int MaxRegionWidth { get; set; } = 200;    // envía regiones más anchas en franjas (0 = sin límite)
    public string Background { get; set; } = "#12161C"; // solo lo usa el tema "classic"
    public string Theme { get; set; } = "classic";      // clave de Themes.All
    public List<WidgetConfig> Widgets { get; set; } = new();

    static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
    };

    static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // conserva "°C" legible
    };

    /// <summary>Lee y normaliza la configuración. Un valor fuera de rango se corrige, no se propaga al monitor.</summary>
    public static AppConfig Load(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes)
            throw new InvalidDataException("config.json es demasiado grande para ser una configuración.");
        var cfg = JsonSerializer.Deserialize<AppConfig>(System.IO.File.ReadAllText(path), ReadOpts)
                  ?? throw new InvalidDataException("config.json vacío o inválido.");
        cfg.Normalize();
        return cfg;
    }

    /// <summary>
    /// Rangos seguros para el monitor. Sin esto, un intervalo de 0 ms deja un bucle girando sin pausa, y un
    /// componente que sobresale de la pantalla (p. ej. tras cambiar la orientación) provoca un error en cada ciclo.
    /// </summary>
    public void Normalize()
    {
        Brightness = Math.Clamp(Brightness, 0, 100);
        IntervalMs = Math.Clamp(IntervalMs, 500, 10_000);
        RefreshSeconds = Math.Clamp(RefreshSeconds, 0, 3_600);
        MaxRegionWidth = Math.Clamp(MaxRegionWidth, 0, 480);
        Port ??= "AUTO";
        Gpu ??= "";
        Background ??= "#12161C";
        Orientation ??= "portrait";
        Theme = string.IsNullOrWhiteSpace(Theme) ? "classic" : Theme.Trim().ToLowerInvariant();
        Widgets = Widgets?.Where(w => w != null).ToList() ?? new List<WidgetConfig>();
        var screen = DisplayLayout.LogicalSize(Orientation);
        foreach (var w in Widgets) w.Normalize(screen);
    }

    /// <summary>Guarda la configuración (los comentarios del archivo original no se conservan).</summary>
    public void Save(string path)
    {
        var tmp = path + ".tmp";
        System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(this, WriteOpts));
        System.IO.File.Move(tmp, path, overwrite: true);
    }

    public Orientation ParsedOrientation => Orientation.ToLowerInvariant() switch
    {
        "landscape" => TuringLite.Orientation.Landscape,
        "reverse-portrait" => TuringLite.Orientation.ReversePortrait,
        "reverse-landscape" => TuringLite.Orientation.ReverseLandscape,
        _ => TuringLite.Orientation.Portrait,
    };
}
