using System.Drawing.Drawing2D;

namespace LastGasp;

/// <summary>
/// A line from 50% to 100% with two handles: Panting (yellow) and Gasping (red).
/// Panting always stays below Gasping.
/// </summary>
internal sealed class ThresholdSlider : Control
{
    public const int Min = 50;
    public const int Max = 100;

    public int Panting { get; private set; } = Settings.DefaultPanting;
    public int Gasping { get; private set; } = Settings.DefaultGasping;

    /// <summary>Raised whenever a handle moves, including during a drag.</summary>
    public event EventHandler? ThresholdsChanged;

    /// <summary>Raised once when the user lets go of a handle.</summary>
    public event EventHandler? ThresholdsCommitted;

    private enum Thumb { None, Panting, Gasping }
    private Thumb _dragging;

    public ThresholdSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public void SetThresholds(int panting, int gasping)
    {
        Panting = panting;
        Gasping = gasping;
        Invalidate();
    }

    private float _uiScale = 1f;

    /// <summary>The scale of the monitor the window is on: 1 at 100%, 1.5 at 150%. Set by the window.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float UiScale
    {
        get => _uiScale;
        set { _uiScale = value; Invalidate(); }
    }

    private float DpiScale => _uiScale;

    // Fonts are sized in pixels so that they scale with everything else in the window.
    private Font LegendFont() => new("Segoe UI", 9.5f * 96f / 72f * DpiScale, GraphicsUnit.Pixel);
    private Font TickFont() => new("Segoe UI", 8.5f * 96f / 72f * DpiScale, GraphicsUnit.Pixel);

    private float HandleRadius => 8 * DpiScale;

    // Far enough in from each end for a handle, and for the "50%" and "100%" labels to fit.
    private float Inset
    {
        get
        {
            using var font = TickFont();
            return Math.Max(HandleRadius + 2 * DpiScale, TextRenderer.MeasureText("100%", font).Width / 2f);
        }
    }
    private float LegendHeight => 22 * DpiScale;
    private float ValueRowHeight => 18 * DpiScale;
    private float TrackY => LegendHeight + 6 * DpiScale + ValueRowHeight + HandleRadius + 2 * DpiScale;

    private Font ValueFont() => new("Segoe UI Semibold", 8.5f * 96f / 72f * DpiScale, GraphicsUnit.Pixel);

    private float XFor(int percent) => Inset + (Width - 2 * Inset) * (percent - Min) / (Max - Min);

    private int PercentAt(int x) =>
        (int)Math.Round(Min + (x - Inset) * (Max - Min) / (Width - 2 * Inset));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Legend: Panting on the left, Gasping on the right.
        using var legendFont = LegendFont();
        DrawLegend(g, legendFont, "Panting", Palette.Panting, alignRight: false);
        DrawLegend(g, legendFont, "Gasping", Palette.Gasping, alignRight: true);

        DrawValues(g);

        // Track: grey below Panting, yellow from Panting to Gasping, red from Gasping up.
        float thickness = 4 * DpiScale;
        DrawSegment(g, Min, Panting, Palette.Track, thickness);
        DrawSegment(g, Panting, Gasping, Palette.Panting, thickness);
        DrawSegment(g, Gasping, Max, Palette.Gasping, thickness);

        // Tick labels every 10%.
        using var tickFont = TickFont();
        for (int p = Min; p <= Max; p += 10)
        {
            var size = TextRenderer.MeasureText(g, $"{p}%", tickFont);
            var point = new Point((int)(XFor(p) - size.Width / 2f), (int)(TrackY + HandleRadius + 4 * DpiScale));
            TextRenderer.DrawText(g, $"{p}%", tickFont, point, Palette.SecondaryText);
        }

