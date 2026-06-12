#!/usr/bin/env bash
# get-dotnet-sdk-tui.sh
#
# Downloads the latest dotnet-sdk-tui release and installs it locally.
# Inspired by https://github.com/dotnet/sdk/blob/release/dnup/scripts/get-dotnetup.sh
#
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/shahabganji/dotnet-sdk-tui/main/install/install.sh | bash
#   ./install.sh --install-dir /opt/dotnet-sdk-tui
#   ./install.sh --runtime-id linux-musl-x64

set -euo pipefail

# --- Defaults ---
readonly BINARY_NAME="dotnet-sdk-tui"
readonly REPO_OWNER="shahabganji"
readonly REPO_NAME="dotnet-sdk-tui"
INSTALL_DIR="${HOME}/.local/bin"
RUNTIME_ID=""

# --- Colors (disabled if not a terminal) ---
if [ -t 1 ]; then
    CYAN='\033[0;36m'
    GREEN='\033[0;32m'
    YELLOW='\033[1;33m'
    RED='\033[0;31m'
    GRAY='\033[0;90m'
    NC='\033[0m'
else
    CYAN='' GREEN='' YELLOW='' RED='' GRAY='' NC=''
fi

info()  { printf "${CYAN}==> %s${NC}\n" "$*"; }
ok()    { printf "${GREEN}%s${NC}\n" "$*"; }
warn()  { printf "${YELLOW}%s${NC}\n" "$*"; }
err()   { printf "${RED}Error: %s${NC}\n" "$*" >&2; }

# --- Parse arguments ---
while [[ $# -gt 0 ]]; do
    case "$1" in
        --install-dir)  INSTALL_DIR="$2"; shift 2 ;;
        --runtime-id)   RUNTIME_ID="$2"; shift 2 ;;
        --help|-h)
            cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Downloads the latest dotnet-sdk-tui release and installs it locally.

Options:
  --install-dir DIR     Installation directory (default: ~/.local/bin)
  --runtime-id RID      Override OS/architecture detection (e.g. linux-musl-x64, osx-arm64)
  --help, -h            Show this help message
EOF
            exit 0
            ;;
        *) err "Unknown option: $1"; exit 1 ;;
    esac
done

# --- Banner ---
banner() {
    printf '\n'
    printf "${GREEN}  ✦────────────────────────────────────────────✦${NC}\n"
    printf "${GREEN}  ★   dotnet-sdk-tui installer — Let's-a go!   ★${NC}\n"
    printf "${GREEN}  ✦   Silent steps. Sharp tools. Clean setup.  ✦${NC}\n"
    printf "${GREEN}  ✦────────────────────────────────────────────✦${NC}\n"
    printf '\n'
}

# --- Download helper (curl with wget fallback) ---
if command -v curl &>/dev/null; then
    DOWNLOADER="curl"
elif command -v wget &>/dev/null; then
    DOWNLOADER="wget"
else
    err "Neither 'curl' nor 'wget' was found on PATH. One is required."
    exit 1
fi

download() {
    local url="$1" out="$2"
    if [ "$DOWNLOADER" = "curl" ]; then
        curl --fail --location --retry 3 --progress-bar --output "$out" "$url"
    else
        wget --tries=3 --output-document="$out" "$url"
    fi
}

# --- Detect musl vs glibc (for Linux) ---
is_musl() {
    if getconf GNU_LIBC_VERSION &>/dev/null; then
        return 1
    fi
    if ldd --version 2>&1 | grep -qi "musl"; then
        return 0
    fi
    if ls /lib/ld-musl-* &>/dev/null; then
        return 0
    fi
    return 1
}

