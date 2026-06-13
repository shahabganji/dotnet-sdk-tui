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
            echo "Remove .NET SDK Manager (dsm) from the specified directory."
            exit 0
            ;;
        *) shift ;;
    esac
done

target="${INSTALL_DIR}/${BINARY_NAME}"

if [ -f "$target" ]; then
    rm -f "$target"
    printf "${GREEN}Removed ${target}${NC}\n"
    printf "${YELLOW}Note: PATH entry in your shell profile was not removed. Edit it manually if needed.${NC}\n"
else
    printf "${RED}dsm not found at ${target}${NC}\n"
    exit 1
fi
