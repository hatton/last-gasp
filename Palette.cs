namespace LastGasp;

/// <summary>Colours, following the Windows 11 light theme.</summary>
internal static class Palette
{
    public static readonly Color WindowBackground = Color.FromArgb(243, 243, 243);
    public static readonly Color PrimaryText = Color.FromArgb(26, 26, 26);
    public static readonly Color SecondaryText = Color.FromArgb(96, 96, 96);
    public static readonly Color Track = Color.FromArgb(200, 200, 200);
    public static readonly Color Divider = Color.FromArgb(222, 222, 222);

    // A warm yellow that stays readable with black text, not a fluorescent one.
    public static readonly Color Panting = Color.FromArgb(230, 176, 30);
    public static readonly Color PantingText = Color.FromArgb(157, 93, 0);
    public static readonly Color Gasping = Color.FromArgb(196, 43, 28);

    public static readonly Color Accent = Color.FromArgb(0, 95, 184);
    public static readonly Color AccentHover = Color.FromArgb(25, 117, 197);
    public static readonly Color AccentPressed = Color.FromArgb(49, 131, 202);
    public static readonly Color AccentDisabled = Color.FromArgb(197, 197, 197);
}
