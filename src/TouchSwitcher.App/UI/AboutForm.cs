using System.Drawing;
using System.Windows.Forms;

namespace TouchSwitcher.UI;

internal sealed class AboutForm : Form
{
    public AboutForm()
    {
        Text = "About Touch Switcher";
        Size = new Size(440, 340);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(28, 28, 28);
        ForeColor = Color.FromArgb(242, 242, 242);
        Font = new Font("Segoe UI", 9.5f);

        var title = new Label { Text = "Touch Switcher", Font = new Font("Segoe UI", 16, FontStyle.Bold), AutoSize = true, Location = new Point(24, 20), ForeColor = ForeColor };
        var version = new Label { Text = "Version 1.0.0", AutoSize = true, Location = new Point(24, 50), ForeColor = Color.FromArgb(160, 160, 160) };
        var desc = new Label
        {
            Text = "Background utility for Windows Precision Touchpads.\n\nA 3-finger horizontal swipe activates the previous or next application directly — no Alt+Tab overlay.",
            Location = new Point(24, 80), Size = new Size(380, 60), ForeColor = ForeColor
        };
        var tech = new Label
        {
            Text = "Uses Raw Input on HID usage page 0x0D / usage 0x05 (Precision Touchpad). Switching uses SetForegroundWindow — not Alt+Tab.",
            Location = new Point(24, 150), Size = new Size(380, 50), ForeColor = Color.FromArgb(160, 160, 160)
        };
        var tip = new Label
        {
            Text = "Tip: Turn off 3-finger gestures in Windows Settings → Bluetooth & devices → Touchpad so this app can own the gesture.",
            Location = new Point(24, 210), Size = new Size(380, 40), ForeColor = Color.FromArgb(130, 130, 130)
        };
        var closeBtn = new Button
        {
            Text = "Close", Location = new Point(330, 265), Size = new Size(80, 30),
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(51, 51, 51), ForeColor = ForeColor,
            FlatAppearance = { BorderColor = Color.FromArgb(58, 58, 58) }
        };
        closeBtn.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { title, version, desc, tech, tip, closeBtn });
    }
}
