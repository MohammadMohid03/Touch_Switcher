using System.Drawing;
using TouchSwitcher.Interop;

namespace TouchSwitcher.UI;

internal static class IconHelper
{
    private static readonly Dictionary<string, Image> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    public static Image? GetAppIcon(IntPtr hwnd, string processName)
    {
        if (string.IsNullOrEmpty(processName))
        {
            return null;
        }

        lock (_iconCache)
        {
            if (_iconCache.TryGetValue(processName, out var cached))
            {
                return cached;
            }
        }

        Image? iconImage = null;

        // 1. Try WM_GETICON with timeout (ICON_BIG, then ICON_SMALL)
        try
        {
            if (NativeMethods.SendMessageTimeout(
                    hwnd,
                    NativeMethods.WM_GETICON,
                    (IntPtr)NativeMethods.ICON_BIG,
                    IntPtr.Zero,
                    NativeMethods.SMTO_ABORTIFHUNG | NativeMethods.SMTO_BLOCK,
                    30,
                    out IntPtr hIcon) != IntPtr.Zero && hIcon != IntPtr.Zero)
            {
                using var ico = Icon.FromHandle(hIcon);
                iconImage = ico.ToBitmap();
            }

            if (iconImage == null && NativeMethods.SendMessageTimeout(
                    hwnd,
                    NativeMethods.WM_GETICON,
                    (IntPtr)NativeMethods.ICON_SMALL,
                    IntPtr.Zero,
                    NativeMethods.SMTO_ABORTIFHUNG | NativeMethods.SMTO_BLOCK,
                    30,
                    out hIcon) != IntPtr.Zero && hIcon != IntPtr.Zero)
            {
                using var ico = Icon.FromHandle(hIcon);
                iconImage = ico.ToBitmap();
            }

            if (iconImage == null)
            {
                hIcon = (IntPtr)NativeMethods.GetClassLongPtr(hwnd, NativeMethods.GCLP_HICON);
                if (hIcon != IntPtr.Zero)
                {
                    using var ico = Icon.FromHandle(hIcon);
                    iconImage = ico.ToBitmap();
                }
            }
        }
        catch
        {
            // Ignore GDI/window read errors
        }

        // 2. Fallback to process executable associated icon
        if (iconImage == null)
        {
            try
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != 0)
                {
                    using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                    string? exePath = proc.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                    {
                        using var ico = Icon.ExtractAssociatedIcon(exePath);
                        if (ico != null)
                        {
                            iconImage = ico.ToBitmap();
                        }
                    }
                }
            }
            catch
            {
                // Process may be protected or elevated
            }
        }

        if (iconImage != null)
        {
            lock (_iconCache)
            {
                _iconCache[processName] = iconImage;
            }
        }

        return iconImage;
    }

    private static readonly Dictionary<string, string> _friendlyNameCache = new(StringComparer.OrdinalIgnoreCase);

    public static string GetFriendlyName(string processName, string windowTitle)
    {
        if (string.IsNullOrEmpty(processName))
        {
            return "Application";
        }

        string clean = processName;
        if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(0, clean.Length - 4);
        }

        lock (_friendlyNameCache)
        {
            if (_friendlyNameCache.TryGetValue(clean, out var cached))
            {
                return cached;
            }
        }

        string result = ResolveFriendlyName(clean, windowTitle);
        lock (_friendlyNameCache)
        {
            _friendlyNameCache[clean] = result;
        }

        return result;
    }

    private static string ResolveFriendlyName(string clean, string windowTitle)
    {
        switch (clean.ToLowerInvariant())
        {
            case "chrome": return "Google Chrome";
            case "msedge": return "Microsoft Edge";
            case "code": return "VS Code";
            case "devenv": return "Visual Studio";
            case "explorer": return "File Explorer";
            case "windowsterminal": return "Terminal";
            case "powershell": return "PowerShell";
            case "cmd": return "Command Prompt";
            case "notepad": return "Notepad";
            case "spotify": return "Spotify";
            case "discord": return "Discord";
            case "slack": return "Slack";
            case "teams": return "Microsoft Teams";
            case "firefox": return "Firefox";
            case "brave": return "Brave";
            case "steam": return "Steam";
            case "taskmgr": return "Task Manager";
            case "zen": return "Zen Browser";
            case "rider64": return "Rider";
            case "idea64": return "IntelliJ IDEA";
            case "clion64": return "CLion";
            case "pycharm64": return "PyCharm";
            case "webstorm64": return "WebStorm";
            case "datagrip64": return "DataGrip";
            case "telegram": return "Telegram";
            case "whatsapp": return "WhatsApp";
            case "signal": return "Signal";
            case "vlc": return "VLC";
            case "winword": return "Word";
            case "excel": return "Excel";
            case "powerpnt": return "PowerPoint";
            case "onenote": return "OneNote";
            case "outlook": return "Outlook";
            case "figma": return "Figma";
            case "obs64": return "OBS Studio";
            case "calculatorapp":
            case "calculator": return "Calculator";
            case "snippingtool": return "Snipping Tool";
            case "systemsettings": return "Settings";
        }

        // Check if window title has a clear app name suffix (e.g. "Doc - Word" -> "Word")
        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            int dash = windowTitle.LastIndexOf(" - ", StringComparison.Ordinal);
            if (dash >= 0 && dash + 3 < windowTitle.Length)
            {
                string suffix = windowTitle.Substring(dash + 3).Trim();
                if (suffix.Length <= 18 && !suffix.Contains('\\') && !suffix.Contains('/'))
                {
                    return suffix;
                }
            }
        }

        if (clean.Length > 0)
        {
            return char.ToUpperInvariant(clean[0]) + clean.Substring(1);
        }

        return clean;
    }
}
