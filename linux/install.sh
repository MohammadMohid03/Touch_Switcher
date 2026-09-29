#!/usr/bin/env bash
# ==============================================================================
# TouchSwitcher for Linux - One-Click Installer
# ==============================================================================
set -e

echo ""
echo "============================================="
echo "       TouchSwitcher for Linux Installer     "
echo "============================================="
echo ""

# 1. Detect Package Manager & Install Dependencies
echo "--> Checking dependencies..."

if command -v apt-get &>/dev/null; then
    echo "    Debian/Ubuntu/Mint detected."
    sudo apt-get update -qq
    sudo apt-get install -y libinput-tools xdotool python3
elif command -v dnf &>/dev/null; then
    echo "    Fedora/RHEL detected."
    sudo dnf install -y libinput-utils xdotool python3
elif command -v pacman &>/dev/null; then
    echo "    Arch Linux / Manjaro detected."
    sudo pacman -S --needed --noconfirm libinput xdotool python
elif command -v zypper &>/dev/null; then
    echo "    openSUSE detected."
    sudo zypper install -y libinput-tools xdotool python3
else
    echo "    Notice: Please ensure 'libinput-tools' (or libinput-utils) and 'xdotool' are installed."
fi

# 2. Add current user to input group for touchpad access
if ! groups "$USER" | grep -q "\binput\b"; then
    echo "--> Adding $USER to 'input' group for touchpad permissions..."
    sudo usermod -aG input "$USER"
    NEED_RELOG=1
else
    echo "--> User $USER is already in the 'input' group. Great!"
    NEED_RELOG=0
fi

# 3. Install binary to ~/.local/bin
INSTALL_DIR="$HOME/.local/bin"
mkdir -p "$INSTALL_DIR"
cp "$(dirname "$0")/touchswitcher.py" "$INSTALL_DIR/touchswitcher"
chmod +x "$INSTALL_DIR/touchswitcher"
echo "--> Installed executable to $INSTALL_DIR/touchswitcher"

# Ensure ~/.local/bin is in PATH in ~/.bashrc or ~/.zshrc
if [[ ":$PATH:" != *":$HOME/.local/bin:"* ]]; then
    echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$HOME/.bashrc"
    [ -f "$HOME/.zshrc" ] && echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$HOME/.zshrc"
fi

# 4. Install systemd user service for background auto-start
SERVICE_DIR="$HOME/.config/systemd/user"
mkdir -p "$SERVICE_DIR"
cat << EOF > "$SERVICE_DIR/touchswitcher.service"
[Unit]
Description=TouchSwitcher (3-finger touchpad app switcher)
After=graphical-session.target

[Service]
ExecStart=%h/.local/bin/touchswitcher
Restart=always
RestartSec=3

[Install]
WantedBy=default.target
EOF

systemctl --user daemon-reload
systemctl --user enable touchswitcher.service
echo "--> Configured systemd user service at $SERVICE_DIR/touchswitcher.service"

echo ""
echo "============================================="
echo "       Installation Completed Successfully!   "
echo "============================================="
echo ""

if [ "$NEED_RELOG" -eq 1 ]; then
    echo "IMPORTANT:"
    echo "  You were added to the 'input' group. Please LOG OUT and LOG BACK IN"
    echo "  so the new touchpad permissions take effect!"
    echo ""
    echo "To test immediately right now without logging out, run:"
    echo "  sudo python3 $(dirname "$0")/touchswitcher.py"
else
    echo "To start TouchSwitcher in the background now, run:"
    echo "  systemctl --user start touchswitcher.service"
    echo ""
    echo "Or run in terminal live test mode:"
    echo "  touchswitcher"
fi
echo ""