# --- Detect runtime ID ---
detect_rid() {
    if [ -n "$RUNTIME_ID" ]; then
        echo "$RUNTIME_ID"
        return
    fi

    local os arch

    case "$(uname -s)" in
        Linux)
            if is_musl; then
                os="linux-musl"
            else
                os="linux"
            fi
            ;;
        Darwin)
            os="osx"
            ;;
        *)
            err "Unsupported OS: $(uname -s). Use --runtime-id to specify manually."
            exit 1
            ;;
    esac

    # On macOS, uname -m reports x86_64 under Rosetta 2. Prefer native hw check.
    local machine_arch
    if [ "$os" = "osx" ] && [ "$(sysctl -n hw.optional.arm64 2>/dev/null || echo 0)" = "1" ]; then
        machine_arch="arm64"
    else
        machine_arch="$(uname -m)"
    fi

    case "$machine_arch" in
        x86_64|amd64)    arch="x64" ;;
        aarch64|arm64)   arch="arm64" ;;
        *)
            err "Unsupported architecture: $machine_arch. Use --runtime-id to specify manually."
            exit 1
            ;;
    esac

    echo "${os}-${arch}"
}

# --- Cleanup ---
cleanup() {
    if [[ -n "${TEMP_DIR:-}" && -d "${TEMP_DIR}" ]]; then
        rm -rf "$TEMP_DIR"
    fi
}
trap cleanup EXIT

# --- Main ---
main() {
    banner

    local rid download_url
    rid="$(detect_rid)"
    download_url="https://github.com/${REPO_OWNER}/${REPO_NAME}/releases/latest/download/${BINARY_NAME}-${rid}.tar.gz"

    info "Detected runtime: ${rid}"
    info "Installing into ${INSTALL_DIR}"

    TEMP_DIR=$(mktemp -d)
    local archive_path="${TEMP_DIR}/${BINARY_NAME}.tar.gz"

    mkdir -p "$INSTALL_DIR"

    info "Downloading release archive"
    if ! download "$download_url" "$archive_path"; then
        err "Download failed: ${download_url}"
        err "Available RIDs: osx-x64, osx-arm64, linux-x64, linux-arm64"
        exit 1
    fi

    info "Extracting archive"
    if ! tar -xzf "$archive_path" -C "$TEMP_DIR"; then
        err "Extraction failed"
        exit 1
    fi

    local binary_path target_path
    binary_path="$(find "$TEMP_DIR" -type f -name "$BINARY_NAME" -print -quit)"
    [[ -n "$binary_path" ]] || { err "Could not find ${BINARY_NAME} in archive."; exit 1; }

    target_path="${INSTALL_DIR}/${BINARY_NAME}"
    cp "$binary_path" "$target_path"
    chmod 0755 "$target_path"

    printf '\n'
    ok "✦ Installation complete. ${BINARY_NAME} is ready at ${target_path}"

    # Check PATH
    RESOLVED_INSTALL_DIR=$(cd "$INSTALL_DIR" 2>/dev/null && pwd -P || echo "$INSTALL_DIR")
    local on_path=false
    local dir
    while IFS= read -r -d ':' dir || [ -n "$dir" ]; do
        dir="${dir%/}"
        [ -z "$dir" ] && continue
        local resolved
        resolved=$(cd "$dir" 2>/dev/null && pwd -P || echo "$dir")
        if [ "$resolved" = "$RESOLVED_INSTALL_DIR" ]; then
            on_path=true
            break
        fi
    done <<< "$PATH"

    if $on_path; then
        ok "${BINARY_NAME} is already on your PATH. Run '${BINARY_NAME}' to get started."
    else
        warn "Add ${INSTALL_DIR} to your PATH:"
        printf '\n'
        printf "${GRAY}  # Current session:${NC}\n"
        printf "  export PATH=\"%s:\$PATH\"\n" "$INSTALL_DIR"
        printf '\n'

        local shell_name profile
        shell_name=$(basename "${SHELL:-/bin/bash}")
        case "$shell_name" in
            zsh)   profile="~/.zshrc" ;;
            fish)  profile="~/.config/fish/config.fish" ;;
            *)     profile="~/.bashrc" ;;
        esac

        printf "${GRAY}  # Permanently:${NC}\n"
        if [ "$shell_name" = "fish" ]; then
            printf "  fish_add_path \"%s\"\n" "$INSTALL_DIR"
        else
            printf "  echo 'export PATH=\"%s:\$PATH\"' >> %s\n" "$INSTALL_DIR" "$profile"
        fi
        printf '\n'
    fi
}

main "$@"
