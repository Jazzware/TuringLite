using System.Drawing;

namespace TuringLite;

/// <summary>
/// Aspecto con que se dibujan los componentes: colores, formas y tipografías. Dónde va cada componente
/// lo decide la plantilla (Layout.cs); cómo se ve, el tema. Son dos ejes independientes.
/// </summary>
public sealed record DisplayTheme
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }

    public required Color Background { get; init; }
    /// <summary>Fondo de cada tarjeta. null = sin tarjeta: el componente va directo sobre el fondo.</summary>
    public Color? Surface { get; init; }
    public Color? Border { get; init; }
    public required Color Label { get; init; }
    public required Color Text { get; init; }
    public required Color Accent { get; init; }
    public required Color Ok { get; init; }
    public required Color Warn { get; init; }
    public required Color Crit { get; init; }
    public required Color Track { get; init; }

    /// <summary>Radio de esquina de tarjetas y barras, en píxeles. 0 = rectas.</summary>
    public int Radius { get; init; }
    public int BarHeight { get; init; } = 5;
    /// <summary>0 = barra continua; N &gt; 1 = barra de N bloques (estilo HUD).</summary>
    public int Segments { get; init; }
    /// <summary>Franja de acento de 2 px arriba de cada tarjeta.</summary>
    public bool AccentTop { get; init; }
    public bool UpperLabels { get; init; }
    /// <summary>Prefijo de la etiqueta, p. ej. "$ " para un estilo de terminal.</summary>
    public string LabelPrefix { get; init; } = "";

    public string LabelFont { get; init; } = "Segoe UI";
    public string ValueFont { get; init; } = "Segoe UI Semibold";
    public string ClockFont { get; init; } = "Segoe UI Light";
    public FontStyle ValueStyle { get; init; } = FontStyle.Regular;
}

/// <summary>
/// Catálogo de temas. "classic" es el diseño original: su fondo sale de config.json ("background") para
/// que las configuraciones existentes no cambien. Los demás definen su propio fondo.
/// </summary>
public static class Themes
{
    static readonly DisplayTheme Classic = new()
    {
        Key = "classic", Name = "Clásico", Category = "Base",
        Description = "El diseño original. El fondo es el color «background» de config.json.",
        Background = Color.FromArgb(0x12, 0x16, 0x1C),
        Label = Color.FromArgb(0x8A, 0x94, 0xA3), Text = Color.FromArgb(0xE6, 0xEA, 0xF0),
        Accent = Color.FromArgb(0x7D, 0xD3, 0xA8), Ok = Color.FromArgb(0x7D, 0xD3, 0xA8),
        Warn = Color.FromArgb(0xF5, 0xC2, 0x6B), Crit = Color.FromArgb(0xF0, 0x70, 0x7A),
        Track = Color.FromArgb(0x23, 0x2A, 0x33),
        BarHeight = 5,
    };

