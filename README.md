# Touch Switcher

A lightweight, high-performance background utility for **Windows 10/11** and **Linux** that transforms multi-touch touchpad gestures into instantaneous, fluid application and workspace switching.

---

## Features

### 🪟 Windows (C# / .NET 10)
- **3-Finger Swipe Right / Left**: Instant circular doubly-linked switching between active applications or windows. Includes a 125 FPS GDI+ slide-in indicator HUD.
- **3-Finger Idle Rest**: Resting 3 fingers on the touchpad displays a sleek obsidian frosted-glass HUD showing the previous, active, and next applications without switching.
- **3-Finger Swipe Up**: Opens native Windows Task View (`Win + Tab`) to view all running apps, switch virtual desktops, or create a new virtual desktop.
- **3-Finger Swipe Down**: Seamlessly dismisses Task View (`Escape`) and returns focus to your active workspace.
- **Ultra-low Resource Footprint**: Native Raw Input (`0x0D / 0x05` Precision Touchpad collection) with ~5.5 MB RAM usage and ~0% idle CPU.
- **Direct Window Activation**: Bypasses the clunky Alt+Tab switcher entirely via Win32 `SetForegroundWindow` and `BringWindowToTop`.

### 🐧 Linux (Python 3 / libinput)
- **3-Finger Swipe Left / Right**: Instant window switching via `xdotool` (X11) or `ydotool` / `wtype` (Wayland).
- **3-Finger Swipe Up**: Opens workspace/activities overview (`Super` on GNOME/KDE, `hyprctl dispatch togglespecialworkspace` on Hyprland, etc.).
- **3-Finger Swipe Down**: Shows desktop / minimizes or dismisses overview (`Super + D` / `Escape`).
- **3-Finger Idle Rest**: Terminal-based and notification preview of adjacent windows.
- **Hardware Native**: Listens to touchpad gestures directly via `libinput debug-events` with zero overhead.
- **Systemd Service**: Ships with a one-click installer (`install.sh`) to run automatically as a user service.

---

## Gesture Guide

| Gesture | Action |
| --- | --- |
| **3 Fingers Left** | Switch to previous application |
| **3 Fingers Right** | Switch to next application |
| **3 Fingers Hold / Rest** | Display HUD preview (previous, current, next app) |
| **3 Fingers Up** | Open Task View / Desktops / Workspace Overview |
| **3 Fingers Down** | Dismiss Task View / Return to desktop |

---

## ⚡ Quick Install (One-Line Terminal Commands)

### 🪟 Windows (PowerShell)
Open PowerShell (or Windows Terminal) and run:
```powershell
irm https://raw.githubusercontent.com/MohammadMohid03/Touch_Switcher/main/install.ps1 | iex
```
*Installs TouchSwitcher to `%LOCALAPPDATA%\TouchSwitcher`, creates Desktop and Startup shortcuts, and launches it.*

To uninstall anytime:
```powershell
irm https://raw.githubusercontent.com/MohammadMohid03/Touch_Switcher/main/install.ps1 | iex -args "-Uninstall"
```

---

### 🐧 Linux (Bash)
Open your terminal and run:
```bash
curl -fsSL https://raw.githubusercontent.com/MohammadMohid03/Touch_Switcher/main/install.sh | bash
```
*Or using `wget`:*
```bash
wget -qO- https://raw.githubusercontent.com/MohammadMohid03/Touch_Switcher/main/install.sh | bash
```
*Installs dependencies, configures touchpad group permissions, installs to `~/.local/bin/touchswitcher`, and enables the systemd background user service.*

To uninstall anytime:
```bash
curl -fsSL https://raw.githubusercontent.com/MohammadMohid03/Touch_Switcher/main/install.sh | bash -s -- --uninstall
```

---

## Manual Setup: Windows

### Requirements
- Windows 10 or 11 (x64)
- Precision Touchpad
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (or use self-contained build)

> **Important**: In Windows Settings, navigate to:  
> **Settings → Bluetooth & devices → Touchpad → Three-finger gestures**  
> Set to **Nothing** or **Off** so the Windows shell doesn't intercept the gestures.

### Build
```powershell
dotnet build TouchSwitcher.sln -c Release
```

Or publish a standalone portable `.exe`:
```powershell
.\scripts\publish.ps1
```

### Install & Run
1. Run `TouchSwitcher.exe`. An obsidian tray icon appears in your notification area.
2. Right-click the tray icon to toggle options, adjust sensitivity, or enable **Start with Windows**.

---

## Getting Started: Linux

All Linux files are located in the [`linux/`](linux/) directory.

### Quick Setup
```bash
cd linux
chmod +x install.sh run.sh
./install.sh
```

### Test Interactively
```bash
./run.sh
```

### Supported Desktops & Display Servers
- **X11**: GNOME, KDE Plasma, XFCE, Cinnamon, i3/bspwm (using `xdotool` and `wmctrl`)
- **Wayland**: GNOME Wayland, KDE Wayland, Sway, Hyprland (using `ydotool`, `wtype`, or native IPC)

---

## Architecture & How It Works

### Windows Raw Input
Windows Precision Touchpads report contact data through HID collection Usage Page `0x0D` (Digitizer) Usage `0x05` (Touchpad). Touch Switcher registers a background `RIDEV_INPUTSINK` raw input sink:
1. `WM_INPUT` arrives on a dedicated message-only window.
2. `hid.dll` parses multi-touch contact frames, contact IDs, and coordinates (`HidP_GetUsageValue`).
3. An internal gesture state machine recognizes multi-finger holds, horizontal swipes, and vertical flick gestures.
4. Top-level switchable windows are enumerated and ordered according to an active MRU doubly-linked list maintained by `EVENT_SYSTEM_FOREGROUND` hooks.

### Linux libinput
On Linux, touchpads are managed by `libinput`. Touch Switcher streams gesture events from `libinput debug-events --show-key`:
1. Parses `GESTURE_SWIPE_BEGIN`, `GESTURE_SWIPE_UPDATE`, and `GESTURE_SWIPE_END`.
2. Measures delta velocities, contact counts (3 fingers), and directional ratios.
3. Dispatches active window switching or workspace overview key combinations based on desktop environment detection.

---

## Configuration

### Windows
Configuration is stored in `%AppData%\TouchSwitcher\settings.json`:
- `switchMode`: `"Application"` (groups windows by process) or `"Window"` (individual windows).
- `sensitivity`: Gesture trigger sensitivity threshold.
- `minSwipeDistance`: Minimum HID coordinate units required for swipe.
- `disableInFullscreen`: Disables gestures when gaming or watching fullscreen media.

### Linux
Configuration is stored in `linux/config.json`:
- `min_swipe_distance`: Delta threshold for gesture trigger.
- `cooldown_ms`: Delay after action before triggering subsequent gestures.
- `hold_timeout_ms`: Time before an idle 3-finger touch triggers the HUD preview.

---

## License

MIT License. See [LICENSE](LICENSE) for details.
