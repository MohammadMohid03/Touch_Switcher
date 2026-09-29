using System.Drawing;
using System.Windows.Forms;
using TouchSwitcher.Configuration;

namespace TouchSwitcher.UI;

internal sealed class SettingsForm : Form
{
    private readonly SettingsManager _settings;
    private readonly TrackBar _distanceSlider;
    private readonly Label _distanceLabel;
    private readonly TrackBar _sensitivitySlider;
    private readonly Label _sensitivityLabel;
    private readonly RadioButton _appMode;
    private readonly RadioButton _windowMode;
    private readonly CheckBox _startWithWindows;
    private readonly CheckBox _enabledBox;
    private readonly CheckBox _fullscreenBox;
    private readonly CheckBox _animationBox;
    private readonly TextBox _timeoutBox;
    private readonly TextBox _cooldownBox;
    private readonly ListBox _excludedList;

    public SettingsForm(SettingsManager settings)
    {
        _settings = settings;

        Text = "Touch Switcher Settings";
        Size = new Size(500, 640);
        MinimumSize = new Size(440, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(28, 28, 28);
        ForeColor = Color.FromArgb(242, 242, 242);
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;

        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24) };
        Controls.Add(panel);

        int y = 16;

        // --- Gestures header ---
        panel.Controls.Add(MakeLabel("Gestures", 18, FontStyle.Bold, 0, ref y));
        panel.Controls.Add(MakeLabel("3-finger swipe left → Previous app", 9, FontStyle.Regular, 0, ref y, Color.FromArgb(160, 160, 160)));
        panel.Controls.Add(MakeLabel("3-finger swipe right → Next app", 9, FontStyle.Regular, 0, ref y, Color.FromArgb(160, 160, 160)));
        y += 8;

        // Minimum swipe distance
        panel.Controls.Add(MakeLabel("Minimum swipe distance", 9.5f, FontStyle.Regular, 0, ref y));
        _distanceSlider = new TrackBar { Location = new Point(0, y), Width = 400, Minimum = 20, Maximum = 300, TickFrequency = 10, SmallChange = 10, LargeChange = 50, BackColor = Color.FromArgb(28, 28, 28) };
        _distanceSlider.Value = Math.Clamp(settings.Current.MinSwipeDistance, 20, 300);
        panel.Controls.Add(_distanceSlider);
        y += 50;
        _distanceLabel = MakeLabel($"{_distanceSlider.Value} px", 9, FontStyle.Regular, 0, ref y, Color.FromArgb(160, 160, 160));
        panel.Controls.Add(_distanceLabel);
        _distanceSlider.ValueChanged += (_, _) => _distanceLabel.Text = $"{_distanceSlider.Value} px";

        // Sensitivity
        panel.Controls.Add(MakeLabel("Gesture sensitivity", 9.5f, FontStyle.Regular, 0, ref y));
        _sensitivitySlider = new TrackBar { Location = new Point(0, y), Width = 400, Minimum = 0, Maximum = 100, TickFrequency = 5, SmallChange = 1, BackColor = Color.FromArgb(28, 28, 28) };
        _sensitivitySlider.Value = Math.Clamp(settings.Current.Sensitivity, 0, 100);
        panel.Controls.Add(_sensitivitySlider);
        y += 50;
        _sensitivityLabel = MakeLabel($"{_sensitivitySlider.Value}", 9, FontStyle.Regular, 0, ref y, Color.FromArgb(160, 160, 160));
        panel.Controls.Add(_sensitivityLabel);
        _sensitivitySlider.ValueChanged += (_, _) => _sensitivityLabel.Text = $"{_sensitivitySlider.Value}";
        y += 8;

        // Switch mode
        panel.Controls.Add(MakeLabel("Switch mode", 9.5f, FontStyle.Regular, 0, ref y));
        _appMode = new RadioButton { Text = "Application", Location = new Point(0, y), ForeColor = ForeColor, AutoSize = true };
        _windowMode = new RadioButton { Text = "Window", Location = new Point(140, y), ForeColor = ForeColor, AutoSize = true };
        _appMode.Checked = settings.Current.SwitchMode == SwitchMode.Application;
        _windowMode.Checked = settings.Current.SwitchMode == SwitchMode.Window;
        panel.Controls.Add(_appMode);
        panel.Controls.Add(_windowMode);
        y += 30;