    /// <summary>Todos los temas, en el orden en que se ofrecen. El primero es el predeterminado.</summary>
    public static readonly IReadOnlyList<DisplayTheme> All = new DisplayTheme[]
    {
        Classic,

        new()
        {
            Key = "neon", Name = "Gamer · Neón", Category = "Gamer",
            Description = "Fondo casi negro, tarjetas azul noche y barras cian redondeadas.",
            Background = Color.FromArgb(0x0A, 0x0C, 0x12), Surface = Color.FromArgb(0x12, 0x18, 0x26),
            Border = Color.FromArgb(0x1F, 0x2A, 0x44),
            Label = Color.FromArgb(0x7A, 0x89, 0xA8), Text = Color.FromArgb(0xF2, 0xF7, 0xFF),
            Accent = Color.FromArgb(0x00, 0xF5, 0xD4), Ok = Color.FromArgb(0x00, 0xF5, 0xD4),
            Warn = Color.FromArgb(0xFE, 0xE4, 0x40), Crit = Color.FromArgb(0xFF, 0x2E, 0x63),
            Track = Color.FromArgb(0x1C, 0x24, 0x38),
            Radius = 6, BarHeight = 6, AccentTop = true, UpperLabels = true,
            LabelFont = "Bahnschrift", ValueFont = "Bahnschrift", ClockFont = "Bahnschrift",
            ValueStyle = FontStyle.Bold,
        },

        new()
        {
            Key = "cyber", Name = "Gamer · Cyberpunk", Category = "Gamer",
            Description = "Magenta y cian sobre violeta oscuro, con barras en bloques de HUD.",
            Background = Color.FromArgb(0x0D, 0x02, 0x21), Surface = Color.FromArgb(0x18, 0x06, 0x36),
            Border = Color.FromArgb(0xFF, 0x2A, 0x6D),
            Label = Color.FromArgb(0x05, 0xD9, 0xE8), Text = Color.FromArgb(0xF8, 0xF8, 0xFF),
            Accent = Color.FromArgb(0xFF, 0x2A, 0x6D), Ok = Color.FromArgb(0x05, 0xD9, 0xE8),
            Warn = Color.FromArgb(0xF9, 0xF8, 0x71), Crit = Color.FromArgb(0xFF, 0x2A, 0x6D),
            Track = Color.FromArgb(0x2A, 0x0B, 0x4A),
            Radius = 0, BarHeight = 8, Segments = 10, AccentTop = true, UpperLabels = true,
            LabelFont = "Bahnschrift", ValueFont = "Bahnschrift", ClockFont = "Bahnschrift",
            ValueStyle = FontStyle.Bold,
        },

        new()
        {
            Key = "fire", Name = "Gamer · Fuego", Category = "Gamer",
            Description = "Negro carbón con tonos cálidos: amarillo para lo normal, rojo para lo crítico.",
            Background = Color.FromArgb(0x12, 0x08, 0x06), Surface = Color.FromArgb(0x1E, 0x0E, 0x0A),
            Border = Color.FromArgb(0x3A, 0x1A, 0x10),
            Label = Color.FromArgb(0xB5, 0x8A, 0x7A), Text = Color.FromArgb(0xFF, 0xF4, 0xE8),
            Accent = Color.FromArgb(0xFF, 0x9F, 0x1C), Ok = Color.FromArgb(0xFF, 0xD1, 0x66),
            Warn = Color.FromArgb(0xFF, 0x9F, 0x1C), Crit = Color.FromArgb(0xFF, 0x3B, 0x30),
            Track = Color.FromArgb(0x2E, 0x14, 0x0D),
            Radius = 4, BarHeight = 7, UpperLabels = true,
            LabelFont = "Bahnschrift", ValueFont = "Bahnschrift", ClockFont = "Bahnschrift",
            ValueStyle = FontStyle.Bold,
        },

        new()
        {
            Key = "terminal", Name = "Programador · Terminal", Category = "Programador",
            Description = "Verde sobre negro, como una consola clásica. Etiquetas con prompt y barra segmentada.",
            Background = Color.FromArgb(0x0C, 0x0C, 0x0C), Surface = null,
            Label = Color.FromArgb(0x2F, 0xA8, 0x66), Text = Color.FromArgb(0x7C, 0xFF, 0x9B),
            Accent = Color.FromArgb(0x33, 0xFF, 0x66), Ok = Color.FromArgb(0x33, 0xFF, 0x66),
            Warn = Color.FromArgb(0xFF, 0xD8, 0x66), Crit = Color.FromArgb(0xFF, 0x55, 0x55),
            Track = Color.FromArgb(0x0F, 0x2A, 0x17),
            Radius = 0, BarHeight = 6, Segments = 20, LabelPrefix = "$ ",
            LabelFont = "Consolas", ValueFont = "Consolas", ClockFont = "Consolas",
            ValueStyle = FontStyle.Bold,
        },

        new()
        {
            Key = "nocturne", Name = "Programador · Nocturno", Category = "Programador",
            Description = "Azul grisáceo y lila con tarjetas suaves y tipografía monoespaciada.",
            Background = Color.FromArgb(0x1A, 0x1B, 0x26), Surface = Color.FromArgb(0x22, 0x25, 0x36),
            Border = Color.FromArgb(0x2F, 0x33, 0x50),
            Label = Color.FromArgb(0x8B, 0x93, 0xB8), Text = Color.FromArgb(0xC8, 0xD0, 0xF5),
            Accent = Color.FromArgb(0xBB, 0x9A, 0xF7), Ok = Color.FromArgb(0x9E, 0xCE, 0x6A),
            Warn = Color.FromArgb(0xE0, 0xAF, 0x68), Crit = Color.FromArgb(0xF7, 0x76, 0x8E),
            Track = Color.FromArgb(0x2A, 0x2E, 0x45),
            Radius = 8, BarHeight = 4,
            LabelFont = "Consolas", ValueFont = "Consolas", ClockFont = "Consolas",
            ValueStyle = FontStyle.Bold,
        },

        new()
        {
            Key = "minimal-light", Name = "Minimalista · Blanco", Category = "Minimalista",
            Description = "Fondo claro sin tarjetas ni bordes. Solo tipografía y una barra fina.",
            Background = Color.FromArgb(0xF4, 0xF4, 0xF2), Surface = null,
            Label = Color.FromArgb(0x5E, 0x5E, 0x58), Text = Color.FromArgb(0x1C, 0x1C, 0x1A),
            Accent = Color.FromArgb(0x1C, 0x1C, 0x1A), Ok = Color.FromArgb(0x2F, 0x7D, 0x5B),
            Warn = Color.FromArgb(0x9A, 0x60, 0x0C), Crit = Color.FromArgb(0xB4, 0x23, 0x18),
            Track = Color.FromArgb(0xDC, 0xDC, 0xD6),
            Radius = 0, BarHeight = 2,
            LabelFont = "Segoe UI", ValueFont = "Segoe UI", ClockFont = "Segoe UI Light",
        },

        new()
        {
            Key = "minimal-graphite", Name = "Minimalista · Grafito", Category = "Minimalista",
            Description = "Negro grafito, texto blanco y barras de 2 px. Lo mínimo que se puede leer.",
            Background = Color.FromArgb(0x0E, 0x0E, 0x10), Surface = null,
            Label = Color.FromArgb(0x8E, 0x8E, 0x96), Text = Color.FromArgb(0xF2, 0xF2, 0xF4),
            Accent = Color.FromArgb(0xFF, 0xFF, 0xFF), Ok = Color.FromArgb(0xD4, 0xD4, 0xDA),
            Warn = Color.FromArgb(0xF5, 0xC4, 0x51), Crit = Color.FromArgb(0xFF, 0x6B, 0x6B),
            Track = Color.FromArgb(0x2A, 0x2A, 0x30),
            Radius = 0, BarHeight = 2,
            LabelFont = "Segoe UI", ValueFont = "Segoe UI Light", ClockFont = "Segoe UI Light",
        },

        new()
        {
            Key = "retro-amber", Name = "Retro · Ámbar", Category = "Retro",
            Description = "Monitor monocromo de fósforo ámbar. Los avisos suben de brillo o pasan a rojizo.",
            Background = Color.FromArgb(0x14, 0x0C, 0x00), Surface = null,
            Label = Color.FromArgb(0xB0, 0x7A, 0x10), Text = Color.FromArgb(0xFF, 0xB0, 0x00),
            Accent = Color.FromArgb(0xFF, 0xB0, 0x00), Ok = Color.FromArgb(0xFF, 0xB0, 0x00),
            Warn = Color.FromArgb(0xFF, 0xD2, 0x7A), Crit = Color.FromArgb(0xFF, 0x5A, 0x1F),
            Track = Color.FromArgb(0x2E, 0x1E, 0x02),
            Radius = 0, BarHeight = 6, UpperLabels = true,
            LabelFont = "Consolas", ValueFont = "Consolas", ClockFont = "Consolas",
            ValueStyle = FontStyle.Bold,
        },

        new()
        {
            Key = "soft-sky", Name = "Suave · Cielo", Category = "Suave",
            Description = "Tarjetas blancas sobre azul muy claro, esquinas redondeadas y tonos pastel.",
            Background = Color.FromArgb(0xE9, 0xF1, 0xF8), Surface = Color.FromArgb(0xFF, 0xFF, 0xFF),
            Border = Color.FromArgb(0xD3, 0xE0, 0xEE),
            Label = Color.FromArgb(0x4A, 0x61, 0x78), Text = Color.FromArgb(0x14, 0x28, 0x3B),
            Accent = Color.FromArgb(0x2F, 0x6F, 0xB2), Ok = Color.FromArgb(0x1F, 0x7A, 0x4D),
            Warn = Color.FromArgb(0x8C, 0x5A, 0x12), Crit = Color.FromArgb(0xC0, 0x39, 0x2B),
            Track = Color.FromArgb(0xE3, 0xEC, 0xF5),
            Radius = 10, BarHeight = 6,
            LabelFont = "Segoe UI", ValueFont = "Segoe UI Semibold", ClockFont = "Segoe UI Light",
        },

        new()
        {
            Key = "high-contrast", Name = "Alto contraste", Category = "Accesible",
            Description = "Negro, blanco y amarillo. Pensado para leer de un vistazo y para baja visión.",
            Background = Color.FromArgb(0x00, 0x00, 0x00), Surface = Color.FromArgb(0x00, 0x00, 0x00),
            Border = Color.FromArgb(0xFF, 0xFF, 0xFF),
            Label = Color.FromArgb(0xFF, 0xFF, 0xFF), Text = Color.FromArgb(0xFF, 0xFF, 0xFF),
            Accent = Color.FromArgb(0xFF, 0xFF, 0x00), Ok = Color.FromArgb(0x00, 0xFF, 0x66),
            Warn = Color.FromArgb(0xFF, 0xFF, 0x00), Crit = Color.FromArgb(0xFF, 0x4D, 0x4D),
            Track = Color.FromArgb(0x4D, 0x4D, 0x4D),
            Radius = 0, BarHeight = 10, UpperLabels = true,
            LabelFont = "Segoe UI Semibold", ValueFont = "Segoe UI Black", ClockFont = "Segoe UI Semibold",
        },
    };

