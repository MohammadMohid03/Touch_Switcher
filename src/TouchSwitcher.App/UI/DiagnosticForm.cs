using System.Drawing;
using System.Windows.Forms;
using TouchSwitcher.Configuration;
using TouchSwitcher.Gesture;
using TouchSwitcher.WindowManagement;

namespace TouchSwitcher.UI;

internal sealed class DiagnosticForm : Form
{
    private readonly SettingsManager _settings;
    private readonly RawInputReceiver _receiver;
    private readonly GestureRecognizer _recognizer;
    private readonly SwitcherService _switcher;

    private readonly Label _statusLabel;
    private readonly Label _inputCountLabel;
    private readonly Label _fingerLabel;
    private readonly Label _moveLabel;
    private readonly Label _threshLabel;
    private readonly Label _swipeLabel;
    private readonly Label _queueLabel;
    private int _swipeCount;

    public DiagnosticForm(SettingsManager settings, RawInputReceiver receiver, GestureRecognizer recognizer, SwitcherService switcher)
    {
        _settings = settings;
        _receiver = receiver;
        _recognizer = recognizer;
        _switcher = switcher;

        Text = "Touch Switcher - Diagnostics";
        ClientSize = new Size(520, 380);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ShowInTaskbar = true;
        BackColor = Color.FromArgb(28, 28, 28);
        ForeColor = Color.FromArgb(242, 242, 242);
        Font = new Font("Segoe UI", 10);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        Program.Log("DiagnosticForm: Initializing controls...");

        _statusLabel = new Label { Location = new Point(20, 15), AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
        _inputCountLabel = new Label { Location = new Point(20, 48), AutoSize = true, Text = $"WM_INPUT Messages: {_receiver.RawInputMessageCount}" };
        _fingerLabel = new Label { Location = new Point(20, 75), AutoSize = true, Text = "Fingers on Touchpad: 0", Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Color.LightSkyBlue };
        _moveLabel = new Label { Location = new Point(20, 105), AutoSize = true, Text = "Live Displacement: dx=0  dy=0", ForeColor = Color.Cyan };
        _threshLabel = new Label { Location = new Point(20, 130), AutoSize = true, ForeColor = Color.DarkGray };
        _swipeLabel = new Label { Location = new Point(20, 158), AutoSize = true, ForeColor = Color.LimeGreen, Font = new Font("Segoe UI", 13, FontStyle.Bold), Text = "Last Swipe: (none)" };
        _queueLabel = new Label { Location = new Point(20, 195), Size = new Size(470, 45), ForeColor = Color.Goldenrod, Font = new Font("Segoe UI", 9, FontStyle.Bold), Text = "Doubly Queue: (initializing...)" };

        var helpLabel = new Label
        {
            Location = new Point(20, 248),
            Size = new Size(470, 65),
            ForeColor = Color.Gray,
            Text = "3-Finger Gestures:\n• Swipe Left / Right: Sequential app switch (circular queue)\n• Swipe Up: Task View (Desktops + All Apps + New Desktop)\n• Swipe Down: Dismiss Task View / Return to workspace\n• Rest 3 fingers: Idle HUD preview bar"
        };

        var hideBtn = new Button
        {
            Text = "Hide to Tray",
            Location = new Point(20, 325),
            Size = new Size(110, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(51, 51, 51),
            ForeColor = ForeColor
        };
        hideBtn.Click += (_, _) => Close();

        var settingsBtn = new Button
        {
            Text = "Settings...",
            Location = new Point(145, 325),
            Size = new Size(100, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(51, 51, 51),
            ForeColor = ForeColor
        };
        settingsBtn.Click += (_, _) => new SettingsForm(_settings).Show();

        var exitBtn = new Button
        {
            Text = "Exit App",
            Location = new Point(390, 325),
            Size = new Size(95, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(80, 30, 30),
            ForeColor = ForeColor
        };
        exitBtn.Click += (_, _) => Application.Exit();

        Controls.AddRange(new Control[] { _statusLabel, _inputCountLabel, _fingerLabel, _moveLabel, _threshLabel, _swipeLabel, _queueLabel, helpLabel, hideBtn, settingsBtn, exitBtn });

        UpdateStatus();
        UpdateThreshold();
        UpdateQueueDisplay();

        // Subscribe to events
        _receiver.InputReceived += OnInputReceived;
        _receiver.Coalescer.FrameReady += OnFrameReady;
        _recognizer.MovementUpdated += OnMovementUpdated;
        _recognizer.SwipeRecognized += OnSwipeRecognized;

        FormClosed += (_, _) =>
        {
            _receiver.InputReceived -= OnInputReceived;
            _receiver.Coalescer.FrameReady -= OnFrameReady;
            _recognizer.MovementUpdated -= OnMovementUpdated;
            _recognizer.SwipeRecognized -= OnSwipeRecognized;
        };
    }

    private void UpdateStatus()
    {
        bool pExists = HidTouchpadParser.PrecisionTouchpadExists();
        bool isReg = _receiver.IsRegistered;
        _statusLabel.Text = $"Touchpad: {(pExists ? "YES" : "NO")}  |  Raw Input: {(isReg ? "ACTIVE" : "ERROR")}";
        _statusLabel.ForeColor = (pExists && isReg) ? Color.LimeGreen : Color.Red;
    }

    private void UpdateThreshold()
    {
        double t = GestureRecognizer.GetEffectiveDistance(_settings.Current);
        _threshLabel.Text = $"Swipe threshold: {t:F0} units (MinDist={_settings.Current.MinSwipeDistance}, Sensitivity={_settings.Current.Sensitivity})";
    }

    private void UpdateQueueDisplay()
    {
        var keys = _switcher.DoublyQueue.CurrentQueueKeys;
        string current = _switcher.DoublyQueue.CurrentKey ?? "(none)";
        if (keys.Count == 0)
        {
            _queueLabel.Text = "Doubly Queue: (empty)";
        }
        else
        {
            string line = string.Join(" <-> ", keys.Select(k => k == current ? $"[{k}]" : k));
            _queueLabel.Text = $"Doubly Queue: {line}";
        }
    }

    private void OnInputReceived(int count)
    {
        if (IsDisposed || !IsHandleCreated) return;
        _inputCountLabel.Text = $"WM_INPUT Messages: {count}";
    }

    private void OnFrameReady(TouchFrame frame)
    {
        if (IsDisposed || !IsHandleCreated) return;
        _fingerLabel.Text = $"Fingers on Touchpad: {frame.ContactCount}";
        _fingerLabel.ForeColor = frame.ContactCount == 3 ? Color.Yellow : Color.LightSkyBlue;
    }

    private void OnMovementUpdated(double dx, double dy, int fingers)
    {
        if (IsDisposed || !IsHandleCreated) return;
        _moveLabel.Text = $"Live Displacement: dx={dx:F0}  dy={dy:F0}  (fingers: {fingers})";
    }

    private void OnSwipeRecognized(SwipeDirection dir)
    {
        if (IsDisposed || !IsHandleCreated) return;
        _swipeCount++;
        _swipeLabel.Text = $"Last Swipe: {dir} (#{_swipeCount}) @ {DateTime.Now:HH:mm:ss}";
        _swipeLabel.ForeColor = Color.Yellow;
        UpdateQueueDisplay();
    }
}
