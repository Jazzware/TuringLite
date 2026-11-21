using System.Drawing;
using System.Drawing.Drawing2D;

namespace TuringLite;

/// <summary>
/// Batería de los periféricos inalámbricos: un icono, el porcentaje y una barra fina por dispositivo.
/// En fila si el componente es más ancho que alto; en columna si no. Los colores salen del tema:
/// barra en color de aviso por debajo del 30 % y crítico por debajo del 15 %; un rayo indica que carga.
/// </summary>
sealed class BatteryWidget : Widget
{
    const int LowWarn = 30, LowCrit = 15;

    readonly WidgetConfig _c;
    IReadOnlyList<BatteryInfo> _items = Array.Empty<BatteryInfo>();
    bool _ready;

    public BatteryWidget(WidgetConfig c, DisplayTheme look) : base(look) => _c = c;

    public override string Signature(DateTime now, ISensorSource? sensors)
    {
        _items = BatteryFilter.Apply(BatteryCache.Latest, _c);
        _ready = BatteryCache.Ready;
        return _ready + "|" + string.Join("|", _items.Select(b => $"{b.Id}:{b.Percent}:{b.Charging}"));
    }

    public override void Draw(Graphics g, Size size)
    {
        var area = PaintBackdrop(g, size);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int pad = Look.Surface is null ? 4 : 10;
        var inner = Rectangle.Inflate(area, -pad, -pad / 2);
        if (inner.Width < 10 || inner.Height < 10) return;

        if (_items.Count == 0)
        {
            using var f = FontFactory.Make(Look.LabelFont, _c.FontSize > 0 ? _c.FontSize : 10f);
            using var b = new SolidBrush(Look.Label);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(_ready ? "Sin dispositivos" : "Leyendo baterías…", f, b, inner, sf);
            return;
        }

        bool row = _c.Arrange switch { "row" => true, "column" => false, _ => inner.Width >= inner.Height };
        int n = _items.Count;
        const int gap = 8;
        for (int i = 0; i < n; i++)
        {
            Rectangle cell = row
                ? new Rectangle(inner.X + i * (inner.Width + gap) / n, inner.Y, (inner.Width + gap) / n - gap, inner.Height)
                : new Rectangle(inner.X, inner.Y + i * (inner.Height + gap) / n, inner.Width, (inner.Height + gap) / n - gap);
            DrawItem(g, cell, _items[i]);
        }
    }

    void DrawItem(Graphics g, Rectangle cell, BatteryInfo b)
    {
        int icon = Math.Clamp(Math.Min(cell.Height - 6, cell.Width / 3), 10, 40);
        var iconBox = new Rectangle(cell.X, cell.Y + (cell.Height - icon) / 2, icon, icon);
        DrawIcon(g, iconBox, b.Kind, Look.Label);

        int textX = iconBox.Right + Math.Max(6, icon / 4);
        int textW = cell.Right - textX;
        if (textW < 12) return;

        Color level = b.Percent is not int p ? Look.Label : p <= LowCrit ? Look.Crit : p <= LowWarn ? Look.Warn : Look.Ok;
        string text = b.Percent is int pct ? pct + "%" : "--";

        // Con nombre: una línea pequeña arriba y el resto de la celda para el porcentaje y la barra.
        if (_c.ShowNames)
        {
            using var nf = FontFactory.Make(Look.LabelFont, Math.Clamp(cell.Height * 0.2f, 6f, 9f));
            using var nb = new SolidBrush(Look.Label);
            using var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            float nh = nf.GetHeight(g);
            g.DrawString(Look.UpperLabels ? b.Name.ToUpperInvariant() : b.Name, nf, nb, new RectangleF(textX - 1, cell.Y, textW + 1, nh), sf);
            cell = new Rectangle(cell.X, cell.Y + (int)nh, cell.Width, Math.Max(8, cell.Height - (int)nh));
        }

        // El texto ocupa la parte de arriba de la celda y la barra queda debajo.
        float px = _c.FontSize > 0 ? _c.FontSize * 96f / 72f : Math.Min(cell.Height * 0.5f, textW * 0.38f);
        using var font = FontFactory.Make(Look.ValueFont, Math.Max(6f, px * 72f / 96f), Look.ValueStyle);
        var textSize = g.MeasureString(text, font);
        int barH = Math.Max(2, Look.BarHeight - 1);
        float blockH = textSize.Height + 3 + barH;
        float top = cell.Y + (cell.Height - blockH) / 2f;

        using (var tb = new SolidBrush(b.Percent is null ? Look.Label : Look.Text))
            g.DrawString(text, font, tb, textX - 2, top);

        if (b.Charging)
        {
            float bolt = Math.Min(textSize.Height * 0.6f, 16f);
            DrawBolt(g, new RectangleF(textX + textSize.Width - 2, top + (textSize.Height - bolt) / 2f, bolt * 0.65f, bolt), Look.Accent);
        }

        var track = new Rectangle(textX, (int)(top + textSize.Height + 3), textW, barH);
        if (b.Percent is int v) PaintBar(g, track, v / 100.0, level);
        else Shapes.Fill(g, Look.Track, track, Look.Radius);
    }

