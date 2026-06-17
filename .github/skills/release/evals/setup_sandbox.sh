#!/usr/bin/env bash
#
# Build a throwaway git repo fixture for DRY-RUN testing of the release skill.
# Each fixture has a local `file://` bare remote, so `git push` is safe and no
# GitHub call is ever made. Use it to exercise version inference + local-state
# handling without cutting a real release.
#
# Usage:  setup_sandbox.sh <scenario> <target-dir>
# Scenarios:
#   patch     fix/docs commits since v1.2.3            -> expect next v1.2.4
#   minor     a feat: commit since v1.2.3              -> expect next v1.3.0
#   major     a feat!: (breaking) commit since v1.2.3  -> expect next v2.0.0
#   dirty     feat pushed, then uncommitted edits      -> skill must ASK about them
#   unpushed  a feat: commit that is NOT pushed        -> skill must push first
#   clean     no commits since v1.2.3                  -> skill must warn (nothing to release)

set -euo pipefail
scenario="${1:?scenario required}"
dir="${2:?target dir required}"

remote="${dir}.remote.git"
rm -rf "$dir" "$remote"
git init -q --bare "$remote"
git init -q -b main "$dir"

cd "$dir"
git config user.email test@example.com
git config user.name "Test User"
git config commit.gpgsign false
git remote add origin "$remote"

# Seed history + a base release tag, pushed to the remote.
printf 'v1\n' > app.txt
git add -A && git commit -q -m "chore: initial commit"
git tag -a v1.2.3 -m "v1.2.3 — base release"
git push -q -u origin main
git push -q origin v1.2.3

case "$scenario" in
  patch)
    printf 'a\n' >> app.txt && git commit -qam "fix: correct an off-by-one"
    printf 'b\n' >> app.txt && git commit -qam "docs: clarify usage"
    git push -q origin main ;;
  minor)
    printf 'a\n' >> app.txt && git commit -qam "feat: add export command"
    printf 'b\n' >> app.txt && git commit -qam "fix: small fix"
    git push -q origin main ;;
  major)
    printf 'a\n' >> app.txt && git commit -qam "feat!: redesign the CLI surface"
    git push -q origin main ;;
  dirty)
    printf 'a\n' >> app.txt && git commit -qam "feat: add export command"
    git push -q origin main
    printf 'uncommitted work\n' >> app.txt ;;        # leave working tree dirty
  unpushed)
    printf 'a\n' >> app.txt && git commit -qam "feat: add export command" ;;  # committed, not pushed
  clean)
    : ;;                                              # nothing since the tag
  *)
    echo "unknown scenario: $scenario" >&2; exit 2 ;;
esac

echo "$dir"
