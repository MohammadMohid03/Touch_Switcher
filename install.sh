#!/usr/bin/env bash
# ==============================================================================
# TouchSwitcher for Linux - One-Line Terminal Installer
# ==============================================================================
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/MohammadMohid03/Touch_Switcher/main/install.sh | bash
#   wget -qO- https://raw.githubusercontent.com/MohammadMohid03/Touch_Switcher/main/install.sh | bash
# ==============================================================================
set -e

REPO_RAW="https://raw.githubusercontent.com/MohammadMohid03/Touch_Switcher/main"
INSTALL_BIN="$HOME/.local/bin"
CONFIG_DIR="$HOME/.config/touchswitcher"
SERVICE_DIR="$HOME/.config/systemd/user"

# Colors
CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${CYAN}"
echo "====================================================="
echo "          TouchSwitcher for Linux Installer          "
echo "====================================================="
echo -e "${NC}"

# Handle Uninstall
if [[ "$1" == "--uninstall" || "$1" == "-u" ]]; then
    echo -e "${YELLOW}[>] Uninstalling TouchSwitcher...${NC}"
    systemctl --user stop touchswitcher.service 2>/dev/null || true
    systemctl --user disable touchswitcher.service 2>/dev/null || true
    rm -f "$SERVICE_DIR/touchswitcher.service"
    rm -f "$INSTALL_BIN/touchswitcher"
    rm -rf "$CONFIG_DIR"
    systemctl --user daemon-reload 2>/dev/null || true
    echo -e "${GREEN}[✓] TouchSwitcher has been completely removed.${NC}"
    exit 0
fi

# 1. Dependency Installation
echo -e "${CYAN}[>] Checking and installing dependencies...${NC}"
SUDO_CMD=""
if [ "$EUID" -ne 0 ]; then
    if command -v sudo &>/dev/null; then
        SUDO_CMD="sudo"
    else
        echo -e "${RED}[!] sudo is required to install dependencies and configure input group.${NC}"
        exit 1
    fi
fi

if command -v apt-get &>/dev/null; then
    echo "    Debian/Ubuntu detected."
    $SUDO_CMD apt-get update -qq
    $SUDO_CMD apt-get install -y -qq libinput-tools xdotool python3 curl
elif command -v dnf &>/dev/null; then
    echo "    Fedora/RHEL detected."
    $SUDO_CMD dnf install -y -q libinput-utils xdotool python3 curl
elif command -v pacman &>/dev/null; then
    echo "    Arch Linux / Manjaro detected."
    $SUDO_CMD pacman -S --needed --noconfirm libinput xdotool python curl
elif command -v zypper &>/dev/null; then
    echo "    openSUSE detected."
    $SUDO_CMD zypper install -y libinput-tools xdotool python3 curl
else
    echo -e "${YELLOW}[!] Unknown package manager. Please ensure 'libinput-tools', 'xdotool', and 'python3' are installed.${NC}"
fi

# 2. Add User to input group
NEED_RELOG=0
CURRENT_USER="${SUDO_USER:-$USER}"
if ! id -nG "$CURRENT_USER" | grep -qw "input"; then
    echo -e "${CYAN}[>] Adding $CURRENT_USER to 'input' group for touchpad access...${NC}"
    $SUDO_CMD usermod -aG input "$CURRENT_USER"
    NEED_RELOG=1
    echo -e "${GREEN}[✓] Added to 'input' group.${NC}"
else
    echo -e "${GREEN}[✓] User $CURRENT_USER already has touchpad (input) access.${NC}"
fi

# 3. Create directories
mkdir -p "$INSTALL_BIN"
mkdir -p "$CONFIG_DIR"
mkdir -p "$SERVICE_DIR"

# 4. Fetch / Copy Files
echo -e "${CYAN}[>] Installing TouchSwitcher executable...${NC}"
SCRIPT_PATH="$INSTALL_BIN/touchswitcher"