        DrawHandle(g, Panting, Palette.Panting);
        DrawHandle(g, Gasping, Palette.Gasping);
    }

    // Each handle's value, centred above it. When the handles are close, the two values are
    // pushed apart so they touch instead of overlapping, and both stay inside the control.
    private void DrawValues(Graphics g)
    {
        using var font = ValueFont();
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        string pantingText = $"{Panting}%", gaspingText = $"{Gasping}%";
        int pantingWidth = TextRenderer.MeasureText(g, pantingText, font, Size.Empty, flags).Width;
        int gaspingWidth = TextRenderer.MeasureText(g, gaspingText, font, Size.Empty, flags).Width;
        float gap = 4 * DpiScale;

        float pantingLeft = XFor(Panting) - pantingWidth / 2f;
        float gaspingLeft = XFor(Gasping) - gaspingWidth / 2f;
        float overlap = pantingLeft + pantingWidth + gap - gaspingLeft;
        if (overlap > 0)
        {
            pantingLeft -= overlap / 2;
            gaspingLeft += overlap / 2;
        }
        // Keep both inside the control, moving the other along if one hits an edge.
        if (pantingLeft < 0)
        {
            gaspingLeft = Math.Max(gaspingLeft, pantingWidth + gap);
            pantingLeft = 0;
        }
        if (gaspingLeft + gaspingWidth > Width)
        {
            gaspingLeft = Width - gaspingWidth;
            pantingLeft = Math.Min(pantingLeft, gaspingLeft - gap - pantingWidth);
        }

        int top = (int)(LegendHeight + 6 * DpiScale);
        int height = (int)ValueRowHeight;
        TextRenderer.DrawText(g, pantingText, font, new Rectangle((int)pantingLeft, top, pantingWidth, height),
            Palette.PantingText, flags | TextFormatFlags.Bottom);
        TextRenderer.DrawText(g, gaspingText, font, new Rectangle((int)gaspingLeft, top, gaspingWidth, height),
            Palette.Gasping, flags | TextFormatFlags.Bottom);
    }

    private void DrawLegend(Graphics g, Font font, string text, Color color, bool alignRight)
    {
        float dot = 10 * DpiScale;
        float gap = 6 * DpiScale;
        var size = TextRenderer.MeasureText(g, text, font);
        float left = alignRight ? Width - size.Width - dot - gap : 0;
        using (var brush = new SolidBrush(color))
            g.FillEllipse(brush, left, (LegendHeight - dot) / 2, dot, dot);
        TextRenderer.DrawText(g, text, font,
            new Point((int)(left + dot + gap), (int)((LegendHeight - size.Height) / 2)), Palette.PrimaryText);
    }

    private void DrawSegment(Graphics g, int from, int to, Color color, float thickness)
    {
        if (to <= from)
            return;
        using var pen = new Pen(color, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, XFor(from), TrackY, XFor(to), TrackY);
    }

    private void DrawHandle(Graphics g, int percent, Color color)
    {
        float r = HandleRadius;
        float x = XFor(percent);
        using var fill = new SolidBrush(color);
        using var ring = new Pen(Color.White, 2 * DpiScale);
        using var outline = new Pen(ControlPaint.Dark(color, 0.1f), 1 * DpiScale);
        g.FillEllipse(fill, x - r, TrackY - r, 2 * r, 2 * r);
        g.DrawEllipse(ring, x - r + DpiScale, TrackY - r + DpiScale, 2 * r - 2 * DpiScale, 2 * r - 2 * DpiScale);
        g.DrawEllipse(outline, x - r, TrackY - r, 2 * r, 2 * r);
    }

    private Thumb NearestThumb(int x)
    {
        float toPanting = Math.Abs(x - XFor(Panting));
        float toGasping = Math.Abs(x - XFor(Gasping));
        if (toPanting == toGasping)
            return x < XFor(Panting) ? Thumb.Panting : Thumb.Gasping;
        return toPanting < toGasping ? Thumb.Panting : Thumb.Gasping;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;
        _dragging = NearestThumb(e.X);
        MoveHandle(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging != Thumb.None)
        {
            MoveHandle(e.X);
            return;
        }
        bool overHandle = Math.Abs(e.Y - TrackY) <= HandleRadius
            && (Math.Abs(e.X - XFor(Panting)) <= HandleRadius || Math.Abs(e.X - XFor(Gasping)) <= HandleRadius);
        Cursor = overHandle ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging == Thumb.None)
            return;
        _dragging = Thumb.None;
        ThresholdsCommitted?.Invoke(this, EventArgs.Empty);
    }

    private void MoveHandle(int x)
    {
        int percent = PercentAt(x);
        int panting = Panting, gasping = Gasping;
        if (_dragging == Thumb.Panting)
            panting = Math.Clamp(percent, Min, Gasping - 1);
        else
            gasping = Math.Clamp(percent, Panting + 1, Max);
        if (panting == Panting && gasping == Gasping)
            return;
        SetThresholds(panting, gasping);
        ThresholdsChanged?.Invoke(this, EventArgs.Empty);
    }
}
