using System.Runtime.InteropServices;
using System.Text;
using TouchSwitcher.Interop;

namespace TouchSwitcher.WindowManagement;

internal sealed record SwitchableWindow(
    IntPtr Handle,
    uint ProcessId,
    string ProcessName,
    string Title,
    string ClassName);

internal static class WindowEnumerator
{
    private static readonly HashSet<string> BlockedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Progman",
        "WorkerW",
        "Windows.UI.Core.CoreWindow",
        "ApplicationManager_ImmersiveShellWindow",
        "MultitaskingViewFrame",
        "ForegroundStaging",
        "NotifyIconOverflowWindow",
        "TaskListOverlayWnd",
        "TaskListThumbnailWnd"
    };

    private static readonly HashSet<string> BlockedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "searchhost.exe",
        "startmenuexperiencehost.exe",
        "shellexperiencehost.exe",
        "textinputhost.exe",
        "lockapp.exe",
        "dwm.exe",
        "csrss.exe",
        "winlogon.exe",
        "sihost.exe"
    };

    public static IReadOnlyList<SwitchableWindow> Enumerate(IEnumerable<string> excludedExecutables)
    {
        var excluded = new HashSet<string>(
            excludedExecutables.Select(SettingsNormalize),
            StringComparer.OrdinalIgnoreCase);
        string self = SettingsNormalize(System.IO.Path.GetFileName(Environment.ProcessPath ?? "TouchSwitcher.exe"));
        excluded.Add(self);

        var results = new List<SwitchableWindow>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsSwitchable(hwnd))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            string processName = GetProcessName(pid);
            if (processName.Length == 0 || excluded.Contains(processName) || BlockedProcessNames.Contains(processName))
            {
                return true;
            }

            results.Add(new SwitchableWindow(
                hwnd,
                pid,
                processName,
                NativeMethods.GetWindowTitle(hwnd),
                NativeMethods.GetWindowClass(hwnd)));
            return true;
        }, IntPtr.Zero);

        return results;
    }

    public static SwitchableWindow? FromHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return new SwitchableWindow(
            hwnd,
            pid,
            GetProcessName(pid),
            NativeMethods.GetWindowTitle(hwnd),
            NativeMethods.GetWindowClass(hwnd));
    }

    public static bool IsForegroundFullscreen()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out RECT window))
        {
            return false;
        }

        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        RECT screen = info.rcMonitor;
        bool coversExact =
            Math.Abs(window.Left - screen.Left) <= 1 &&
            Math.Abs(window.Top - screen.Top) <= 1 &&
            Math.Abs(window.Right - screen.Right) <= 1 &&
            Math.Abs(window.Bottom - screen.Bottom) <= 1;

        if (!coversExact)
        {
            return false;
        }

        nint style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
        nint ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        string className = NativeMethods.GetWindowClass(hwnd);
        if (className is "WorkerW" or "Progman" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "MultitaskingViewFrame" or "Windows.UI.Core.CoreWindow")
        {
            return false;
        }

        // Standard maximized windows have WS_CAPTION (0x00C00000). Exclusive fullscreen games do not.
        bool hasCaption = (style & 0x00C00000) == 0x00C00000;
        if (hasCaption)
        {
            return false;
        }

        return (ex & NativeMethods.WS_EX_TOPMOST) != 0;
    }

    private static bool IsSwitchable(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd))
        {
            return false;
        }

        if (IsCloaked(hwnd))
        {
            return false;
        }

        nint ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((ex & NativeMethods.WS_EX_NOACTIVATE) != 0)
        {
            return false;
        }

        bool tool = (ex & NativeMethods.WS_EX_TOOLWINDOW) != 0;
        bool app = (ex & NativeMethods.WS_EX_APPWINDOW) != 0;
        if (tool && !app)
        {
            return false;
        }

        string className = NativeMethods.GetWindowClass(hwnd);
        if (BlockedClasses.Contains(className))
        {
            return false;
        }

        IntPtr owner = NativeMethods.GetWindow(hwnd, 4); // GW_OWNER
        if (owner != IntPtr.Zero && !app)
        {
            return false;
        }

        IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOTOWNER);
        if (root != hwnd)
        {
            IntPtr last = NativeMethods.GetLastActivePopup(root);
            if (last != hwnd)
            {
                return false;
            }
        }

        string title = NativeMethods.GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title) && !app)
        {
            return false;
        }

        return true;
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        try
        {
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0)
            {
                return cloaked != 0;
            }
        }
        catch
        {
            // Older systems without DWM attribute support.
        }

        return false;
    }

    public static string GetProcessName(uint processId)
    {
        IntPtr handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                return SettingsNormalize(process.ProcessName + ".exe");
            }
            catch
            {
                return string.Empty;
            }
        }

        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            if (!NativeMethods.QueryFullProcessImageName(handle, 0, sb, ref size))
            {
                return string.Empty;
            }

            return SettingsNormalize(System.IO.Path.GetFileName(sb.ToString()));
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static string SettingsNormalize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        string file = System.IO.Path.GetFileName(name);
        return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file : file + ".exe";
    }
}
