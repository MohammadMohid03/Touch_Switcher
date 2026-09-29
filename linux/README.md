# TouchSwitcher for Linux

3-finger touchpad gesture application switcher for Linux (**X11 & Wayland**).

Enables macOS/Windows-style precision multi-touch touchpad gestures on any Linux distribution (Ubuntu, Fedora, Arch, Debian, Linux Mint, Pop!_OS, etc.).

---

## Gestures

| Gesture | Action | Shortcut / Behavior |
| :--- | :--- | :--- |
| **3-Finger Swipe Right** | **Next App** | Switches forward to the next application (`Alt + Tab`) |
| **3-Finger Swipe Left** | **Previous App** | Switches backward to the previous application (`Alt + Shift + Tab`) |
| **3-Finger Swipe Up** | **Overview / All Desktops & Apps** | Opens Workspace Overview / Task View (`Super` / GNOME Activities / KDE Present Windows) |
| **3-Finger Swipe Down** | **Dismiss Overview** | Closes overview and returns to active workspace (`Escape` / Show Desktop) |

---

## Compatibility

- **Display Servers**: Wayland & X11
- **Desktop Environments**: GNOME (Ubuntu/Fedora default), KDE Plasma, XFCE, Cinnamon, MATE, Pop!_OS, etc.
- **Synthesizers**: Automatically uses `xdotool`, `ydotool`, `wtype`, or GNOME D-Bus depending on your environment.

---

## Quick Start (1 Minute)

### 1. Run the Installer

Open a terminal in the `linux` folder and run:

```bash
chmod +x install.sh run.sh touchswitcher.py
./install.sh
```

The installer will:
- Install `libinput-tools` and `xdotool` via your package manager (`apt`, `dnf`, `pacman`, or `zypper`).
- Add your user to the `input` group so the touchpad can be accessed without root permissions.
- Install the executable to `~/.local/bin/touchswitcher`.
- Set up a systemd background user service.

> **Note**: If you were just added to the `input` group, **log out and log back in** once so the permissions take effect!

---

## Interactive Live Test Mode

To test your touchpad gestures live in the terminal with colored diagnostic feedback:

```bash
./run.sh
```

You will see live logs as you swipe:
```text
[15:30:15] ▶ SWIPE RIGHT (Next App) (dx=54, dy=4)
[15:30:17] ◀ SWIPE LEFT  (Previous App) (dx=-62, dy=2)
[15:30:20] ▲ SWIPE UP    (Overview / Desktops) (dx=6, dy=-78)
[15:30:22] ▼ SWIPE DOWN  (Dismiss Overview) (dx=8, dy=65)
```

Press `Ctrl + C` anytime to exit test mode.

---

## Running in the Background (System Service)

Once tested, enable TouchSwitcher to run automatically in the background whenever you log in:

```bash
# Start the service now
systemctl --user start touchswitcher

# Check service status
systemctl --user status touchswitcher

# View logs
journalctl --user -u touchswitcher -f

# Stop the service
systemctl --user stop touchswitcher
```

---

## Configuration

Settings are saved in `~/.config/touchswitcher/config.json`:

```json
{
  "min_swipe_distance": 45,
  "cooldown_ms": 250,
  "horizontal_ratio": 1.25,
  "vertical_ratio": 1.15,
  "actions": {
    "swipe_right": "next_app",
    "swipe_left": "prev_app",
    "swipe_up": "overview",
    "swipe_down": "dismiss"
  },
  "custom_commands": {
    "next_app": "",
    "prev_app": "",
    "overview": "",
    "dismiss": ""
  }
}
```

You can customize:
- `min_swipe_distance`: Lower values make gestures trigger with shorter finger travel.
- `cooldown_ms`: Delay before a subsequent swipe is recognized (prevents double-firing).
- `custom_commands`: Bind any gesture to run a custom bash command or script!

---

## Troubleshooting

1. **"Permission denied" reading input devices**:
   - Run: `sudo usermod -aG input $USER`
   - Then log out of Linux and log back in.
   - Alternatively, test immediately with `sudo ./run.sh`.

2. **Gestures are detected in terminal but windows don't switch**:
   - Ensure `xdotool` (X11) or `ydotool` (Wayland) is installed:
     - Ubuntu/Debian: `sudo apt install xdotool ydotool`
     - Fedora: `sudo dnf install xdotool ydotool`
     - Arch: `sudo pacman -S xdotool ydotool`
   - If using `ydotool` on Wayland, ensure its daemon is active:
     `systemctl --user start ydotool` or `sudo ydotoold &`
