using System.Drawing;
using System.Drawing.Drawing2D;

namespace TuringLite;

/// <summary>
/// Lienzo del editor: muestra la vista previa escalada del display y permite seleccionar, arrastrar,
/// redimensionar (asa inferior derecha) y mover con las flechas (Mayús = 10 px). Supr pide borrar.
/// Las coordenadas de los componentes viven en WidgetConfig; el lienzo solo las modifica.
/// </summary>
sealed class LayoutCanvas : Control
{
    const int Pad = 16, HandleSize = 9, MinSize = 10;
    static readonly Color Accent = Ui.Accent;

    Rectangle _area;          // zona donde se dibuja la pantalla, en píxeles del control
    float _scale = 1f;
    Bitmap? _frame;
    Size _logical = new(480, 320);
    WidgetConfig? _selected;
    bool _moving, _resizing;
    Point _grab;              // desfase del clic respecto a la esquina del componente (en px lógicos)

    /// <summary>Componentes en orden de dibujo (el último queda encima).</summary>
    public IList<WidgetConfig> Items { get; set; } = new List<WidgetConfig>();

    /// <summary>Se dispara al seleccionar con el ratón (no al asignar Selected por código).</summary>
    public event Action<WidgetConfig?>? SelectionChanged;
    /// <summary>Se dispara mientras se mueve o redimensiona, o al moverlo con las flechas.</summary>
    public event Action? GeometryChanged;
    /// <summary>Se pide borrar el componente seleccionado (tecla Supr).</summary>
    public event Action? DeleteRequested;

    public Size Logical
    {
        get => _logical;
        set { _logical = value; Relayout(); Invalidate(); }
    }

    /// <summary>Imagen ya compuesta de la pantalla. El lienzo se encarga de liberar la anterior.</summary>
    public Bitmap? Frame
    {
        get => _frame;
        set
        {
            var old = _frame;
            _frame = value;
            old?.Dispose();
            Invalidate();
        }
    }

    /// <summary>Selección visual. Asignarla por código no dispara SelectionChanged.</summary>
    public WidgetConfig? Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    public LayoutCanvas()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(0x0B, 0x0E, 0x12);
        TabStop = true;
    }

    void Relayout()
    {
        float sx = (Width - 2 * Pad) / (float)_logical.Width;
        float sy = (Height - 2 * Pad) / (float)_logical.Height;
        _scale = Math.Max(0.1f, Math.Min(sx, sy));
        int w = (int)Math.Round(_logical.Width * _scale), h = (int)Math.Round(_logical.Height * _scale);
        _area = new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
        Invalidate();
    }

    PointF ToLogical(Point p) => new((p.X - _area.X) / _scale, (p.Y - _area.Y) / _scale);

    Rectangle ToScreen(WidgetConfig c) => new(
        _area.X + (int)(c.X * _scale), _area.Y + (int)(c.Y * _scale),
        Math.Max(1, (int)(c.W * _scale)), Math.Max(1, (int)(c.H * _scale)));

    Rectangle HandleRect(WidgetConfig c)
    {
        var r = ToScreen(c);
        return new Rectangle(r.Right - HandleSize, r.Bottom - HandleSize, HandleSize, HandleSize);
    }

    WidgetConfig? HitTest(PointF p)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            var c = Items[i];
            if (p.X >= c.X && p.X < c.X + c.W && p.Y >= c.Y && p.Y < c.Y + c.H) return c;
        }
        return null;
    }

    static int Fit(int v, int min, int max) => Math.Max(min, Math.Min(v, max));

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;

        // El asa del componente seleccionado tiene prioridad sobre el cuerpo.
        if (_selected is { Locked: false } && HandleRect(_selected).Contains(e.Location))
        {
            _resizing = true;
            Capture = true;
            return;
        }

        var p = ToLogical(e.Location);
        var hit = HitTest(p);
        if (hit is { Locked: false })
        {
            _moving = true;
            Capture = true;
            _grab = new Point((int)(p.X - hit.X), (int)(p.Y - hit.Y));
        }
        if (hit != _selected)
        {
            _selected = hit;
            Invalidate();
            SelectionChanged?.Invoke(hit);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var c = _selected;
        if (c == null) { Cursor = Cursors.Default; return; }

        if (_resizing)
        {
            var p = ToLogical(e.Location);
            c.W = Fit((int)p.X - c.X, MinSize, Logical.Width - c.X);
            c.H = Fit((int)p.Y - c.Y, MinSize, Logical.Height - c.Y);
            GeometryChanged?.Invoke();
        }
        else if (_moving)
        {
            var p = ToLogical(e.Location);
            c.X = Fit((int)p.X - _grab.X, 0, Logical.Width - c.W);
            c.Y = Fit((int)p.Y - _grab.Y, 0, Logical.Height - c.H);
            GeometryChanged?.Invoke();
        }
        else if (c.Locked)
        {
            Cursor = Cursors.Default;
        }
        else
        {
            Cursor = HandleRect(c).Contains(e.Location) ? Cursors.SizeNWSE
                : HitTest(ToLogical(e.Location)) == c ? Cursors.SizeAll
                : Cursors.Default;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _moving = _resizing = false;
        Capture = false;
    }

    protected override bool IsInputKey(Keys key) =>
        key is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(key);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Delete) { DeleteRequested?.Invoke(); e.Handled = true; return; }
        var c = _selected;
        if (c == null || c.Locked) return;
        int step = e.Shift ? 10 : 1, dx = 0, dy = 0;
        switch (e.KeyCode)
        {
            case Keys.Left: dx = -step; break;
            case Keys.Right: dx = step; break;
            case Keys.Up: dy = -step; break;
            case Keys.Down: dy = step; break;
            default: return;
        }
        c.X = Fit(c.X + dx, 0, Logical.Width - c.W);
        c.Y = Fit(c.Y + dy, 0, Logical.Height - c.H);
        e.Handled = true;
        GeometryChanged?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        using (var border = new Pen(Ui.Edge))
            g.DrawRectangle(border, _area.X - 1, _area.Y - 1, _area.Width + 1, _area.Height + 1);

        if (_frame != null)
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(_frame, _area);
        }

        // Contorno tenue de cada componente, discontinuo si está oculto.
        foreach (var c in Items)
        {
            if (c == _selected) continue;
            var r = ToScreen(c);
            var color = c.Enabled ? Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x90, 0x90, 0x90);
            using var pen = new Pen(color) { DashStyle = c.Enabled ? DashStyle.Solid : DashStyle.Dash };
            g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        if (_selected != null)
        {
            var r = ToScreen(_selected);
            using (var pen = new Pen(Accent, 2))
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            // Bloqueado: sin asa de tamaño, para que se vea que no se puede mover.
            if (!_selected.Locked)
            {
                var h = HandleRect(_selected);
                g.FillRectangle(Brushes.White, h);
                // El asa es blanca para verse sobre cualquier fondo de tema.
                using var hp = new Pen(Accent);
                g.DrawRectangle(hp, h.X, h.Y, h.Width - 1, h.Height - 1);
            }
        }

        if (Focused)
            ControlPaint.DrawFocusRectangle(g, new Rectangle(0, 0, Width - 1, Height - 1));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _frame?.Dispose();
        base.Dispose(disposing);
    }
}
