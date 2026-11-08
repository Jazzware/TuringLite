using System.Drawing;
using System.Runtime.InteropServices;

namespace TuringLite;

/// <summary>
/// Paleta y piezas compartidas por las ventanas: tema oscuro con el acento menta de siempre.
/// WinForms no trae tema oscuro: aquí se pinta lo que el sistema dejaría en claro (barra de título,
/// listas, desplegables, botones, tarjetas).
/// </summary>
static class Ui
{
    public static readonly Color Window = Color.FromArgb(0x11, 0x14, 0x18);
    public static readonly Color Surface = Color.FromArgb(0x18, 0x1D, 0x24);
    public static readonly Color Field = Color.FromArgb(0x1E, 0x25, 0x2E);
    public static readonly Color Hover = Color.FromArgb(0x2A, 0x33, 0x40);
    public static readonly Color Edge = Color.FromArgb(0x2E, 0x37, 0x43);
    public static readonly Color Ink = Color.FromArgb(0xE6, 0xEA, 0xF0);
    public static readonly Color Dim = Color.FromArgb(0x8A, 0x94, 0xA3);
    public static readonly Color Accent = Color.FromArgb(0x7D, 0xD3, 0xA8);
    public static readonly Color AccentHi = Color.FromArgb(0x9A, 0xE0, 0xBD);
    public static readonly Color OnAccent = Color.FromArgb(0x0D, 0x11, 0x15);
    public static readonly Color Warn = Color.FromArgb(0xF5, 0xC2, 0x6B);
    public static readonly Color Danger = Color.FromArgb(0xF0, 0x70, 0x7A);

    public static readonly Font BodyFont = new("Segoe UI", 9.5f);
    public static readonly Font SmallFont = new("Segoe UI", 8.5f);
    public static readonly Font TitleFont = new("Segoe UI Semibold", 15f);
    public static readonly Font SectionFont = new("Segoe UI Semibold", 9.5f);

    static readonly Pen EdgePen = new(Edge);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string? subIdList);

    /// <summary>Barra de título oscura (Windows 10 1809 o posterior). En otras versiones no hace nada.</summary>
    static void DarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE desde Windows 10 20H1; 19 en builds anteriores.
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
    }

    /// <summary>
    /// Aplica el tema oscuro a una ventana y a todo lo que contiene. Solo cambia los colores que siguen
    /// en los valores por defecto, así que los textos atenuados y los botones ya estilizados se respetan.
    /// </summary>
    public static void Dark(Form form)
    {
        form.BackColor = Window;
        form.ForeColor = Ink;
        form.Font = BodyFont;
        if (form.IsHandleCreated) DarkTitleBar(form.Handle);
        else form.HandleCreated += (_, _) => DarkTitleBar(form.Handle);
        Walk(form);
    }

    static void Walk(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            Style(c, parent);
            Walk(c);
        }
    }

    static void Style(Control c, Control parent)
    {
        switch (c)
        {
            case TextBox t:
                t.BorderStyle = BorderStyle.FixedSingle;
                t.BackColor = Field;
                t.ForeColor = Ink;
                break;
            case NumericUpDown n:
                n.BorderStyle = BorderStyle.FixedSingle;
                n.BackColor = Field;
                n.ForeColor = Ink;
                break;
            case ComboBox cb:
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = Field;
                cb.ForeColor = Ink;
                if (cb.DrawMode != DrawMode.OwnerDrawFixed)
                {
                    cb.DrawMode = DrawMode.OwnerDrawFixed;
                    cb.DrawItem += DrawComboItem;
                }
                break;
            case ListView lv:
                lv.BackColor = Field;
                lv.ForeColor = Ink;
                lv.BorderStyle = BorderStyle.FixedSingle;
                // Cabecera dibujada a mano: el tema del sistema la deja blanca en un formulario oscuro.
                lv.OwnerDraw = true;
                lv.DrawColumnHeader += DrawListHeader;
                lv.DrawItem += (_, e) => e.DrawDefault = true;
                lv.DrawSubItem += (_, e) => e.DrawDefault = true;
                DarkNative(lv);
                break;
            case TrackBar tb:
                tb.BackColor = parent.BackColor;
                DarkNative(tb);
                break;
            case CheckBox:
                c.BackColor = parent.BackColor;
                c.ForeColor = Ink;
                break;
            case Label l when l.ForeColor == SystemColors.ControlText:
                l.ForeColor = Ink;
                break;
        }
    }

    /// <summary>Tema oscuro nativo de Explorer para controles con barras de desplazamiento o deslizadores.</summary>
    static void DarkNative(Control c)
    {
        if (c.IsHandleCreated) SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
        else c.HandleCreated += (_, _) => SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
    }

    static void DrawListHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using (var bg = new SolidBrush(Surface)) e.Graphics.FillRectangle(bg, e.Bounds);
        using (var line = new Pen(Edge)) e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        if (e.Header is not ColumnHeader header) return;
        var align = header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left;
        TextRenderer.DrawText(e.Graphics, header.Text, SectionFont, e.Bounds, Dim,
            align | TextFormatFlags.VerticalCenter | TextFormatFlags.LeftAndRightPadding);
    }

    static void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        bool selected = e.Index >= 0 && (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(selected ? Hover : Field)) e.Graphics.FillRectangle(bg, e.Bounds);
        if (e.Index < 0) return;
        TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), cb.Font, e.Bounds, Ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.LeftAndRightPadding);
    }

    /// <summary>Botón plano. Primario = acento menta (la acción principal de cada ventana).</summary>
    public static Button MakeButton(string text, int x, int y, int w, int h, bool primary, Action onClick)
    {
        var b = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = BodyFont,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = 1;
        if (primary)
        {
            b.BackColor = Accent;
            b.ForeColor = OnAccent;
            b.FlatAppearance.BorderColor = Accent;
            b.FlatAppearance.MouseOverBackColor = AccentHi;
            b.FlatAppearance.MouseDownBackColor = Accent;
        }
        else
        {
            b.BackColor = Field;
            b.ForeColor = Ink;
            b.FlatAppearance.BorderColor = Edge;
            b.FlatAppearance.MouseOverBackColor = Hover;
            b.FlatAppearance.MouseDownBackColor = Edge;
        }
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Etiqueta. Atenuada = texto secundario (pistas, unidades, ayudas).</summary>
    public static Label MakeLabel(string text, int x, int y, bool dim = false, Font? font = null) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(x, y),
        ForeColor = dim ? Dim : Ink,
        Font = font ?? BodyFont,
    };

    /// <summary>Tarjeta: panel de superficie con borde fino y título opcional. Los hijos se colocan en sus coordenadas.</summary>
    public static Panel MakeCard(int x, int y, int w, int h, string title)
    {
        var p = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = Surface };
        p.Paint += (_, e) => e.Graphics.DrawRectangle(EdgePen, 0, 0, p.Width - 1, p.Height - 1);
        if (title.Length > 0) p.Controls.Add(MakeLabel(title, 14, 12, font: SectionFont));
        return p;
    }
}
