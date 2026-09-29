using TouchSwitcher.Interop;

namespace TouchSwitcher.WindowManagement;

internal static class WindowActivator
{
    public static bool Activate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return false;
        }

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == hwnd)
        {
            return true;
        }

        // Pulse Alt key upfront to satisfy Windows foreground lock
        PulseAlt();

        // If iconic (minimized), restore it
        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }
        else
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
        }

        uint targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        uint foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint currentThread = NativeMethods.GetCurrentThreadId();

        bool attachedFg = false;
        bool attachedTarget = false;

        try
        {
            if (currentThread != foregroundThread && foregroundThread != 0)
            {
                attachedFg = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
            }

            if (currentThread != targetThread && targetThread != 0)
            {
                attachedTarget = NativeMethods.AttachThreadInput(currentThread, targetThread, true);
            }

            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetWindowPos(
                hwnd,
                new IntPtr(NativeMethods.HWND_TOP),
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

            bool ok = NativeMethods.SetForegroundWindow(hwnd);
            if (!ok || NativeMethods.GetForegroundWindow() != hwnd)
            {
                PulseAlt();
                NativeMethods.SetForegroundWindow(hwnd);
                NativeMethods.SwitchToThisWindow(hwnd, true);
            }

            return NativeMethods.GetForegroundWindow() == hwnd;
        }
        finally
        {
            if (attachedTarget)
            {
                NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            }

            if (attachedFg)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private static void PulseAlt()
    {
        // keybd_event simulates Alt key press and release without Tab, satisfying Windows foreground lock
        NativeMethods.keybd_event((byte)NativeMethods.VK_MENU, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event((byte)NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
