using System.Runtime.InteropServices;
using System.Windows.Forms;
using TouchSwitcher.Interop;

namespace TouchSwitcher.Gesture;

internal sealed class RawInputReceiver : NativeWindow, IDisposable
{
    private readonly HidTouchpadParser _parser = new();
    private readonly TouchFrameCoalescer _coalescer = new();
    private readonly uint _showDiagnosticsMsg = NativeMethods.RegisterWindowMessage("TouchSwitcher_ShowDiagnostics");
    private readonly bool _registered;

    public TouchFrameCoalescer Coalescer => _coalescer;
    public bool IsRegistered => _registered;
    public int RawInputMessageCount { get; private set; }

    public event Action<int>? InputReceived;
    public event Action? ShowDiagnosticsRequested;

    public RawInputReceiver()
    {
        // Top-level popup window with WS_EX_TOOLWINDOW off-screen.
        // Must be a top-level window (not HWND_MESSAGE) for RIDEV_INPUTSINK.
        // It is never shown, hidden, or minimized, so the OS never recreates its handle.
        var cp = new CreateParams
        {
            Caption = "TouchSwitcher.RawInputSink",
            Style = unchecked((int)0x80000000), // WS_POPUP
            ExStyle = 0x00000080,               // WS_EX_TOOLWINDOW
            X = -32000,
            Y = -32000,
            Width = 0,
            Height = 0
        };

        CreateHandle(cp);
        Program.Log($"RawInputReceiver: HWND=0x{Handle:X}");

        _registered = Register();
        Program.Log($"RawInputReceiver: Registered={_registered}");
    }

    private bool Register()
    {
        var devices = new[]
        {
            new RAWINPUTDEVICE
            {
                usUsagePage = 0x000D, // Digitizers
                usUsage = 0x0005,     // Precision Touch Pad
                dwFlags = NativeMethods.RIDEV_INPUTSINK,
                hwndTarget = Handle
            }
        };

        return NativeMethods.RegisterRawInputDevices(
            devices,
            (uint)devices.Length,
            (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == (int)_showDiagnosticsMsg)
        {
            Program.Log("RawInputReceiver: Received WM_SHOW_DIAGNOSTICS broadcast");
            ShowDiagnosticsRequested?.Invoke();
            return;
        }

        if (m.Msg == NativeMethods.WM_INPUT)
        {
            RawInputMessageCount++;
            InputReceived?.Invoke(RawInputMessageCount);

            try
            {
                TouchFrame? report = _parser.Parse(m.LParam);
                if (report != null)
                {
                    _coalescer.ProcessRawReport(report);
                }
            }
            catch (Exception ex)
            {
                Program.Log($"RawInput error: {ex.Message}");
            }
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        _coalescer.Dispose();
        _parser.Dispose();
        DestroyHandle();
    }
}
