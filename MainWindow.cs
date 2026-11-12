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

    // Windows scales fonts for the display's DPI but not pixel sizes, so every pixel
    // size in this form goes through S().
    private static readonly float DpiScale = GetDpiScale();

    private static float GetDpiScale()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return g.DpiX / 96f;
    }

    private static int S(int pixels) => (int)Math.Round(pixels * DpiScale);

    private readonly Settings _settings = Settings.Load();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = UpdateIntervalMs };

    // The counter that Task Manager's CPU figure uses. It can exceed 100 when the
    // CPU runs above its base clock, and Task Manager caps it at 100.
    private readonly PerformanceCounter _cpuCounter = new("Processor Information", "% Processor Utility", "_Total");

    private readonly Label _memoryValue = MakeValueLabel();
    private readonly Label _cpuValue = MakeValueLabel();
    private readonly ThresholdSlider _thresholds = new()
    {
        Dock = DockStyle.Fill,
        Height = S(66),
        Margin = new Padding(0, S(12), 0, 0),
        BackColor = Palette.WindowBackground,
    };
    private readonly CheckBox _askForAdmin = new()
    {
        Text = "Ask for administrator permission each time",
        AutoSize = true,
        ForeColor = Palette.PrimaryText,
        Margin = new Padding(0, S(10), 0, 0),
        Cursor = Cursors.Hand,
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
        Font = new Font("Segoe UI Semibold", 10.5f),
        Height = S(40),
        Margin = new Padding(0, S(16), 0, 0),
        Cursor = Cursors.Hand,
    };
    private readonly Label _freeStatus = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Palette.SecondaryText,
        Font = new Font("Segoe UI", 9.5f),
        Height = S(32),
        Margin = new Padding(0, S(6), 0, 0),
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
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(S(28), S(16), S(28), S(12));

        _thresholds.SetThresholds(_settings.PantingPercent, _settings.GaspingPercent);
        _askForAdmin.Checked = _settings.AskForAdmin;

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(170)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(170)));
        layout.Controls.Add(_memoryValue, 0, 0);
        layout.Controls.Add(_cpuValue, 1, 0);
        layout.Controls.Add(MakeCaptionLabel("Memory"), 0, 1);
        layout.Controls.Add(MakeCaptionLabel("CPU"), 1, 1);
        layout.Controls.Add(_thresholds, 0, 2);
        layout.SetColumnSpan(_thresholds, 2);
        layout.Controls.Add(_askForAdmin, 0, 3);
        layout.SetColumnSpan(_askForAdmin, 2);
        layout.Controls.Add(_freeButton, 0, 4);
        layout.SetColumnSpan(_freeButton, 2);
        layout.Controls.Add(_freeStatus, 0, 5);
        layout.SetColumnSpan(_freeStatus, 2);
        Controls.Add(layout);

        // Size the window before minimizing it; a size set while minimized is lost.
        var content = layout.GetPreferredSize(Size.Empty);
        ClientSize = new Size(content.Width + Padding.Horizontal, content.Height + Padding.Vertical);
        WindowState = FormWindowState.Minimized;

        _thresholds.ThresholdsChanged += (_, _) =>
        {
            _settings.PantingPercent = _thresholds.Panting;
            _settings.GaspingPercent = _thresholds.Gasping;
            ShowReadings();
        };
        _thresholds.ThresholdsCommitted += (_, _) => _settings.Save();
        _askForAdmin.CheckedChanged += async (_, _) =>
        {
            if (!_revertingCheckBox)
                await SetAskForAdminAsync(_askForAdmin.Checked);
        };
        _freeButton.Click += async (_, _) => await FreeMemoryAsync();
        _timer.Tick += (_, _) => TakeReadings();
    }

    private bool IsGasping => _memoryPercent >= _settings.GaspingPercent;
    private bool IsPanting => _memoryPercent >= _settings.PantingPercent;

    private static Label MakeValueLabel() => new()
    {
        Dock = DockStyle.Fill,
        Height = S(64),
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI Semibold", 28f),
        ForeColor = Palette.PrimaryText,
        Margin = Padding.Empty,
    };

    private static Label MakeCaptionLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Height = S(28),
        TextAlign = ContentAlignment.TopCenter,
        ForeColor = Palette.SecondaryText,
        Font = new Font("Segoe UI", 10f),
        Margin = Padding.Empty,
    };

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_RESTORE = 0xF120;

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
        _freeStatus.Text = "Freeing memory…";
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
        _askForAdmin.Enabled = false;
        try
        {
            string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            int exitCode = await RunAsAdminAsync(ask ? "--unregister-task" : $"--register-task \"{user}\"");
            if (exitCode != 0)
                throw new InvalidOperationException(Marshal.GetExceptionForHR(exitCode)?.Message ?? $"Error {exitCode}");

            _settings.AskForAdmin = ask;
            _settings.Save();
            _freeStatus.Text = ask ? "Freeing will ask for permission each time" : "Freeing will no longer ask for permission";
        }
        catch (Exception ex)
        {
            _freeStatus.Text = ex is Win32Exception { NativeErrorCode: ERROR_CANCELLED }
                ? "Cancelled"
                : $"Could not change this: {ex.Message}";
            _revertingCheckBox = true;
            _askForAdmin.Checked = !ask;
            _revertingCheckBox = false;
        }
        finally
        {
            _askForAdmin.Enabled = true;
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
            : $"CPU {_cpuPercent}%";
        _memoryValue.Text = $"{_memoryPercent}%";
        _memoryValue.ForeColor = IsGasping ? Palette.Gasping
            : IsPanting ? Palette.PantingText
            : Palette.PrimaryText;
        _cpuValue.Text = $"{_cpuPercent}%";
        SetIcon();
    }

    private void SetIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var textColor = Color.White;
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