    static void DrawIcon(Graphics g, Rectangle r, BatteryKind kind, Color color)
    {
        float w = Math.Max(1.5f, r.Width / 12f);
        using var pen = new Pen(color, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float s = r.Width, x = r.X, y = r.Y;
        switch (kind)
        {
            case BatteryKind.Mouse:
            {
                var body = new RectangleF(x + s * 0.24f, y + s * 0.06f, s * 0.52f, s * 0.88f);
                using var path = Rounded(body, s * 0.26f);
                g.DrawPath(pen, path);
                g.DrawLine(pen, x + s * 0.5f, y + s * 0.10f, x + s * 0.5f, y + s * 0.36f);
                break;
            }
            case BatteryKind.Keyboard:
            {
                var body = new RectangleF(x + s * 0.04f, y + s * 0.22f, s * 0.92f, s * 0.56f);
                using var path = Rounded(body, s * 0.1f);
                g.DrawPath(pen, path);
                using var dot = new SolidBrush(color);
                float d = Math.Max(1.5f, s * 0.07f);
                for (int row = 0; row < 2; row++)
                    for (int col = 0; col < 4; col++)
                        g.FillEllipse(dot, x + s * (0.2f + col * 0.2f) - d / 2, y + s * (0.38f + row * 0.15f) - d / 2, d, d);
                g.DrawLine(pen, x + s * 0.3f, y + s * 0.66f, x + s * 0.7f, y + s * 0.66f);
                break;
            }
            case BatteryKind.Headset:
            {
                g.DrawArc(pen, x + s * 0.14f, y + s * 0.08f, s * 0.72f, s * 0.8f, 180, 180);
                using var cup = new SolidBrush(color);
                using (var left = Rounded(new RectangleF(x + s * 0.08f, y + s * 0.48f, s * 0.2f, s * 0.36f), s * 0.07f))
                    g.FillPath(cup, left);
                using (var right = Rounded(new RectangleF(x + s * 0.72f, y + s * 0.48f, s * 0.2f, s * 0.36f), s * 0.07f))
                    g.FillPath(cup, right);
                break;
            }
            default:
            {
                var body = new RectangleF(x + s * 0.06f, y + s * 0.3f, s * 0.8f, s * 0.4f);
                using var path = Rounded(body, s * 0.06f);
                g.DrawPath(pen, path);
                g.DrawLine(pen, x + s * 0.92f, y + s * 0.42f, x + s * 0.92f, y + s * 0.58f);
                break;
            }
        }
    }

    static void DrawBolt(Graphics g, RectangleF r, Color color)
    {
        using var b = new SolidBrush(color);
        g.FillPolygon(b, new[]
        {
            new PointF(r.X + r.Width * 0.6f, r.Y),
            new PointF(r.X, r.Y + r.Height * 0.58f),
            new PointF(r.X + r.Width * 0.45f, r.Y + r.Height * 0.58f),
            new PointF(r.X + r.Width * 0.35f, r.Bottom),
            new PointF(r.Right, r.Y + r.Height * 0.4f),
            new PointF(r.X + r.Width * 0.55f, r.Y + r.Height * 0.4f),
        });
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = Math.Max(1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
