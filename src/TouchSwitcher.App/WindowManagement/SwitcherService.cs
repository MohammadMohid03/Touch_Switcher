using System.Drawing;
using System.Windows.Forms;
using TouchSwitcher.Configuration;
using TouchSwitcher.Gesture;
using TouchSwitcher.Interop;
using TouchSwitcher.UI;

namespace TouchSwitcher.WindowManagement;

internal sealed class SwitcherService : IDisposable
{
    private readonly SettingsManager _settings;
    private readonly WindowDoublyQueue _doublyQueue = new();
    private readonly SlideOverlayForm _slideOverlay = new();
    private int _busy;
    private bool _taskViewActive;

    public WindowDoublyQueue DoublyQueue => _doublyQueue;

    public SwitcherService(SettingsManager settings)
    {
        _settings = settings;
    }

    public void ShowPreview()
    {
        try
        {
            if (_taskViewActive)
            {
                return;
            }

            AppSettings settings = _settings.Current;
            if (!settings.Enabled || !settings.EnableSlideAnimation)
            {
                return;
            }

            if (settings.DisableInFullscreen && WindowEnumerator.IsForegroundFullscreen())
            {
                Program.Log("ShowPreview: Suppressed because foreground window is exclusive fullscreen.");
                return;
            }

            IReadOnlyList<SwitchableWindow> windows = WindowEnumerator.Enumerate(settings.ExcludedApplications);
            if (windows.Count == 0)
            {
                return;
            }

            Rectangle screenBounds = Screen.PrimaryScreen?.Bounds ?? Screen.FromPoint(Cursor.Position).Bounds;
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                screenBounds = Screen.FromHandle(fg).Bounds;
            }

            _slideOverlay.ShowPreview(screenBounds, _doublyQueue, windows, settings.SwitchMode);
        }
        catch (Exception ex)
        {
            Program.Log($"ShowPreview error: {ex}");
        }
    }

    public void DismissPreview()
    {
        try
        {
            _slideOverlay.Dismiss();
        }
        catch (Exception ex)
        {
            Program.Log($"DismissPreview error: {ex}");
        }
    }

    public void HandleSwipe(SwipeDirection direction)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return;
        }

        try
        {
            AppSettings settings = _settings.Current;
            if (!settings.Enabled)
            {
                return;
            }

            // 1. Swipe Up -> Open Windows Task View (Desktops + All Apps + New Desktop option)
            if (direction == SwipeDirection.Up)
            {
                _slideOverlay.Dismiss();
                Program.Log("HandleSwipe: [Up] -> Opening Windows Task View (Win + Tab)");
                SendWinTab();
                _taskViewActive = true;
                return;
            }

            // 2. Swipe Down -> Dismiss Task View / Return to workspace
            if (direction == SwipeDirection.Down)
            {
                _slideOverlay.Dismiss();
                Program.Log("HandleSwipe: [Down] -> Dismissing Task View");
                SendEscape();
                _taskViewActive = false;
                return;
            }

            // Horizontal swipes exit Task View tracking
            _taskViewActive = false;

            // Only suppress horizontal app switching if in exclusive fullscreen
            if (settings.DisableInFullscreen && WindowEnumerator.IsForegroundFullscreen())
            {
                Program.Log("HandleSwipe: Suppressed horizontal switch because foreground window is exclusive fullscreen.");
                return;
            }

            // 3. Normal horizontal switching between apps in circular queue
            IReadOnlyList<SwitchableWindow> windows = WindowEnumerator.Enumerate(settings.ExcludedApplications);
            if (windows.Count == 0)
            {
                Program.Log("HandleSwipe: No switchable windows found.");
                return;
            }

            SwitchableWindow? target = direction == SwipeDirection.Right
                ? _doublyQueue.MoveNext(windows, settings.SwitchMode)
                : _doublyQueue.MovePrevious(windows, settings.SwitchMode);

            if (target == null)
            {
                Program.Log("HandleSwipe: No target window returned from doubly queue.");
                return;
            }

            Program.Log($"HandleSwipe: [{direction}] -> Activating {target.ProcessName} (HWND 0x{target.Handle:X}) - '{target.Title}'");

            // Activate target window immediately
            bool ok = WindowActivator.Activate(target.Handle);
            Program.Log($"HandleSwipe: Activation result = {ok}");

            // Trigger sleek Mac motion animation if enabled
            if (settings.EnableSlideAnimation)
            {
                Rectangle screenBounds = Screen.PrimaryScreen?.Bounds ?? Screen.FromPoint(Cursor.Position).Bounds;
                IntPtr fg = NativeMethods.GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    screenBounds = Screen.FromHandle(fg).Bounds;
                }
                _slideOverlay.TriggerTransition(screenBounds, direction, _doublyQueue, windows, settings.SwitchMode);
            }
        }
        catch (Exception ex)
        {
            Program.Log($"HandleSwipe error: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static void SendWinTab()
    {
        NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_TAB, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_TAB, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static void SendEscape()
    {
        NativeMethods.keybd_event(NativeMethods.VK_ESCAPE, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_ESCAPE, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public void Dispose()
    {
        _slideOverlay.Dispose();
    }
}
