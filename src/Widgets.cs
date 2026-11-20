using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace TuringLite;

/// <summary>Un widget solo se redibuja y se envía cuando su firma cambia. Su aspecto lo da el tema.</summary>
abstract class Widget : IDisposable
{
    public Rectangle Bounds;
    public string? LastSignature;
    public DateTime LastSent = DateTime.MinValue;
    protected readonly DisplayTheme Look;

    protected Widget(DisplayTheme look) => Look = look;

    public abstract string Signature(DateTime now, ISensorSource? sensors);
    public abstract void Draw(Graphics g, Size size);
    /// <summary>Libera las fuentes GDI del widget.</summary>
    public virtual void Dispose() { }

    public static Widget Create(WidgetConfig c, DisplayTheme look)
    {
        Widget w = c.Type.ToLowerInvariant() switch
        {
            "clock" => new ClockWidget(c, look),
            "text" => new TextWidget(c, look),
            _ => new SensorWidget(c, look),
        };
        w.Bounds = new Rectangle(c.X, c.Y, c.W, c.H);
        return w;
    }

    /// <summary>
    /// Pinta el fondo y, si el tema usa tarjetas, la tarjeta. Devuelve el área donde va el contenido:
    /// todo el widget sin tarjeta, o el interior de la tarjeta con ella.
    /// </summary>
    protected Rectangle PaintBackdrop(Graphics g, Size size)
    {
        g.Clear(Look.Background);
        if (Look.Surface is not Color surface) return new Rectangle(0, 0, size.Width, size.Height);

        var card = new Rectangle(2, 2, size.Width - 4, size.Height - 4);
        int r = Math.Min(Look.Radius, Math.Min(card.Width, card.Height) / 2);
        Shapes.Fill(g, surface, card, r);
        if (Look.Border is Color border) Shapes.Outline(g, border, card, r);
        if (Look.AccentTop && card.Width > 2 * r)
        {
            using var accent = new SolidBrush(Look.Accent);
            g.FillRectangle(accent, card.X + r, card.Y, card.Width - 2 * r, 2);
        }
        return card;
    }

    /// <summary>Barra de progreso: continua o por bloques según el tema.</summary>
    protected void PaintBar(Graphics g, Rectangle track, double fraction, Color fill)
    {
        if (Look.Segments > 1)
        {
            const int gap = 2;
            int n = Look.Segments;
            int segW = (track.Width - gap * (n - 1)) / n;
            if (segW < 1) return;
            int lit = (int)Math.Round(Math.Clamp(fraction, 0, 1) * n);
            for (int i = 0; i < n; i++)
            {
                using var b = new SolidBrush(i < lit ? fill : Look.Track);
                g.FillRectangle(b, track.X + i * (segW + gap), track.Y, segW, track.Height);
            }
            return;
        }
        Shapes.Fill(g, Look.Track, track, Look.Radius);
        int w = (int)(track.Width * Math.Clamp(fraction, 0, 1));
        if (w > 0) Shapes.Fill(g, fill, new Rectangle(track.X, track.Y, w, track.Height), Look.Radius);
    }
}

/// <summary>Formato de hora del reloj. Un formato inválido no debe tumbar el dibujo ni la aplicación.</summary>
static class ClockFormat
{
    const int MaxLength = 40;

    public static bool IsValid(string format)
    {
        if (format.Length == 0 || format.Length > MaxLength) return false;
        try { _ = DateTime.Now.ToString(format, CultureInfo.InvariantCulture); return true; }
        catch (FormatException) { return false; }
    }

    public static string Render(DateTime now, string format)
    {
        try { return now.ToString(format, CultureInfo.InvariantCulture); }
        catch (FormatException) { return "--:--"; }
    }
}

sealed class SensorWidget : Widget
{
    readonly WidgetConfig _c;
    readonly Font _label;
    readonly Font _value;
    float? _v;

    public SensorWidget(WidgetConfig c, DisplayTheme look) : base(look)
    {
        _c = c;
        _label = FontFactory.Make(look.LabelFont, 10f);
        _value = FontFactory.Make(look.ValueFont, c.FontSize > 0 ? c.FontSize : 20f, look.ValueStyle);
    }

    string Fmt(float v) => TotalPower.Prefix(_c) + v.ToString("F" + _c.Decimals, CultureInfo.InvariantCulture) + _c.Unit;

    public override void Dispose()
    {
        _label.Dispose();
        _value.Dispose();
    }

    public override string Signature(DateTime now, ISensorSource? s)
    {
        _v = TotalPower.ValueFor(_c, s);
        return _v is float v ? Fmt(v) : "--";
    }

    public override void Draw(Graphics g, Size size)
    {
        PaintBackdrop(g, size);
        // Con tarjeta hay más margen; sin ella, las coordenadas son las originales del diseño clásico.
        bool card = Look.Surface is not null;
        int padX = card ? 12 : 6, labelY = card ? 8 : 2, valueY = card ? 20 : 14;
        int barGap = card ? 8 : 4, barInset = card ? 12 : 6;

        string label = Look.LabelPrefix + (Look.UpperLabels ? _c.Label.ToUpperInvariant() : _c.Label);
        using (var b = new SolidBrush(Look.Label)) g.DrawString(label, _label, b, padX, labelY);

        string text = _v is float v ? Fmt(v) : "--";
        Color col = _v is not float vv ? Look.Label : vv >= _c.Crit ? Look.Crit : vv >= _c.Warn ? Look.Warn : Look.Ok;
        using (var b = new SolidBrush(col)) g.DrawString(text, _value, b, padX - 2, valueY);

        if (_c.Bar && _v is float bv)
        {
            var track = new Rectangle(barInset, size.Height - barGap - Look.BarHeight, size.Width - 2 * barInset, Look.BarHeight);
            PaintBar(g, track, bv / _c.Max, col);
        }
    }
}

