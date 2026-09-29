#!/usr/bin/env bash
# Quick runner for testing TouchSwitcher live in the terminal
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v libinput &>/dev/null; then
    echo "libinput not found! Run ./install.sh first."
    exit 1
fi

if ! groups "$USER" | grep -q "\binput\b" && [ "$EUID" -ne 0 ]; then
    echo "Running with sudo (required until you log out/in after adding to 'input' group)..."
    sudo python3 "$DIR/touchswitcher.py" "$@"
else
    python3 "$DIR/touchswitcher.py" "$@"
fi
