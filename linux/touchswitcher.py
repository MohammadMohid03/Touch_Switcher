#!/usr/bin/env python3
"""
TouchSwitcher for Linux
=======================
3-finger touchpad gesture application switcher for Linux (X11 & Wayland).

Gestures:
  • 3-Finger Swipe Right → Next Application (Alt + Tab)
  • 3-Finger Swipe Left  → Previous Application (Alt + Shift + Tab)
  • 3-Finger Swipe Up    → Overview / All Apps & Desktops (Super / Task View)
  • 3-Finger Swipe Down  → Dismiss Overview / Return to Workspace (Escape / Show Desktop)

Supports GNOME, KDE Plasma, XFCE, and generic Wayland/X11 desktops.
"""

import os
import sys
import time
import json
import re
import shutil
import signal
import subprocess
import argparse
from pathlib import Path

CONFIG_DIR = Path.home() / ".config" / "touchswitcher"
CONFIG_FILE = CONFIG_DIR / "config.json"

DEFAULT_CONFIG = {
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


class Colors:
    HEADER = '\033[95m'
    BLUE = '\033[94m'
    CYAN = '\033[96m'
    GREEN = '\033[92m'
    YELLOW = '\033[93m'
    RED = '\033[91m'
    BOLD = '\033[1m'
    DIM = '\033[2m'
    RESET = '\033[0m'


class SystemInfo:
    def __init__(self):
        self.session_type = os.environ.get("XDG_SESSION_TYPE", "unknown").lower()
        self.desktop = os.environ.get("XDG_CURRENT_DESKTOP", "unknown").lower()
        self.has_libinput = shutil.which("libinput") is not None
        self.has_xdotool = shutil.which("xdotool") is not None
        self.has_ydotool = shutil.which("ydotool") is not None
        self.has_wtype = shutil.which("wtype") is not None
        self.has_gdbus = shutil.which("gdbus") is not None
        self.has_uinput = os.path.exists("/dev/uinput")

    def summary(self):
        tools = []
        if self.has_ydotool: tools.append("ydotool")
        if self.has_xdotool: tools.append("xdotool")
        if self.has_wtype: tools.append("wtype")
        if self.has_gdbus: tools.append("gdbus")
        return {
            "session": self.session_type,
            "desktop": self.desktop,
            "tools": ", ".join(tools) if tools else "None (install xdotool or ydotool)"
        }


class ActionDispatcher:
    def __init__(self, sys_info, config):
        self.sys_info = sys_info
        self.config = config

    def execute(self, action_key):
        custom = self.config.get("custom_commands", {}).get(action_key, "").strip()
        if custom:
            try:
                subprocess.Popen(custom, shell=True)
                return True
            except Exception as e:
                print(f"{Colors.RED}Custom command failed: {e}{Colors.RESET}")

        if action_key == "next_app":
            return self._next_app()
        elif action_key == "prev_app":
            return self._prev_app()
        elif action_key == "overview":
            return self._overview()
        elif action_key == "dismiss":
            return self._dismiss()
        return False

    def _send_keys(self, xdo_keys, ydo_keys, wtype_keys=None):
        # 1. Wayland specific tools
        if self.sys_info.session_type == "wayland":
            if self.sys_info.has_ydotool:
                try:
                    subprocess.run(["ydotool", "key"] + ydo_keys, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                    return True
                except Exception:
                    pass
            if self.sys_info.has_wtype and wtype_keys:
                try:
                    subprocess.run(["wtype"] + wtype_keys, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                    return True
                except Exception:
                    pass

        # 2. X11 or XWayland via xdotool
        if self.sys_info.has_xdotool:
            try:
                subprocess.run(["xdotool", "key"] + xdo_keys, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                return True
            except Exception:
                pass

        # 3. Fallback ydotool if installed
        if self.sys_info.has_ydotool:
            try:
                subprocess.run(["ydotool", "key"] + ydo_keys, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                return True
            except Exception:
                pass

        return False

    def _next_app(self):
        # Alt+Tab
        return self._send_keys(["alt+Tab"], ["56:1", "15:1", "15:0", "56:0"], ["-M", "alt", "-k", "Tab"])

    def _prev_app(self):
        # Alt+Shift+Tab
        return self._send_keys(["alt+shift+Tab"], ["56:1", "42:1", "15:1", "15:0", "42:0", "56:0"], ["-M", "alt", "-M", "shift", "-k", "Tab"])

    def _overview(self):
        # GNOME D-Bus native overview
        if "gnome" in self.sys_info.desktop and self.sys_info.has_gdbus:
            try:
                subprocess.run([
                    "gdbus", "call", "--session",
                    "--dest", "org.gnome.Shell",
                    "--object-path", "/org/gnome/Shell",
                    "--method", "org.gnome.Shell.Eval",
                    "Main.overview.toggle();"
                ], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                return True
            except Exception:
                pass

        # KDE KWin D-Bus native overview / present windows
        if "kde" in self.sys_info.desktop:
            try:
                subprocess.run([
                    "qdbus", "org.kde.kglobalaccel", "/component/kwin",
                    "invokeShortcut", "Overview"
                ], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                return True
            except Exception:
                pass

        # Super key fallback
        return self._send_keys(["Super"], ["125:1", "125:0"], ["-k", "Super_L"])

    def _dismiss(self):
        # Escape key to dismiss overview or popups
        res = self._send_keys(["Escape"], ["1:1", "1:0"], ["-k", "Escape"])
        if not res:
            # Fallback Super+D (Show Desktop)
            return self._send_keys(["super+d"], ["125:1", "32:1", "32:0", "125:0"], ["-M", "super", "-k", "d"])
        return res


class GestureRecognizer:
    def __init__(self, config, on_swipe):
        self.config = config
        self.on_swipe = on_swipe
        self.dx = 0.0
        self.dy = 0.0
        self.is_active = False
        self.last_fire_time = 0.0

    def begin(self, fingers):
        if fingers == 3:
            self.dx = 0.0
            self.dy = 0.0
            self.is_active = True

    def update(self, fingers, d_x, d_y):
        if not self.is_active or fingers != 3:
            return

        self.dx += d_x
        self.dy += d_y

        now = time.time()
        cooldown = self.config.get("cooldown_ms", 250) / 1000.0
        if now - self.last_fire_time < cooldown:
            return

        abs_x = abs(self.dx)
        abs_y = abs(self.dy)
        threshold = self.config.get("min_swipe_distance", 45)
        h_ratio = self.config.get("horizontal_ratio", 1.25)
        v_ratio = self.config.get("vertical_ratio", 1.15)
        v_threshold = max(20, threshold * 0.75)

        is_h = abs_x >= threshold and (abs_y == 0 or (abs_x / abs_y) >= h_ratio)
        is_v = abs_y >= v_threshold and (abs_x == 0 or (abs_y / abs_x) >= v_ratio)

        if not is_h and not is_v:
            return

        self.last_fire_time = now
        self.is_active = False # Reset for next swipe batch

        if is_h and (not is_v or abs_x >= abs_y):
            direction = "swipe_right" if self.dx > 0 else "swipe_left"
        else:
            # In libinput: dy < 0 is upward movement towards screen
            direction = "swipe_up" if self.dy < 0 else "swipe_down"

        self.on_swipe(direction, abs_x, abs_y)

    def end(self, fingers):
        self.is_active = False
        self.dx = 0.0
        self.dy = 0.0


def check_permissions():
    try:
        groups = subprocess.check_output(["groups"], text=True)
        return "input" in groups
    except Exception:
        return False


def load_config():
    CONFIG_DIR.mkdir(parents=True, exist_ok=True)
    if not CONFIG_FILE.exists():
        with open(CONFIG_FILE, "w") as f:
            json.dump(DEFAULT_CONFIG, f, indent=2)
        return DEFAULT_CONFIG
    try:
        with open(CONFIG_FILE, "r") as f:
            return json.load(f)
    except Exception:
        return DEFAULT_CONFIG


def run_daemon(verbose=False):
    sys_info = SystemInfo()
    config = load_config()
    dispatcher = ActionDispatcher(sys_info, config)

    summary = sys_info.summary()
    print(f"\n{Colors.BOLD}{Colors.CYAN}=== TouchSwitcher for Linux ==={Colors.RESET}")
    print(f"{Colors.DIM}Session:{Colors.RESET} {summary['session'].upper()} ({summary['desktop'].title()})")
    print(f"{Colors.DIM}Input Backend:{Colors.RESET} libinput debug-events")
    print(f"{Colors.DIM}Synthesizer:{Colors.RESET} {summary['tools']}")
    print(f"{Colors.DIM}Config:{Colors.RESET} {CONFIG_FILE}\n")

    if not sys_info.has_libinput:
        print(f"{Colors.RED}{Colors.BOLD}ERROR: 'libinput' command not found!{Colors.RESET}")
        print("Please install libinput tools for your distribution:")
        print("  • Debian/Ubuntu/Mint:  sudo apt install libinput-tools xdotool")
        print("  • Fedora/RHEL:         sudo dnf install libinput-utils xdotool")
        print("  • Arch/Manjaro:        sudo pacman -S libinput xdotool")
        sys.exit(1)

    if not check_permissions() and os.geteuid() != 0:
        print(f"{Colors.YELLOW}{Colors.BOLD}NOTE:{Colors.RESET} You may need to add your user to the 'input' group:")
        print(f"  {Colors.BOLD}sudo usermod -aG input $USER{Colors.RESET}")
        print("Then log out and log back in, or run this script with sudo for testing.\n")

    def handle_swipe(direction, dx, dy):
        action_name = config.get("actions", {}).get(direction, direction)
        symbols = {
            "swipe_right": f"{Colors.GREEN}▶ SWIPE RIGHT{Colors.RESET} (Next App)",
            "swipe_left":  f"{Colors.BLUE}◀ SWIPE LEFT{Colors.RESET}  (Previous App)",
            "swipe_up":    f"{Colors.YELLOW}▲ SWIPE UP{Colors.RESET}    (Overview / Desktops)",
            "swipe_down":  f"{Colors.CYAN}▼ SWIPE DOWN{Colors.RESET}  (Dismiss Overview)"
        }
        symbol_text = symbols.get(direction, direction)
        print(f"[{time.strftime('%H:%M:%S')}] {symbol_text} (dx={dx:.0f}, dy={dy:.0f})")
        dispatcher.execute(action_name)

    recognizer = GestureRecognizer(config, handle_swipe)

    # Spawn libinput debug-events
    cmd = ["libinput", "debug-events"]
    try:
        proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, bufsize=1)
    except PermissionError:
        print(f"{Colors.RED}Permission denied accessing /dev/input!{Colors.RESET}")
        print("Run: sudo usermod -aG input $USER && log out/in, OR run with sudo.")
        sys.exit(1)

    def sig_handler(sig, frame):
        print(f"\n{Colors.DIM}Shutting down TouchSwitcher...{Colors.RESET}")
        proc.terminate()
        sys.exit(0)

    signal.signal(signal.SIGINT, sig_handler)
    signal.signal(signal.SIGTERM, sig_handler)

    print(f"{Colors.GREEN}● Active & Listening for 3-finger gestures...{Colors.RESET}")
    print(f"{Colors.DIM}Press Ctrl+C to exit.{Colors.RESET}\n")

    pattern_begin = re.compile(r"GESTURE_SWIPE_BEGIN\s+([+\-\d\.]+s)?\s*(\d+)")
    pattern_update = re.compile(r"GESTURE_SWIPE_UPDATE\s+([+\-\d\.]+s)?\s*(\d+)\s+([+\-\d\.]+)\s*/\s*([+\-\d\.]+)")
    pattern_end = re.compile(r"GESTURE_SWIPE_END\s+([+\-\d\.]+s)?\s*(\d+)")

    while True:
        line = proc.stdout.readline()
        if not line:
            if proc.poll() is not None:
                err = proc.stderr.read()
                if "Permission denied" in err:
                    print(f"{Colors.RED}Permission denied reading input devices!{Colors.RESET}")
                    print("Add your user to input group: sudo usermod -aG input $USER")
                else:
                    print(f"{Colors.RED}libinput error:{Colors.RESET} {err}")
                break
            continue

        if "GESTURE_SWIPE" not in line:
            continue

        m_up = pattern_update.search(line)
        if m_up:
            fingers = int(m_up.group(2))
            dx = float(m_up.group(3))
            dy = float(m_up.group(4))
            recognizer.update(fingers, dx, dy)
            continue

        m_beg = pattern_begin.search(line)
        if m_beg:
            fingers = int(m_beg.group(2))
            recognizer.begin(fingers)
            continue

        m_end = pattern_end.search(line)
        if m_end:
            fingers = int(m_end.group(2))
            recognizer.end(fingers)
            continue


def main():
    parser = argparse.ArgumentParser(description="TouchSwitcher for Linux (3-finger touchpad switcher)")
    parser.add_argument("-v", "--verbose", action="store_true", help="Verbose debug output")
    parser.add_argument("--test", action="store_true", help="Test mode (runs in foreground)")
    args = parser.parse_args()

    run_daemon(verbose=args.verbose)


if __name__ == "__main__":
    main()
