namespace LastGasp;

/// <summary>
/// A label drawn without the padding that Windows adds around text. That padding grows with
/// the font, so large text in an ordinary label sits visibly inside the edge of the smaller
/// text around it. This one keeps it flush.
/// </summary>
internal sealed class FlushLabel : Label
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        flags |= TextAlign switch
        {
            ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter,
        };
        flags |= TextAlign switch
        {
            ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight => TextFormatFlags.Top,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => TextFormatFlags.Bottom,
            _ => TextFormatFlags.VerticalCenter,
        };
        e.Graphics.Clear(BackColor);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, flags);
    }
}
