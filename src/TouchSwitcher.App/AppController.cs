using TouchSwitcher.Configuration;
using TouchSwitcher.Gesture;
using TouchSwitcher.UI;
using TouchSwitcher.WindowManagement;

namespace TouchSwitcher;

internal sealed class AppController : IDisposable
{
    private readonly Control _invoker;
    private readonly SettingsManager _settings;
    private readonly SwitcherService _switcher;
    private readonly RawInputReceiver _receiver;
    private readonly GestureRecognizer _recognizer;
    private readonly TrayIconManager _tray;

    public TrayIconManager Tray => _tray;

    public AppController()
    {
        _invoker = new Control();
        _ = _invoker.Handle; // Guarantee persistent HWND on the UI thread for Dispatch

        Program.Log("AppController: Initializing Settings...");
        _settings = new SettingsManager();

        if (_settings.Current.StartWithWindows != StartupManager.IsEnabled())
        {
            StartupManager.Apply(_settings.Current.StartWithWindows);
        }

        Program.Log("AppController: Initializing SwitcherService...");
        _switcher = new SwitcherService(_settings);

        Program.Log("AppController: Initializing GestureRecognizer...");
        _recognizer = new GestureRecognizer();
        _recognizer.ThreeFingersDown += () =>
        {
            Program.Log("GESTURE: 3 FINGERS DOWN (IDLE PREVIEW)");
            _switcher.ShowPreview();
        };
        _recognizer.FingersLifted += () =>
        {
            Program.Log("GESTURE: FINGERS LIFTED (DISMISS PREVIEW)");
            _switcher.DismissPreview();
        };
        _recognizer.SwipeRecognized += direction =>
        {
            Program.Log($"GESTURE: SWIPE RECOGNIZED: {direction}");
            _switcher.HandleSwipe(direction);
        };

        Program.Log("AppController: Initializing RawInputReceiver...");
        _receiver = new RawInputReceiver();
        _receiver.Coalescer.FrameReady += frame =>
        {
            _recognizer.Process(frame, _settings.Current);
        };

        Program.Log("AppController: Initializing TrayIconManager...");
        _tray = new TrayIconManager(_settings, _receiver, _recognizer, _switcher);
        _receiver.ShowDiagnosticsRequested += () => _tray.OpenDiagnostics();

        Program.Log("AppController: Ready!");
    }

    public void Dispatch(Action action)
    {
        if (_invoker.IsDisposed) return;
        try
        {
            if (_invoker.InvokeRequired)
            {
                _invoker.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (Exception ex)
        {
            Program.Log($"AppController.Dispatch error: {ex}");
        }
    }

    public void Dispose()
    {
        _tray.Dispose();
        _receiver.Dispose();
        _switcher.Dispose();
        _invoker.Dispose();
    }
}
