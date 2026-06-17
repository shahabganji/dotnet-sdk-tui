#!/usr/bin/env bash
#
# Infer the next semantic-version tag from the commits since the latest vX.Y.Z tag,
# using conventional-commit prefixes.
#
#   breaking ( type!:  or  "BREAKING CHANGE" in body )  -> major
#   feat:                                               -> minor
#   anything else (fix/chore/docs/refactor/…)           -> patch
#
# Usage:  next_version.sh [<base-ref>]      (default base-ref: origin/main)
# Output (stdout, key=value lines so callers can parse deterministically):
#   current=<latest tag or empty>
#   bump=<major|minor|patch|initial>
#   next=<vX.Y.Z>
#   count=<number of commits since the latest tag>

set -euo pipefail

base="${1:-origin/main}"

latest="$(git tag --sort=-v:refname | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | head -1 || true)"

if [[ -z "$latest" ]]; then
  # No release tag yet — start the series.
  echo "current="
  echo "bump=initial"
  echo "next=v0.1.0"
  echo "count=$(git rev-list --count "$base" 2>/dev/null || echo 0)"
  exit 0
fi

version="${latest#v}"
IFS='.' read -r major minor patch <<<"$version"

range="${latest}..${base}"
count="$(git rev-list --count "$range" 2>/dev/null || echo 0)"

subjects="$(git log "$range" --format='%s' 2>/dev/null || true)"
bodies="$(git log "$range" --format='%b' 2>/dev/null || true)"

bump="patch"
if printf '%s\n' "$subjects" | grep -qE '^[a-zA-Z]+(\([^)]*\))?!:' \
   || printf '%s\n' "$bodies" | grep -qiE 'BREAKING[ -]CHANGE'; then
  bump="major"
elif printf '%s\n' "$subjects" | grep -qE '^feat(\([^)]*\))?:'; then
  bump="minor"
fi

case "$bump" in
  major) next="v$((major + 1)).0.0" ;;
  minor) next="v${major}.$((minor + 1)).0" ;;
  patch) next="v${major}.${minor}.$((patch + 1))" ;;
esac

echo "current=$latest"
echo "bump=$bump"
echo "next=$next"
echo "count=$count"
