#!/usr/bin/env bash
# uninstall.sh — Remove .NET SDK Manager (dsm)
#
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/shahabganji/dotnet-sdk-tui/main/install/uninstall.sh | bash

set -euo pipefail

BINARY_NAME="dsm"
INSTALL_DIR="${HOME}/.local/bin"

if [ -t 1 ]; then
    GREEN='\033[0;32m' YELLOW='\033[1;33m' RED='\033[0;31m' NC='\033[0m'
else
    GREEN='' YELLOW='' RED='' NC=''
fi

# Parse arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        --install-dir) INSTALL_DIR="$2"; shift 2 ;;
        --help|-h)
            echo "Usage: $(basename "$0") [--install-dir DIR]"
            echo "Remove .NET SDK Manager (dsm) from the specified directory and clean up PATH."
            exit 0
            ;;
        *) shift ;;
    esac
done

target="${INSTALL_DIR}/${BINARY_NAME}"

if [ ! -f "$target" ]; then
    printf "${RED}dsm not found at ${target}${NC}\n"
    exit 1
fi

rm -f "$target"
printf "${GREEN}Removed ${target}${NC}\n"

# Remove PATH entry from shell profile
shell_name=$(basename "${SHELL:-/bin/bash}")
case "$shell_name" in
    zsh)   profile="$HOME/.zshrc" ;;
    fish)  profile="$HOME/.config/fish/config.fish" ;;
    *)     profile="$HOME/.bashrc" ;;
esac

if [ -f "$profile" ]; then
    # Remove the PATH export line added by the installer
    if grep -qF "$INSTALL_DIR" "$profile" 2>/dev/null; then
        if [ "$shell_name" = "fish" ]; then
            sed -i.bak "/fish_add_path.*$(echo "$INSTALL_DIR" | sed 's/[\/&]/\\&/g')/d" "$profile"
        else
            sed -i.bak "/export PATH=.*$(echo "$INSTALL_DIR" | sed 's/[\/&]/\\&/g')/d" "$profile"
        fi
        rm -f "${profile}.bak"
        printf "${GREEN}Removed PATH entry from ${profile}${NC}\n"
    fi
fi

# Remove install dir if empty
if [ -d "$INSTALL_DIR" ] && [ -z "$(ls -A "$INSTALL_DIR" 2>/dev/null)" ]; then
    rmdir "$INSTALL_DIR" 2>/dev/null || true
fi

printf "${GREEN}dsm has been uninstalled.${NC}\n"
