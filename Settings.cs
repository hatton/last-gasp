using System.Text.Json;

namespace LastGasp;

/// <summary>The user's choices, kept in %APPDATA%\Last Gasp\settings.json.</summary>
internal sealed class Settings
{
    public const int DefaultPanting = 70;
    public const int DefaultGasping = 80;

    /// <summary>Memory percent at which the icon turns yellow.</summary>
    public int PantingPercent { get; set; } = DefaultPanting;

    /// <summary>Memory percent at which the icon turns red and the label reads "Free Now".</summary>
    public int GaspingPercent { get; set; } = DefaultGasping;

    /// <summary>
    /// When true, each free shows a UAC prompt. When false, freeing runs the scheduled task
    /// in <see cref="FreeTask"/>, which needs no prompt.
    /// </summary>
    public bool AskForAdmin { get; set; } = true;

    /// <summary>
    /// When true, the taskbar label shows CPU use and the window shows the CPU figure and its
    /// own "Last Gasp" heading. When false, the label and title bar just say "Last Gasp".
    /// </summary>
    public bool ShowCpu { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Last Gasp", "settings.json");

    public static Settings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
            if (settings != null
                && settings.PantingPercent >= ThresholdSlider.Min
                && settings.PantingPercent < settings.GaspingPercent
                && settings.GaspingPercent <= ThresholdSlider.Max)
                return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
