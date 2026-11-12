using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace LastGasp;

/// <summary>A flat button with rounded corners, drawn like a Windows 11 accent button.</summary>
internal sealed class RoundedButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverFill { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color PressedFill { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color DisabledFill { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float CornerRadius { get; set; } = 4f;

    private bool _hover;
    private bool _pressed;

    public RoundedButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? SystemColors.Control);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var fill = !Enabled ? DisabledFill : _pressed ? PressedFill : _hover ? HoverFill : Fill;
        float radius = CornerRadius;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using (var path = new GraphicsPath())
        {
            float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }

        // The image, if any, sits before the text, and the two are centred together.
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        int textWidth = TextRenderer.MeasureText(g, Text, Font, Size, flags).Width;
        int gap = Image == null ? 0 : Image.Width / 2;
        int imageWidth = Image?.Width ?? 0;
        int left = (Width - imageWidth - gap - textWidth) / 2;
        if (Image != null)
            g.DrawImage(Image, left, (Height - Image.Height) / 2, Image.Width, Image.Height);
        TextRenderer.DrawText(g, Text, Font,
            new Rectangle(left + imageWidth + gap, 0, textWidth, Height), ForeColor, flags);
    }
}
