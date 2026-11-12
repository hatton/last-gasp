using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace LastGasp;

/// <summary>
/// Lives on the taskbar. The icon shows the percent of memory in use, the label shows CPU use
/// (or "Free Now" when Gasping), and clicking the button opens this window or frees memory.
/// </summary>
internal sealed class MainWindow : Form
{
    private const int UpdateIntervalMs = 2000;

    // The error that starting a process as administrator gives when the user declines the UAC prompt.
    private const int ERROR_CANCELLED = 1223;

    private readonly Settings _settings = Settings.Load();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = UpdateIntervalMs };

    // The counter that Task Manager's CPU figure uses. It can exceed 100 when the
    // CPU runs above its base clock, and Task Manager caps it at 100.
    private readonly PerformanceCounter _cpuCounter = new("Processor Information", "% Processor Utility", "_Total");

    // Sizes and fonts are set in ApplyScale, not here.
    private readonly TableLayoutPanel _layout = new()
    {
        ColumnCount = 2,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Fill,
    };
    // The title bar shows the readings, so the app's name goes here.
    private readonly FlushLabel _heading = new()
    {
        Text = "Last Gasp",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Palette.PrimaryText,
        Margin = Padding.Empty,
    };
    // Memory leads, on the left; CPU is the smaller figure on the right.
    private readonly FlushLabel _memoryValue = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, ForeColor = Palette.PrimaryText,
    };
    private readonly FlushLabel _cpuValue = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomRight, ForeColor = Palette.PrimaryText,
    };
    private readonly FlushLabel _memoryCaption = MakeLabel(ContentAlignment.TopLeft, Palette.SecondaryText, "Memory");
    private readonly FlushLabel _cpuCaption = MakeLabel(ContentAlignment.TopRight, Palette.SecondaryText, "CPU");

    // Separates the readings and the Free button from the settings below.
    private readonly Label _divider = new() { Dock = DockStyle.Fill, BackColor = Palette.Divider };
    private readonly FlushLabel _settingsHeading = MakeLabel(ContentAlignment.MiddleLeft, Palette.PrimaryText, "When to warn");

    private readonly ThresholdSlider _thresholds = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Palette.WindowBackground,
    };
    private readonly CheckBox _showCpu = new()
    {
        Text = "Show CPU %",
        AutoSize = true,
        ForeColor = Palette.PrimaryText,
        Cursor = Cursors.Hand,
    };
    // Ticked means freeing skips the UAC prompt, which is the opposite of Settings.AskForAdmin.
    private readonly CheckBox _skipPrompt = new()
    {
        Text = "Free memory without a permission prompt",
        AutoSize = true,
        TextImageRelation = TextImageRelation.TextBeforeImage,
        ForeColor = Palette.PrimaryText,
        Cursor = Cursors.Hand,
    };
    private readonly Label _skipPromptNote = new()
    {
        Text = "Adds a Windows scheduled task that runs Last Gasp's freeing step as administrator. "
            + "Setting it up or removing it asks for permission once.",
        Dock = DockStyle.Fill,
        ForeColor = Palette.SecondaryText,
    };
    private bool _revertingCheckBox;
    private readonly RoundedButton _freeButton = new()
    {
        Text = "Free Memory Now",
        Dock = DockStyle.Fill,
        Fill = Palette.Accent,
        HoverFill = Palette.AccentHover,
        PressedFill = Palette.AccentPressed,
        DisabledFill = Palette.AccentDisabled,
        ForeColor = Color.White,
        Cursor = Cursors.Hand,
    };
    private readonly Label _freeStatus = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Palette.SecondaryText,
    };

    private Icon? _currentIcon;
    private int _memoryPercent;
    private int _cpuPercent;
    private bool _freeing;

    public MainWindow()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Palette.WindowBackground;

        _thresholds.SetThresholds(_settings.PantingPercent, _settings.GaspingPercent);
        _skipPrompt.Checked = !_settings.AskForAdmin;
        _showCpu.Checked = _settings.ShowCpu;
        ShowOrHideCpu();

        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
        _layout.Controls.Add(_heading, 0, 0);
        _layout.SetColumnSpan(_heading, 2);
        _layout.Controls.Add(_memoryValue, 0, 1);
        _layout.Controls.Add(_cpuValue, 1, 1);
        _layout.Controls.Add(_memoryCaption, 0, 2);
        _layout.Controls.Add(_cpuCaption, 1, 2);
        // Seeing and acting come first; the settings, changed rarely, come after the divider.
        int row = 3;
        foreach (var control in new Control[]
            { _freeButton, _freeStatus, _divider, _settingsHeading, _thresholds, _showCpu, _skipPrompt, _skipPromptNote })
        {
            _layout.Controls.Add(control, 0, row++);
            _layout.SetColumnSpan(control, 2);
        }
        Controls.Add(_layout);

        // Size the window before minimizing it; a size set while minimized is lost.
        ApplyScale(DeviceDpi);
        WindowState = FormWindowState.Minimized;

        _thresholds.ThresholdsChanged += (_, _) =>
        {
            _settings.PantingPercent = _thresholds.Panting;
            _settings.GaspingPercent = _thresholds.Gasping;
            ShowReadings();
        };
        _thresholds.ThresholdsCommitted += (_, _) => _settings.Save();
        _showCpu.CheckedChanged += (_, _) =>
        {
            _settings.ShowCpu = _showCpu.Checked;
            _settings.Save();
            ShowOrHideCpu();
            FitWindowToContent();
            ShowReadings();
        };
        _skipPrompt.CheckedChanged += async (_, _) =>
        {
            if (!_revertingCheckBox)
                await SetAskForAdminAsync(!_skipPrompt.Checked);
        };
        _freeButton.Click += async (_, _) => await FreeMemoryAsync();
        _timer.Tick += (_, _) => TakeReadings();
    }

    private bool IsGasping => _memoryPercent >= _settings.GaspingPercent;
    private bool IsPanting => _memoryPercent >= _settings.PantingPercent;

    private static FlushLabel MakeLabel(ContentAlignment align, Color color, string text = "") => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = align,
        ForeColor = color,
        Margin = Padding.Empty,
    };

    // The main monitor's scale: 1 at 100%, 1.5 at 150%. The app is system-aware (see
    // LastGasp.csproj), so it lays out once at this scale and Windows resizes the window
    // on monitors with a different scale. Per-monitor scaling let WinForms rescale the
    // fonts but not the spacing, so the two drifted apart.
    private float _scale = 1f;

    private int S(int pixels) => (int)Math.Round(pixels * _scale);

    // Fonts are sized in pixels, so that text and spacing scale together.
    private Font UiFont(float points, bool semibold = false) =>
        new(semibold ? "Segoe UI Semibold" : "Segoe UI", points * 96f / 72f * _scale, GraphicsUnit.Pixel);

    /// <summary>Sets every size and font for a monitor at the given DPI, then sizes the window.</summary>
    private void ApplyScale(int dpi)
    {
        _scale = dpi / 96f;
        SuspendLayout();
        _layout.SuspendLayout();

        // Spacing steps of 4: 4 and 8 inside a group, 16 between related rows, 24 between groups.
        Font = UiFont(9.5f);
        Padding = new Padding(S(24), S(16), S(24), S(20));
        _layout.ColumnStyles[0].Width = S(176);
        _layout.ColumnStyles[1].Width = S(176);
        int width = S(352);

        _heading.Font = UiFont(11f, semibold: true);
        _heading.Height = S(24);

        _memoryValue.Font = UiFont(34f, semibold: true);
        _memoryValue.Height = S(60);
        _memoryValue.Margin = new Padding(0, S(8), 0, 0);
        _cpuValue.Font = UiFont(18f, semibold: true);
        _cpuValue.Height = S(60);
        _cpuValue.Margin = new Padding(0, S(8), 0, 0);
        foreach (var caption in new[] { _memoryCaption, _cpuCaption })
        {
            caption.Font = UiFont(9.5f);
            caption.Height = S(20);
        }

        _freeButton.Font = UiFont(10.5f, semibold: true);
        _freeButton.CornerRadius = 4 * _scale;
        _freeButton.Height = S(40);
        _freeButton.Margin = new Padding(0, S(16), 0, 0);

        _freeStatus.Font = UiFont(9f);
        _freeStatus.Height = S(20);
        _freeStatus.Margin = new Padding(0, S(4), 0, 0);

        _divider.Height = Math.Max(1, S(1));
        _divider.Margin = new Padding(0, S(16), 0, 0);

        _settingsHeading.Font = UiFont(10.5f, semibold: true);
        _settingsHeading.Height = S(24);
        _settingsHeading.Margin = new Padding(0, S(20), 0, 0);

        _thresholds.UiScale = _scale;
        _thresholds.Height = S(86);
        _thresholds.Margin = new Padding(0, S(8), 0, 0);

        _showCpu.Font = UiFont(9.5f);
        _showCpu.Margin = new Padding(0, S(16), 0, 0);

        _skipPrompt.Font = UiFont(9.5f);
        _skipPrompt.Margin = new Padding(0, S(12), 0, 0);

        // Line the note up with the checkbox's text, not its box. Measured before the shield
        // is added after the text, so that the shield's width isn't counted.
        _skipPrompt.Image = null;
        int indent = _skipPrompt.GetPreferredSize(Size.Empty).Width
            - TextRenderer.MeasureText(_skipPrompt.Text, _skipPrompt.Font).Width;

        // The UAC shield marks the controls that ask for administrator permission.
        _shield?.Dispose();
        using (var shieldIcon = SystemIcons.GetStockIcon(StockIconId.Shield, S(16)))
            _shield = new Bitmap(shieldIcon.ToBitmap(), S(16), S(16));
        _skipPrompt.Image = _shield;
        ShowShieldOnFreeButton();
        _skipPromptNote.Font = UiFont(8.5f);
        _skipPromptNote.Margin = new Padding(indent, S(2), 0, 0);
        _skipPromptNote.Height = TextRenderer.MeasureText(_skipPromptNote.Text, _skipPromptNote.Font,
            new Size(width - indent, int.MaxValue), TextFormatFlags.WordBreak).Height + S(2);

        _layout.ResumeLayout(true);
        ResumeLayout(true);
        FitWindowToContent();
    }

    private Bitmap? _shield;

    // Freeing asks for permission unless the scheduled task is set up.
    private void ShowShieldOnFreeButton()
    {
        _freeButton.Image = _settings.AskForAdmin ? _shield : null;
        _freeButton.Invalidate();
    }

    private void FitWindowToContent()
    {
        var content = _layout.GetPreferredSize(Size.Empty);
        ClientSize = new Size(content.Width + Padding.Horizontal, content.Height + Padding.Vertical);
    }

    // With CPU off, the title bar already says "Last Gasp", so the window's own heading goes too.
    private void ShowOrHideCpu()
    {
        _heading.Visible = _settings.ShowCpu;
        _cpuValue.Visible = _settings.ShowCpu;
        _cpuCaption.Visible = _settings.ShowCpu;
    }

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_RESTORE = 0xF120;
    private const int CS_NOCLOSE = 0x0200;

    // Greys out the close button, so the app can't be closed by accident. Windows can't
    // show the minimize button without the close button.
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_NOCLOSE;
            return cp;
        }
    }

    // Clicking the taskbar button of a minimized window sends SC_RESTORE. When Gasping,
    // free memory instead of opening the window, which could take a long time to appear
    // while the machine is paging.
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_SYSCOMMAND && ((int)m.WParam & 0xFFF0) == SC_RESTORE && IsGasping)
        {
            _ = FreeMemoryAsync();
            return;
        }
        base.WndProc(ref m);
    }

    private async Task FreeMemoryAsync()
    {
        if (_freeing)
            return;
        _freeing = true;
        _freeButton.Enabled = false;
        _freeButton.Text = "Freeing…";
        _freeStatus.Text = "";
        Text = "Freeing…";
        try
        {
            int before = TakeReadings();

            // Freeing needs admin rights: either the scheduled task has them, or a second copy
            // of this exe asks for them with a UAC prompt.
            int exitCode = !_settings.AskForAdmin && FreeTask.Exists()
                ? await FreeTask.RunAsync()
                : await RunAsAdminAsync("--free");
            if (exitCode != 0)
            {
                _freeStatus.Text = $"Freeing failed: {new Win32Exception(exitCode).Message}";
                WindowState = FormWindowState.Normal;
                return;
            }

            int after = TakeReadings();
            _freeStatus.Text = $"Went from {before}% to {after}%";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            _freeStatus.Text = "Cancelled";
        }
        catch (Exception ex)
        {
            _freeStatus.Text = ex.Message;
            WindowState = FormWindowState.Normal;
        }
        finally
        {
            _freeing = false;
            _freeButton.Text = "Free Memory Now";
            _freeButton.Enabled = true;
            ShowReadings();
        }
    }

    /// <summary>Runs a second copy of this exe as administrator, which shows a UAC prompt.</summary>
    /// <exception cref="Win32Exception">ERROR_CANCELLED if the user declines the prompt.</exception>
    private static async Task<int> RunAsAdminAsync(string arguments)
    {
        var startInfo = new ProcessStartInfo(Environment.ProcessPath!, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        using var process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    /// <summary>
    /// Turning the prompt off creates the scheduled task; turning it back on deletes it.
    /// Both need admin rights, so each shows one UAC prompt.
    /// </summary>
    private async Task SetAskForAdminAsync(bool ask)
    {
        _skipPrompt.Enabled = false;
        try
        {
            string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            int exitCode = await RunAsAdminAsync(ask ? "--unregister-task" : $"--register-task \"{user}\"");
            if (exitCode != 0)
                throw new InvalidOperationException(Marshal.GetExceptionForHR(exitCode)?.Message ?? $"Error {exitCode}");

            _settings.AskForAdmin = ask;
            _settings.Save();
            ShowShieldOnFreeButton();
            _freeStatus.Text = "";
        }
        catch (Exception ex)
        {
            _freeStatus.Text = ex is Win32Exception { NativeErrorCode: ERROR_CANCELLED }
                ? "Cancelled"
                : $"Could not change this: {ex.Message}";
            _revertingCheckBox = true;
            _skipPrompt.Checked = ask;
            _revertingCheckBox = false;
        }
        finally
        {
            _skipPrompt.Enabled = true;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        TakeReadings();
        _timer.Start();
    }

    /// <returns>Percent of physical memory in use.</returns>
    private int TakeReadings()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref status))
            _memoryPercent = (int)Math.Round((double)(status.ullTotalPhys - status.ullAvailPhys) / status.ullTotalPhys * 100);

        try
        {
            // Returns 0 the first time it is read.
            _cpuPercent = (int)Math.Round(Math.Min(100f, _cpuCounter.NextValue()));
        }
        catch (InvalidOperationException)
        {
            _cpuPercent = 0;
        }

        ShowReadings();
        return _memoryPercent;
    }

    private void ShowReadings()
    {
        Text = _freeing ? "Freeing…"
            : IsGasping ? "Free Now"
            : _settings.ShowCpu ? $"CPU {_cpuPercent}%"
            : "Last Gasp";
        _memoryValue.Text = $"{_memoryPercent}%";
        _memoryValue.ForeColor = IsGasping ? Palette.Gasping
            : IsPanting ? Palette.PantingText
            : Palette.PrimaryText;
        _cpuValue.Text = $"{_cpuPercent}%";
        SetIcon();
    }

    // The taskbar follows Windows' mode (Settings > Personalization > Colors > "Choose your
    // mode"), not the apps' mode. Read each time the icon is drawn, so a change shows within
    // one update.
    private static bool TaskbarIsLight()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }

    private void SetIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            // With no background, the number sits straight on the taskbar, so it must contrast with it.
            var textColor = TaskbarIsLight() ? Palette.PrimaryText : Color.White;
            if (IsGasping)
            {
                using var brush = new SolidBrush(Palette.Gasping);
                g.FillRectangle(brush, 0, 0, 32, 32);
            }
            else if (IsPanting)
            {
                using var brush = new SolidBrush(Palette.Panting);
                g.FillRectangle(brush, 0, 0, 32, 32);
                textColor = Color.Black;
            }

            string text = _memoryPercent.ToString();
            float size = text.Length >= 3 ? 12f : 17f;
            using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(textColor);
            var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, textBrush, new RectangleF(0, 0, 32, 32), format);
        }

        var newIcon = Icon.FromHandle(bitmap.GetHicon());
        Icon = newIcon;

        if (_currentIcon != null)
        {
            DestroyIcon(_currentIcon.Handle);
            _currentIcon.Dispose();
        }
        _currentIcon = newIcon;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _cpuCounter.Dispose();
            if (_currentIcon != null)
            {
                DestroyIcon(_currentIcon.Handle);
                _currentIcon.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
