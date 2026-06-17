---
name: release
description: Cut and publish a new versioned release of this project. Infers the next semantic version from the commits since the last tag, makes sure local work is committed and pushed and the branch is rebased on origin/main, gets the release PR green and merged, then creates and pushes the annotated vX.Y.Z tag that triggers the GitHub Actions release pipeline (which builds the platform binaries and publishes the GitHub Release with auto-generated notes). Use this whenever the user says "release a new version", "cut a release", "create/push a new tag", "ship it", "publish vX.Y.Z", "tag a release", or anything that means shipping a new version of this repo — even when they don't name a version number.
---

# Release a new version

This repo releases entirely through an **annotated `vX.Y.Z` tag**. Pushing a tag that matches `v*` triggers
`.github/workflows/release.yml`, which builds the six platform binaries (NativeAOT) and publishes a GitHub
Release whose notes are auto-generated against the previous tag. There is **no manual release step** — your job
is to get `main` into the right state and push a correct tag.

Because a tag push is effectively irreversible (it kicks off a public release), the guiding principle is:
**reconcile local state, propose a concrete plan, get one confirmation, then execute.** Never tag work that
isn't committed, pushed, and merged.

## 1. Preflight

- `git fetch --tags origin`
- Confirm GitHub access: `gh auth status` (the tag push needs the Release workflow; merges/notes need `gh`).
- Note the default branch (`main`) and the current branch.

## 2. Reconcile local state — do this first, before anything else

The most common way a release goes wrong is tagging a `main` that doesn't actually contain the work, so settle
local state up front.

- **Uncommitted changes** — run `git status --porcelain`. If the working tree is dirty, **stop and ask the
  user** whether those changes should be committed and included in this release, or left out. Do not auto-commit
  and do not silently ignore them — the user is the only one who knows if that work belongs in the release.
- **Unpushed commits** — check whether the branch is ahead of its upstream (`git log @{u}..HEAD --oneline`).
  If there are local-only commits, **push them first** (`git push`). Don't tag history that only exists locally.
- **Rebase the PR branch on `origin/main`** — for any feature branch that will become a PR (or an existing PR
  branch about to be merged), rebase it so it's current and unlikely to conflict at merge time:
  `git fetch origin && git rebase origin/main`, then `git push --force-with-lease`. If the rebase hits
  conflicts, **stop and surface them to the user** — do not guess at a resolution.

## 3. Assess state

- Latest released tag: `git tag --sort=-v:refname | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | head -1`
- Commits since that tag: `git log <latest>..origin/main --oneline`
- Any open PRs to main: `gh pr list --base main --state open`

From this you know whether there's a release PR to merge first, or whether `main` already holds the unreleased
work and you only need to tag. If there are **no** commits since the last tag, warn the user — there's nothing
to release.

## 4. Infer the next version

Use the bundled script so the logic is deterministic and matches what the evals check:

```bash
.claude/skills/release/scripts/next_version.sh origin/main
```

It prints `current=`, `bump=`, and `next=`. The bump comes from conventional-commit prefixes since the last
tag: a breaking change (`type!:` or `BREAKING CHANGE`) → **major**, any `feat:` → **minor**, otherwise →
**patch**. Treat the result as a *proposal* — the user can always override with an explicit version.

## 5. Present the plan and get one confirmation

Show the user, concisely:
- the **proposed version** and the one-line reason (e.g. "minor — there's a `feat:` since v0.3.1"),
- whether a **PR will be merged first** (which one),
- the **annotated tag message** you'll use, and
- the exact **commands** you'll run.

Then wait for their OK. This single gate is the safety valve before any mutating action.

**Tag message format** (matches this repo's history):

```
vX.Y.Z — <short title of the release>

- <bullet summarising a change, synthesised from the commits>
- <bullet>
```

## 6. Execute (only after confirmation)

- If a release PR is open and intended:
  - `gh pr checks <n> --watch` — wait for green. If a check is red, **stop** and report it; never merge red.
    (Distinguish genuine failures from infra/transient ones, and from non-required checks, before deciding.)
  - `gh pr merge <n> --merge --delete-branch`
- Sync main: `git checkout main && git pull origin main`
- Tag and push:
  - Compose the annotated message with **real newlines**, not a literal `\n`. Use a heredoc (or repeated
    `-m` flags, one per paragraph), e.g.:
    ```bash
    git tag -a vX.Y.Z -m "$(cat <<'MSG'
    vX.Y.Z — <short title>

    - <bullet>
    - <bullet>
    MSG
    )"
    ```
  - `git push origin vX.Y.Z`

Never force-push a tag or move an existing one.

## 7. Report (fire-and-report)

Confirm the pipeline started and report — don't block waiting for it to finish:

- `gh run list --workflow Release --limit 1`
- Tell the user the tag is pushed, link the triggered run, and note the GitHub Release (with platform binaries
  and notes comparing against the previous tag) will publish shortly. Offer to watch it if they want.

## Safety rails (summary)

- Dirty tree → ask whether to include; never auto-commit.
- Local-only commits → push first; rebase feature branches on `origin/main` before PR/merge.
- No commits since last tag → warn, don't tag.
- Red CI → stop, don't merge.
- One explicit confirmation before merging/tagging; never force-push or rewrite published tags.
