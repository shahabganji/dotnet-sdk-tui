#!/usr/bin/env bash
set -euo pipefail

# Creates a predictable sandbox with a single-project folder and a solution folder
# so the TUI can be exercised against realistic .NET layouts.
require_command() {
  command -v "$1" >/dev/null 2>&1 || {
    printf 'Error: required command not found: %s\n' "$1" >&2
    exit 1
  }
}

info() {
  printf '==> %s\n' "$1"
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
SANDBOX_DIR="${REPO_ROOT}/test-sandbox"
SAMPLE_PROJECT_DIR="${SANDBOX_DIR}/sample-project"
SAMPLE_SOLUTION_DIR="${SANDBOX_DIR}/sample-solution"
SAMPLE_SOLUTION_FILE="${SAMPLE_SOLUTION_DIR}/SampleSolution.sln"
SAMPLE_SOLUTION_PROJECT_DIR="${SAMPLE_SOLUTION_DIR}/src/SampleProject"

main() {
  require_command dotnet

  info "Resetting sandbox under ${SANDBOX_DIR}"
  rm -rf "$SAMPLE_PROJECT_DIR" "$SAMPLE_SOLUTION_DIR"
  mkdir -p "$SANDBOX_DIR"

  info 'Creating sample project'
  dotnet new console \
    --name SampleProject \
    --output "$SAMPLE_PROJECT_DIR" \
    --force >/dev/null

  info 'Creating sample solution and project'
  dotnet new sln \
    --name SampleSolution \
    --format sln \
    --output "$SAMPLE_SOLUTION_DIR" >/dev/null

  dotnet new console \
    --name SampleProject \
    --output "$SAMPLE_SOLUTION_PROJECT_DIR" \
    --force >/dev/null

  dotnet sln "$SAMPLE_SOLUTION_FILE" add "$SAMPLE_SOLUTION_PROJECT_DIR/SampleProject.csproj" >/dev/null

  cat <<EOF

Test sandbox ready:
  - ${SAMPLE_PROJECT_DIR}
  - ${SAMPLE_SOLUTION_FILE}

Run the TUI against the sample project:
  cd "${SAMPLE_PROJECT_DIR}"
  dotnet run --project ../../src/DotnetSdkTui/DotnetSdkTui.csproj

Run the TUI against the sample solution:
  cd "${SAMPLE_SOLUTION_DIR}"
  dotnet run --project ../../src/DotnetSdkTui/DotnetSdkTui.csproj
EOF
}

main "$@"
