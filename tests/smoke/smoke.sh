#!/usr/bin/env bash
# smoke.sh — Verify dsm installs, runs and uninstalls on this machine (Linux/macOS).
#
# Exercises the real install/uninstall scripts inside a sandbox HOME, so it never
# touches your real ~/.local/bin or shell profile.
#
# Usage:
#   tests/smoke/smoke.sh install   [--archive PATH | --release [TAG]] [--expect-version X.Y.Z]
#   tests/smoke/smoke.sh uninstall [--release]
#   tests/smoke/smoke.sh all       ...  (install + uninstall)
#
# Environment:
#   SMOKE_HOME   Sandbox home directory (default: a new temp dir; reuse it across
#                `install` and `uninstall`). On GitHub Actions the path of the
#                installed binary is exported as DSM_E2E_BINARY via $GITHUB_ENV.
#   SMOKE_SCRIPTS_URL  Base URL for install scripts in --release mode
#                (default: the main branch on GitHub — what users actually run).

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
scripts_url="${SMOKE_SCRIPTS_URL:-https://raw.githubusercontent.com/shahabganji/dotnet-sdk-tui/main/install}"

phase="${1:-}"; shift || true
archive="" release=false tag="" expect_version=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --archive)        archive="$2"; shift 2 ;;
        --release)        release=true
                          if [[ $# -gt 1 && "$2" != --* ]]; then tag="$2"; shift; fi
                          shift ;;
        --expect-version) expect_version="${2#v}"; shift 2 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

fail() { echo "SMOKE FAIL: $*" >&2; exit 1; }
pass() { echo "SMOKE OK:   $*"; }

export SMOKE_HOME="${SMOKE_HOME:-$(mktemp -d)}"
mkdir -p "$SMOKE_HOME"
install_dir="$SMOKE_HOME/.local/bin"
binary="$install_dir/dsm"
profile="$SMOKE_HOME/.bashrc"
path_line="export PATH=\"$install_dir:\$PATH\""

# Run an install script the way users do (`curl | bash`), against the sandbox HOME.
run_script() {
    local name="$1"; shift
    local vars=(HOME="$SMOKE_HOME" SHELL=/bin/bash PATH="$PATH" "$@")
    if $release; then
        curl -fsSL --retry 3 "$scripts_url/$name" | env "${vars[@]}" bash
    else
        env "${vars[@]}" bash < "$repo_root/install/$name"
    fi
}

do_install() {
    local vars=()
    if $release; then
        if [[ -n "$tag" ]]; then vars+=(DSM_VERSION="$tag"); fi
    else
        [[ -n "$archive" ]] || fail "--archive PATH or --release is required"
        [[ -f "$archive" ]] || fail "archive not found: $archive"
        vars+=(DSM_ARCHIVE="$(cd "$(dirname "$archive")" && pwd)/$(basename "$archive")")
    fi

    local out
    out="$(run_script install.sh ${vars[@]+"${vars[@]}"} 2>&1)" || { echo "$out"; fail "installer exited non-zero"; }
    echo "$out"
    grep -q "Installing dsm" <<<"$out" || fail "fresh install did not report 'Installing'"
    [[ -x "$binary" ]] || fail "$binary missing or not executable"
    pass "installed $binary"

    [[ "$(grep -cxF "$path_line" "$profile" 2>/dev/null)" == 1 ]] || fail "$profile should contain the PATH line exactly once"
    pass "PATH added to $profile"

    out="$(run_script install.sh ${vars[@]+"${vars[@]}"} 2>&1)" || { echo "$out"; fail "re-install exited non-zero"; }
    grep -q "Updating dsm" <<<"$out" || { echo "$out"; fail "re-install did not report 'Updating'"; }
    [[ "$(grep -cxF "$path_line" "$profile")" == 1 ]] || fail "re-install duplicated the PATH line in $profile"
    pass "re-install updates in place"

    local version
    if ! version="$(PATH="$install_dir:$PATH" dsm --version 2>&1)"; then
        echo "$version"
        if command -v ldd >/dev/null; then
            echo "--- ldd $binary"; ldd "$binary" || true
            echo "--- libc"; ldd --version 2>&1 | head -1 || true
        fi
        if command -v file >/dev/null; then file "$binary"; fi
        fail "'dsm --version' failed"
    fi
    echo "$version"
    [[ "$version" =~ ^dsm\ [0-9]+\.[0-9]+\.[0-9]+ ]] || fail "unexpected --version output: $version"
    if [[ -n "$expect_version" && "$version" != "dsm $expect_version" ]]; then
        fail "expected 'dsm $expect_version', got '$version'"
    fi
    pass "$version"

    if [[ -n "${GITHUB_ENV:-}" ]]; then
        { echo "DSM_E2E_BINARY=$binary"; echo "SMOKE_HOME=$SMOKE_HOME"; } >> "$GITHUB_ENV"
    fi
    echo "SMOKE_HOME=$SMOKE_HOME"
}

do_uninstall() {
    local out
    out="$(run_script uninstall.sh 2>&1)" || { echo "$out"; fail "uninstaller exited non-zero"; }
    echo "$out"
    [[ ! -e "$binary" ]] || fail "$binary still exists"
    if grep -qF "$install_dir" "$profile" 2>/dev/null; then fail "$profile still references $install_dir"; fi
    pass "uninstalled"
}

case "$phase" in
    install)   do_install ;;
    uninstall) do_uninstall ;;
    all)       do_install; do_uninstall ;;
    *) echo "Usage: $0 install|uninstall|all [options]" >&2; exit 2 ;;
esac