    /// <summary>Tema de una clave. Clave desconocida o vacía = clásico, sin lanzar excepción.</summary>
    public static DisplayTheme Find(string? key, string classicBackground)
    {
        var k = (key ?? "").Trim().ToLowerInvariant();
        var found = All.FirstOrDefault(t => t.Key == k);
        if (found != null && k != "classic") return found;
        return Classic with { Background = Parse(classicBackground, Classic.Background) };
    }

    /// <summary>Tema efectivo de una configuración.</summary>
    public static DisplayTheme Resolve(AppConfig cfg) => Find(cfg.Theme, cfg.Background);

    /// <summary>Posición de una clave en <see cref="All"/>, o 0 si no existe.</summary>
    public static int IndexOf(string? key)
    {
        var k = (key ?? "").Trim().ToLowerInvariant();
        for (int i = 0; i < All.Count; i++)
            if (All[i].Key == k) return i;
        return 0;
    }

    /// <summary>Color hex (#RRGGBB). Un valor inválido da el de respaldo: nunca tumba la aplicación.</summary>
    public static Color Parse(string? hex, Color fallback)
    {
        try
        {
            var c = ColorTranslator.FromHtml(hex ?? "");
            return c.IsEmpty ? fallback : c;
        }
        catch (FormatException) { return fallback; }
        catch (ArgumentException) { return fallback; }
    }
}