        // --- Startup header ---
        y += 10;
        panel.Controls.Add(MakeLabel("Startup", 18, FontStyle.Bold, 0, ref y));
        _startWithWindows = MakeCheckBox("Start automatically with Windows", settings.Current.StartWithWindows, ref y);
        panel.Controls.Add(_startWithWindows);
        _enabledBox = MakeCheckBox("Gestures enabled", settings.Current.Enabled, ref y);
        panel.Controls.Add(_enabledBox);
        _fullscreenBox = MakeCheckBox("Disable gestures in fullscreen applications", settings.Current.DisableInFullscreen, ref y);
        panel.Controls.Add(_fullscreenBox);
        _animationBox = MakeCheckBox("Enable Mac slide motion animation", settings.Current.EnableSlideAnimation, ref y);
        panel.Controls.Add(_animationBox);

        // --- Advanced header ---
        y += 10;
        panel.Controls.Add(MakeLabel("Advanced", 18, FontStyle.Bold, 0, ref y));
        panel.Controls.Add(MakeLabel("Gesture timeout (ms)", 9.5f, FontStyle.Regular, 0, ref y));
        _timeoutBox = new TextBox { Location = new Point(0, y), Width = 120, BackColor = Color.FromArgb(38, 38, 38), ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle, Text = settings.Current.GestureTimeoutMs.ToString() };
        panel.Controls.Add(_timeoutBox);
        y += 30;
        panel.Controls.Add(MakeLabel("Cooldown (ms)", 9.5f, FontStyle.Regular, 0, ref y));
        _cooldownBox = new TextBox { Location = new Point(0, y), Width = 120, BackColor = Color.FromArgb(38, 38, 38), ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle, Text = settings.Current.CooldownMs.ToString() };
        panel.Controls.Add(_cooldownBox);
        y += 34;

        // Excluded apps
        panel.Controls.Add(MakeLabel("Excluded applications", 9.5f, FontStyle.Regular, 0, ref y));
        _excludedList = new ListBox { Location = new Point(0, y), Width = 320, Height = 90, BackColor = Color.FromArgb(38, 38, 38), ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle };
        foreach (string exe in settings.Current.ExcludedApplications) _excludedList.Items.Add(exe);
        panel.Controls.Add(_excludedList);

        var addBtn = MakeButton("Add", 340, y);
        addBtn.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe", Title = "Exclude application" };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                string name = SettingsManager.NormalizeExe(dlg.FileName);
                if (!_excludedList.Items.Contains(name)) _excludedList.Items.Add(name);
            }
        };
        panel.Controls.Add(addBtn);
        var rmBtn = MakeButton("Remove", 340, y + 36);
        rmBtn.Click += (_, _) => { if (_excludedList.SelectedItem is string s) _excludedList.Items.Remove(s); };
        panel.Controls.Add(rmBtn);
        y += 100;

        // Save / Close
        var saveBtn = MakeButton("Save", 280, y);
        saveBtn.Click += (_, _) => { Save(); Close(); };
        panel.Controls.Add(saveBtn);
        var closeBtn = MakeButton("Close", 370, y);
        closeBtn.Click += (_, _) => Close();
        panel.Controls.Add(closeBtn);
    }

    private void Save()
    {
        var next = new AppSettings
        {
            Enabled = _enabledBox.Checked,
            SwitchMode = _appMode.Checked ? SwitchMode.Application : SwitchMode.Window,
            MinSwipeDistance = _distanceSlider.Value,
            HorizontalVerticalRatio = _settings.Current.HorizontalVerticalRatio,
            GestureTimeoutMs = int.TryParse(_timeoutBox.Text, out int t) ? t : _settings.Current.GestureTimeoutMs,
            CooldownMs = int.TryParse(_cooldownBox.Text, out int c) ? c : _settings.Current.CooldownMs,
            RequiredFingers = 3,
            Sensitivity = _sensitivitySlider.Value,
            StartWithWindows = _startWithWindows.Checked,
            DisableInFullscreen = _fullscreenBox.Checked,
            EnableSlideAnimation = _animationBox.Checked,
            ExcludedApplications = _excludedList.Items.Cast<string>().ToList()
        };
        _settings.Save(next);
    }

    private static Label MakeLabel(string text, float size, FontStyle style, int x, ref int y, Color? color = null)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            Font = new Font("Segoe UI", size, style),
            ForeColor = color ?? Color.FromArgb(242, 242, 242)
        };
        y += label.PreferredHeight + 4;
        return label;
    }

    private static CheckBox MakeCheckBox(string text, bool isChecked, ref int y)
    {
        var cb = new CheckBox
        {
            Text = text,
            Checked = isChecked,
            Location = new Point(0, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(242, 242, 242)
        };
        y += 26;
        return cb;
    }

    private static Button MakeButton(string text, int x, int y)
    {
        return new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(80, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(51, 51, 51),
            ForeColor = Color.FromArgb(242, 242, 242),
            FlatAppearance = { BorderColor = Color.FromArgb(58, 58, 58) }
        };
    }
}
