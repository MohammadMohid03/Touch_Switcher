using System.Drawing;
using System.Windows.Forms;
using TouchSwitcher.Configuration;
using TouchSwitcher.Gesture;
using TouchSwitcher.Interop;
using TouchSwitcher.WindowManagement;

namespace TouchSwitcher.UI;

internal sealed class TrayIconManager : IDisposable
{
    private readonly SettingsManager _settings;
    private readonly RawInputReceiver _receiver;
    private readonly GestureRecognizer _recognizer;
    private readonly SwitcherService _switcher;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;

    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _appModeItem;
    private readonly ToolStripMenuItem _windowModeItem;
    private readonly ToolStripMenuItem _startupItem;
    private bool _syncing;

    public TrayIconManager(SettingsManager settings, RawInputReceiver receiver, GestureRecognizer recognizer, SwitcherService switcher)
    {
        _settings = settings;
        _receiver = receiver;
        _recognizer = recognizer;
        _switcher = switcher;

        _icon = CreateGoldIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "Touch Switcher",
            Visible = true
        };

        _enabledItem = new ToolStripMenuItem("Enabled") { CheckOnClick = true, Checked = settings.Current.Enabled };
        _enabledItem.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            var s = CloneSettings(settings.Current);
            s.Enabled = _enabledItem.Checked;
            settings.Save(s);
        };

        _appModeItem = new ToolStripMenuItem("Application") { CheckOnClick = true };
        _windowModeItem = new ToolStripMenuItem("Window") { CheckOnClick = true };
        _appModeItem.Click += (_, _) => SetMode(SwitchMode.Application);
        _windowModeItem.Click += (_, _) => SetMode(SwitchMode.Window);

        var modeMenu = new ToolStripMenuItem("Switch Mode");
        modeMenu.DropDownItems.AddRange(new ToolStripItem[] { _appModeItem, _windowModeItem });

        _startupItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = settings.Current.StartWithWindows };
        _startupItem.CheckedChanged += (_, _) =>
        {
            if (_syncing) return;
            var s = CloneSettings(settings.Current);
            s.StartWithWindows = _startupItem.Checked;
            settings.Save(s);
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripLabel("Touch Switcher") { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enabledItem);
        menu.Items.Add(modeMenu);
        menu.Items.Add(new ToolStripMenuItem("Gesture Settings...", null, (_, _) => OpenSettings()));
        menu.Items.Add(new ToolStripMenuItem("Diagnostics...", null, (_, _) => OpenDiagnostics()));
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("About...", null, (_, _) => OpenAbout()));
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Application.Exit()));

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => OpenDiagnostics();

        SyncModeChecks();
        settings.Changed += _ =>
        {
            _syncing = true;
            try
            {
                _enabledItem.Checked = settings.Current.Enabled;
                _startupItem.Checked = settings.Current.StartWithWindows;
                SyncModeChecks();
            }
            finally
            {
                _syncing = false;
            }
        };

        _notifyIcon.ShowBalloonTip(
            3000,
            "Touch Switcher",
            "Active! 3-finger swipe left/right to switch apps.",
            ToolTipIcon.Info);
    }

    public void OpenDiagnostics()
    {
        Program.Log("TrayIconManager: OpenDiagnostics called");
        try
        {
            foreach (Form f in Application.OpenForms)
            {
                if (f is DiagnosticForm existing && !existing.IsDisposed)
                {
                    Program.Log($"TrayIconManager: Existing DiagnosticForm found (HWND=0x{existing.Handle:X})");
                    if (existing.WindowState == FormWindowState.Minimized)
                    {
                        existing.WindowState = FormWindowState.Normal;
                    }
                    existing.TopMost = true;
                    existing.Show();
                    existing.BringToFront();
                    existing.Activate();
                    ForceForeground(existing.Handle);
                    return;
                }
            }

            Program.Log("TrayIconManager: Instantiating new DiagnosticForm");
            var form = new DiagnosticForm(_settings, _receiver, _recognizer, _switcher)
            {
                TopMost = true
            };
            form.Show();
            form.BringToFront();
            form.Activate();
            ForceForeground(form.Handle);
            Program.Log($"TrayIconManager: DiagnosticForm displayed (HWND=0x{form.Handle:X})");
        }
        catch (Exception ex)
        {
            Program.Log($"TrayIconManager: OpenDiagnostics error: {ex}");
        }
    }

    private static void ForceForeground(IntPtr hWnd)
    {
        try
        {
            IntPtr fgWnd = NativeMethods.GetForegroundWindow();
            uint fgThread = NativeMethods.GetWindowThreadProcessId(fgWnd, out _);
            uint curThread = NativeMethods.GetCurrentThreadId();
            if (fgWnd != hWnd && fgThread != curThread && fgThread != 0)
            {
                NativeMethods.AttachThreadInput(curThread, fgThread, true);
                NativeMethods.SetForegroundWindow(hWnd);
                NativeMethods.BringWindowToTop(hWnd);
                NativeMethods.AttachThreadInput(curThread, fgThread, false);
            }
            else
            {
                NativeMethods.SetForegroundWindow(hWnd);
                NativeMethods.BringWindowToTop(hWnd);
            }
        }
        catch { }
    }

    public void OpenSettings()
    {
        foreach (Form f in Application.OpenForms)
        {
            if (f is SettingsForm existing)
            {
                existing.Activate();
                return;
            }
        }

        new SettingsForm(_settings).Show();
    }

    public void OpenAbout()
    {
        foreach (Form f in Application.OpenForms)
        {
            if (f is AboutForm existing)
            {
                existing.Activate();
                return;
            }
        }

        new AboutForm().Show();
    }

    private void SetMode(SwitchMode mode)
    {
        var s = CloneSettings(_settings.Current);
        s.SwitchMode = mode;
        _settings.Save(s);
        SyncModeChecks();
    }

    private void SyncModeChecks()
    {
        _appModeItem.Checked = _settings.Current.SwitchMode == SwitchMode.Application;
        _windowModeItem.Checked = _settings.Current.SwitchMode == SwitchMode.Window;
    }

    private static AppSettings CloneSettings(AppSettings s) => new()
    {
        Enabled = s.Enabled,
        SwitchMode = s.SwitchMode,
        MinSwipeDistance = s.MinSwipeDistance,
        HorizontalVerticalRatio = s.HorizontalVerticalRatio,
        GestureTimeoutMs = s.GestureTimeoutMs,
        CooldownMs = s.CooldownMs,
        RequiredFingers = s.RequiredFingers,
        Sensitivity = s.Sensitivity,
        StartWithWindows = s.StartWithWindows,
        DisableInFullscreen = s.DisableInFullscreen,
        ExcludedApplications = s.ExcludedApplications.ToList()
    };

    private static Icon CreateGoldIcon()
    {
        try
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                using var fill = new SolidBrush(Color.FromArgb(212, 160, 23));
                using var ink = new SolidBrush(Color.FromArgb(28, 28, 28));
                g.FillRectangle(fill, 2, 2, 28, 28);
                g.FillRectangle(ink, 8, 14, 4, 10);
                g.FillRectangle(ink, 14, 10, 4, 14);
                g.FillRectangle(ink, 20, 14, 4, 10);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
