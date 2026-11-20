using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

namespace TuringLite;

/// <summary>Geometría del diseño: tamaño lógico según orientación, plantillas y composición de la vista previa.</summary>
static class DisplayLayout
{
    /// <summary>Las coordenadas de los widgets usan el tamaño lógico (480×320 horizontal, 320×480 vertical).</summary>
    public static Size LogicalSize(string orientation) =>
        orientation.ToLowerInvariant() is "landscape" or "reverse-landscape" ? new Size(480, 320) : new Size(320, 480);

    /// <summary>Plantillas predefinidas. Usan alias (cpu.temp...) que existen en cualquier PC.</summary>
    public static readonly (string Name, Func<Size, List<WidgetConfig>> Build)[] Templates =
    {
        ("Panel completo (6 sensores)", FullPanel),
        ("Minimalista (reloj + temperaturas)", Minimal),
    };

    /// <summary>Diseño por defecto: el mismo que trae un config.json recién instalado.</summary>
    public static List<WidgetConfig> Default(Size s) => FullPanel(s);

    static List<WidgetConfig> FullPanel(Size s)
    {
        var list = Header(s, withText: true);
        Grid(list, new[]
        {
            Sensor("CPU TEMP", "cpu.temp", "°C", 70, 85, bar: true),
            Sensor("CPU LOAD", "cpu.load", "%", 80, 95, bar: true),
            Sensor("CPU POWER", "cpu.power", " W", null, null, bar: true, max: 200),
            Sensor("GPU TEMP", "gpu.temp", "°C", 75, 87, bar: true),
            Sensor("GPU LOAD", "gpu.load", "%", 85, 98, bar: true),
            Sensor("RAM", "ram.load", "%", 80, 92, bar: true),
        }, s, top: IsLandscape(s) ? 80 : 120, cols: IsLandscape(s) ? 3 : 2);
        return list;
    }

    static List<WidgetConfig> Minimal(Size s)
    {
        var list = Header(s, withText: false);
        Grid(list, new[]
        {
            Sensor("CPU", "cpu.temp", "°C", 70, 85, bar: false, font: 40),
            Sensor("GPU", "gpu.temp", "°C", 75, 87, bar: false, font: 40),
        }, s, top: IsLandscape(s) ? 80 : 120, cols: 2);
        return list;
    }

    /// <summary>Texto del valor actual de un componente, tal como aparece en las listas.</summary>
    public static string ValueText(WidgetConfig c, ISensorSource? s)
    {
        try
        {
            switch (c.Type)
            {
                case "clock":
                    return DateTime.Now.ToString(c.Format, CultureInfo.InvariantCulture);
                case "text":
                    return "";
                default:
                    var v = TotalPower.ValueFor(c, s);
                    return v is float f ? TotalPower.Prefix(c) + f.ToString("F" + c.Decimals, CultureInfo.InvariantCulture) + c.Unit : "--";
            }
        }
        catch (FormatException)
        {
            return "formato inválido";
        }
    }

    /// <summary>Mantiene el componente dentro de la pantalla y con un tamaño mínimo (el driver rechaza regiones fuera).</summary>
    public static void Clamp(WidgetConfig c, Size s)
    {
        c.W = Math.Clamp(c.W, 10, s.Width);
        c.H = Math.Clamp(c.H, 10, s.Height);
        c.X = Math.Clamp(c.X, 0, s.Width - c.W);
        c.Y = Math.Clamp(c.Y, 0, s.Height - c.H);
    }

    static bool IsLandscape(Size s) => s.Width > s.Height;

    static List<WidgetConfig> Header(Size s, bool withText)
    {
        var list = new List<WidgetConfig>
        {
            new() { Type = "clock", Format = "HH:mm", X = 10, Y = 6, W = IsLandscape(s) ? 200 : s.Width - 20, H = 64 },
        };
        if (withText)
        {
            list.Add(IsLandscape(s)
                ? new WidgetConfig { Type = "text", File = "status.txt", Text = "Escribe en status.txt", X = 220, Y = 10, W = s.Width - 230, H = 60, FontSize = 10 }
                : new WidgetConfig { Type = "text", File = "status.txt", Text = "Escribe en status.txt", X = 10, Y = 74, W = s.Width - 20, H = 40, FontSize = 10 });
        }
        return list;
    }

    /// <summary>Reparte las celdas en una cuadrícula bajo el encabezado.</summary>
    static void Grid(List<WidgetConfig> list, IReadOnlyList<WidgetConfig> cells, Size s, int top, int cols)
    {
        const int margin = 10, gap = 5;
        int rows = (cells.Count + cols - 1) / cols;
        int cw = (s.Width - 2 * margin - (cols - 1) * gap) / cols;
        int ch = (s.Height - top - margin - (rows - 1) * gap) / rows;
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            c.X = margin + (i % cols) * (cw + gap);
            c.Y = top + (i / cols) * (ch + gap);
            c.W = cw;
            c.H = ch;
            list.Add(c);
        }
    }

    static WidgetConfig Sensor(string label, string key, string unit, double? warn, double? crit, bool bar,
        double max = 100, float font = 26) =>
        new()
        {
            Type = "sensor", Label = label, Sensor = key, Unit = unit, Warn = warn, Crit = crit,
            Bar = bar, Max = max, FontSize = font, X = 0, Y = 0, W = 150, H = 100,
        };

    /// <summary>
    /// Dibuja los widgets sobre el fondo del tema, igual que la pantalla real (mismo código de dibujo).
    /// Los widgets se dibujan con sus coordenadas actuales, así que la vista previa coincide con el resultado.
    /// </summary>
    public static Bitmap Compose(IEnumerable<Widget> widgets, ISensorSource? sensors, Size size, DisplayTheme look)
    {
        var canvas = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
        using var cg = Graphics.FromImage(canvas);
        cg.Clear(look.Background);
        var now = DateTime.Now;
        foreach (var w in widgets)
        {
            w.Signature(now, sensors);
            using var part = new Bitmap(w.Bounds.Width, w.Bounds.Height, PixelFormat.Format24bppRgb);
            using (var pg = Graphics.FromImage(part))
            {
                pg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                w.Draw(pg, w.Bounds.Size);
            }
            cg.DrawImage(part, w.Bounds.Location);
        }
        return canvas;
    }
}