sealed class ClockWidget : Widget
{
    readonly WidgetConfig _c;
    readonly Font _font;
    string _text = "";

    public ClockWidget(WidgetConfig c, DisplayTheme look) : base(look)
    {
        _c = c;
        _font = FontFactory.Make(look.ClockFont, c.FontSize > 0 ? c.FontSize : 36f);
    }

    public override void Dispose() => _font.Dispose();

    public override string Signature(DateTime now, ISensorSource? s) => _text = ClockFormat.Render(now, _c.Format);

    public override void Draw(Graphics g, Size size)
    {
        var area = PaintBackdrop(g, size);
        using var b = new SolidBrush(Look.Text);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(_text, _font, b, new RectangleF(area.X, area.Y, area.Width, area.Height), sf);
    }
}

/// <summary>Texto propio: fijo o leído de un archivo (ej. estado de tus proyectos).</summary>
sealed class TextWidget : Widget
{
    // Un archivo de texto para una pantalla de 480x320 cabe de sobra en 16 KB; más sería solo memoria y tiempo de dibujo.
    const int MaxFileBytes = 16 * 1024;
    const int MaxChars = 2000;

    readonly WidgetConfig _c;
    readonly Font _font;
    DateTime _lastCheck = DateTime.MinValue, _lastWrite = DateTime.MinValue;
    string _text;

    public TextWidget(WidgetConfig c, DisplayTheme look) : base(look)
    {
        _c = c;
        _text = c.Text;
        _font = FontFactory.Make(look.LabelFont, c.FontSize > 0 ? c.FontSize : 11f);
    }

    public override void Dispose() => _font.Dispose();

    public override string Signature(DateTime now, ISensorSource? s)
    {
        // Comprueba el archivo como máximo cada 5 s y solo lo relee si cambió.
        if (_c.File.Length > 0 && (now - _lastCheck).TotalSeconds >= 5)
        {
            _lastCheck = now;
            try
            {
                var path = Path.IsPathRooted(_c.File) ? _c.File : Path.Combine(AppContext.BaseDirectory, _c.File);
                if (File.Exists(path))
                {
                    var w = File.GetLastWriteTimeUtc(path);
                    if (w != _lastWrite) { _lastWrite = w; _text = ReadCapped(path); }
                }
            }
            catch { /* conserva el último texto válido */ }
        }
        return _text;
    }

    /// <summary>Lee como mucho <see cref="MaxFileBytes"/>, sin bloquear a quien esté escribiendo el archivo.</summary>
    static string ReadCapped(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buf = new byte[MaxFileBytes];
        int n = 0, r;
        while (n < buf.Length && (r = fs.Read(buf, n, buf.Length - n)) > 0) n += r;
        var text = System.Text.Encoding.UTF8.GetString(buf, 0, n);
        if (text.Length > 0 && text[0] == '﻿') text = text[1..]; // marca BOM
        return text.Length > MaxChars ? text[..MaxChars] : text;
    }

    public override void Draw(Graphics g, Size size)
    {
        var area = PaintBackdrop(g, size);
        float pad = Look.Surface is null ? 6 : 10;
        using var b = new SolidBrush(Look.Text);
        g.DrawString(_text, _font, b, new RectangleF(area.X + pad, area.Y + 2, area.Width - 2 * pad, area.Height - 4));
    }
}

/// <summary>Fuentes GDI. Si el sistema no tiene la familia, GDI+ usa una por defecto (no lanza excepción).</summary>
static class FontFactory
{
    public static Font Make(string family, float size, FontStyle style = FontStyle.Regular) =>
        new(family, Math.Clamp(size, 4f, 200f), style, GraphicsUnit.Point);
}

static class Shapes
{
    public static void Fill(Graphics g, Color color, Rectangle r, int radius)
    {
        using var b = new SolidBrush(color);
        if (radius <= 0) { g.FillRectangle(b, r); return; }
        using var path = RoundRect(r, radius);
        g.FillPath(b, path);
    }

    public static void Outline(Graphics g, Color color, Rectangle r, int radius)
    {
        using var p = new Pen(color);
        var rr = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
        if (radius <= 0) { g.DrawRectangle(p, rr); return; }
        using var path = RoundRect(rr, radius);
        g.DrawPath(p, path);
    }

    static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

static class Raster
{
    /// <summary>Renderiza un widget y lo convierte a RGB565 little-endian.</summary>
    public static int Render(Widget w, ref byte[] buffer)
    {
        var size = w.Bounds.Size;
        using var bmp = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            w.Draw(g, size);
        }
        return ToRgb565(bmp, ref buffer);
    }

    public static int ToRgb565(Bitmap bmp, ref byte[] buffer)
    {
        int width = bmp.Width, height = bmp.Height, need = width * height * 2;
        if (buffer.Length < need) buffer = new byte[need];
        var data = bmp.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        try
        {
            unsafe
            {
                int o = 0;
                for (int y = 0; y < height; y++)
                {
                    byte* row = (byte*)data.Scan0 + y * data.Stride;
                    for (int x = 0; x < width; x++)
                    {
                        int b = row[x * 3], gg = row[x * 3 + 1], r = row[x * 3 + 2];
                        ushort p = (ushort)(((r & 0xF8) << 8) | ((gg & 0xFC) << 3) | (b >> 3));
                        buffer[o++] = (byte)(p & 0xFF);
                        buffer[o++] = (byte)(p >> 8);
                    }
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return need;
    }
}