# Check if installing from local repo or remote pipe
LOCAL_SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" 2>/dev/null && pwd || true)"
if [ -f "$LOCAL_SOURCE_DIR/linux/touchswitcher.py" ]; then
    cp "$LOCAL_SOURCE_DIR/linux/touchswitcher.py" "$SCRIPT_PATH"
    [ ! -f "$CONFIG_DIR/config.json" ] && cp "$LOCAL_SOURCE_DIR/linux/config.json" "$CONFIG_DIR/config.json"
elif [ -f "$LOCAL_SOURCE_DIR/touchswitcher.py" ]; then
    cp "$LOCAL_SOURCE_DIR/touchswitcher.py" "$SCRIPT_PATH"
    [ ! -f "$CONFIG_DIR/config.json" ] && cp "$LOCAL_SOURCE_DIR/config.json" "$CONFIG_DIR/config.json"
else
    echo "    Downloading latest script from GitHub..."
    curl -fsSL "$REPO_RAW/linux/touchswitcher.py" -o "$SCRIPT_PATH"
    if [ ! -f "$CONFIG_DIR/config.json" ]; then
        curl -fsSL "$REPO_RAW/linux/config.json" -o "$CONFIG_DIR/config.json"
    fi
fi

chmod +x "$SCRIPT_PATH"
echo -e "${GREEN}[✓] Installed to $SCRIPT_PATH${NC}"

# Ensure ~/.local/bin is in PATH
if [[ ":$PATH:" != *":$HOME/.local/bin:"* ]]; then
    for RC in "$HOME/.bashrc" "$HOME/.zshrc" "$HOME/.profile"; do
        if [ -f "$RC" ]; then
            if ! grep -q 'HOME/.local/bin' "$RC"; then
                echo 'export PATH="$HOME/.local/bin:$PATH"' >> "$RC"
            fi
        fi
    done
fi

# 5. Setup Systemd User Service
echo -e "${CYAN}[>] Configuring systemd user service...${NC}"
cat << EOF > "$SERVICE_DIR/touchswitcher.service"
[Unit]
Description=TouchSwitcher (3-Finger Touchpad App Switcher)
After=graphical-session.target

[Service]
Type=simple
ExecStart=%h/.local/bin/touchswitcher
Restart=always
RestartSec=3

[Install]
WantedBy=default.target
EOF

systemctl --user daemon-reload 2>/dev/null || true
systemctl --user enable touchswitcher.service 2>/dev/null || true
echo -e "${GREEN}[✓] Systemd user service configured.${NC}"

# 6. Summary & Completion
echo ""
echo -e "${CYAN}=====================================================${NC}"
echo -e "${GREEN}             Installation Complete!                  ${NC}"
echo -e "${CYAN}=====================================================${NC}"
echo ""
echo " Binary:  $SCRIPT_PATH"
echo " Config:  $CONFIG_DIR/config.json"
echo ""
echo -e "${CYAN}GESTURES:${NC}"
echo "   3 Fingers Left/Right : Switch applications"
echo "   3 Fingers Hold/Rest  : Display preview of adjacent windows"
echo "   3 Fingers Up         : Workspace / Overview (Super / Win)"
echo "   3 Fingers Down       : Show Desktop / Dismiss Overview"
echo ""

if [ "$NEED_RELOG" -eq 1 ]; then
    echo -e "${YELLOW}IMPORTANT NOTICE:${NC}"
    echo "  Your user account was added to the 'input' group."
    echo "  Please LOG OUT and LOG BACK IN to activate touchpad permissions!"
    echo ""
    echo "  To test immediately right now without logging out:"
    echo "    sudo touchswitcher"
else
    systemctl --user restart touchswitcher.service 2>/dev/null || true
    echo -e "${GREEN}TouchSwitcher background service is active!${NC}"
    echo "To test interactively in the terminal:"
    echo "  touchswitcher"
fi
echo ""
