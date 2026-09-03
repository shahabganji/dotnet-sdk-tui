#!/usr/bin/env bash
set -eo pipefail

output_file="${1:-notes.md}"
repo="${GITHUB_REPOSITORY:-}"

current="${GITHUB_REF_NAME:-}"
if [[ -z "$current" ]]; then
  current="$(git tag --points-at HEAD | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | head -1 || true)"
fi

if [[ -z "$current" ]]; then
  echo "Could not determine current tag (set GITHUB_REF_NAME or run on a tagged commit)." >&2
  exit 1
fi

previous="$(git tag --sort=-v:refname | awk -v c="$current" 'p==1{print;exit} $0==c{p=1}')"

if [[ -n "$previous" ]]; then
  range="${previous}..${current}"
else
  range="$current"
fi

trim() {
  local s="$1"
  s="${s#"${s%%[![:space:]]*}"}"
  s="${s%"${s##*[![:space:]]}"}"
  printf '%s' "$s"
}

sentence_case() {
  local s="$1"
  if [[ -z "$s" ]]; then
    return
  fi
  local first="${s:0:1}"
  local rest="${s:1}"
  printf '%s%s' "$(printf '%s' "$first" | tr '[:lower:]' '[:upper:]')" "$rest"
}

clean_subject() {
  local subject="$1"
  local cleaned
  cleaned="$(printf '%s' "$subject" | sed -E 's/^[a-zA-Z]+(\([^)]*\))?!?:[[:space:]]*//')"
  cleaned="$(trim "$cleaned")"
  if [[ -z "$cleaned" ]]; then
    cleaned="$subject"
  fi
  sentence_case "$cleaned"
}

declare -a highlights=()
declare -a fixes=()
declare -a maintenance=()
declare -a others=()
declare -a breaking=()

while IFS= read -r subject; do
  subject="$(trim "$subject")"
  [[ -z "$subject" ]] && continue
  [[ "$subject" =~ ^Merge[[:space:]]pull[[:space:]]request[[:space:]]#[0-9]+ ]] && continue

  clean="$(clean_subject "$subject")"

  if printf '%s\n' "$subject" | grep -Eq '^[a-zA-Z]+(\([^)]*\))?!:'; then
    breaking+=("$clean")
    continue
  fi

  if printf '%s\n' "$subject" | grep -Eq '^feat(\([^)]*\))?:'; then
    highlights+=("$clean")
  elif printf '%s\n' "$subject" | grep -Eq '^(fix|perf|refactor|revert)(\([^)]*\))?:'; then
    fixes+=("$clean")
  elif printf '%s\n' "$subject" | grep -Eq '^(docs|chore|test|build|ci)(\([^)]*\))?:'; then
    maintenance+=("$clean")
  else
    others+=("$clean")
  fi
done < <(git log "$range" --format='%s')

has_breaking_body="false"
if git log "$range" --format='%b' | grep -qiE 'BREAKING[ -]CHANGE'; then
  has_breaking_body="true"
fi

total_items=$(( ${#highlights[@]} + ${#fixes[@]} + ${#maintenance[@]} + ${#others[@]} + ${#breaking[@]} ))

fallback_to_github_generated() {
  if [[ -z "$repo" ]]; then
    return 1
  fi
  if [[ -n "$previous" ]]; then
    gh api "repos/${repo}/releases/generate-notes" \
      -f tag_name="$current" \
      -f previous_tag_name="$previous" \
      --jq .body > "$output_file"
  else
    gh api "repos/${repo}/releases/generate-notes" \
      -f tag_name="$current" \
      --jq .body > "$output_file"
  fi
}

if [[ "$total_items" -eq 0 ]]; then
  if ! fallback_to_github_generated; then
    {
      echo "## What's Changed"
      echo "- Release $current"
      echo
      if [[ -n "$repo" && -n "$previous" ]]; then
        echo "**Full Changelog**: https://github.com/${repo}/compare/${previous}...${current}"
      elif [[ -n "$repo" ]]; then
        echo "**Release**: https://github.com/${repo}/releases/tag/${current}"
      fi
    } > "$output_file"
  fi
  exit 0
fi

{
  if [[ ${#highlights[@]} -gt 0 ]]; then
    echo "## Highlights"
    for item in "${highlights[@]}"; do
      echo "- $item"
    done
    echo
  fi

  if [[ ${#fixes[@]} -gt 0 || ${#others[@]} -gt 0 ]]; then
    echo "## Fixes & Improvements"
    for item in "${fixes[@]}"; do
      echo "- $item"
    done
    for item in "${others[@]}"; do
      echo "- $item"
    done
    echo
  fi

  if [[ ${#maintenance[@]} -gt 0 ]]; then
    echo "## Maintenance"
    for item in "${maintenance[@]}"; do
      echo "- $item"
    done
    echo
  fi

  if [[ ${#breaking[@]} -gt 0 || "$has_breaking_body" == "true" ]]; then
    echo "## Breaking Changes / Upgrade Notes"
    for item in "${breaking[@]}"; do
      echo "- $item"
    done
    if [[ "$has_breaking_body" == "true" && ${#breaking[@]} -eq 0 ]]; then
      echo "- One or more commits include BREAKING CHANGE notes in commit bodies. Review the full changelog before upgrading."
    fi
    echo
  fi

  if [[ -n "$repo" && -n "$previous" ]]; then
    echo "**Full Changelog**: https://github.com/${repo}/compare/${previous}...${current}"
  elif [[ -n "$repo" ]]; then
    echo "**Release**: https://github.com/${repo}/releases/tag/${current}"
  fi
} > "$output_file"
