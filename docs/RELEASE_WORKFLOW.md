# Release workflow: splitting PRs and tagging a phase

This documents the two-part workflow used to close out a BUILD_PLAN.md phase: splitting one
feature's changes into separate code/docs/build-plan PRs, and applying the phase's git tag once
everything has merged. Both parts are mechanical and easy to get subtly wrong (stale stashes,
tagging the wrong ref, missing a still-unchecked item in a different phase), so this records the
actual commands and the reasoning behind each one, using the Phase 7 "audit log" feature as the
worked example.

## Why PRs are split into code / docs / build-plan

Per [BUILD_PLAN.md's Git workflow section](../BUILD_PLAN.md#git-workflow): GitHub Flow,
Conventional Commits, squash-merged PRs, linear `main` history for portfolio reviewers. A single
feature's working tree often touches three different *kinds* of file at once — production code,
reference docs under `docs/`, and `BUILD_PLAN.md`'s own checklist — and bundling all three into
one commit/PR makes the history harder to scan (a reviewer looking for "what changed in the API"
has to skip past checklist checkboxes) and harder to revert independently (reverting the feature
shouldn't also revert the build-plan bookkeeping).

So each feature becomes up to three PRs, always in this order:

1. **Code** — everything except `BUILD_PLAN.md` and `docs/*.md`/`CLAUDE.md`/`README.md`. Stays on
   the feature branch (`feature/phase-N-description`) that already exists.
2. **Docs** — any `docs/*.md`, `CLAUDE.md`, or `README.md` changes. New branch off `main`:
   `docs/description`.
3. **Build plan (chore)** — `BUILD_PLAN.md` only, checking off the phase's items with notes on any
   deviations. New branch off `main`: `chore/update-build-plan-description`.

If a feature touches none of the docs/build-plan files, skip the PRs that don't apply.

## The mechanics: carrying uncommitted changes to separate branches

The code, docs, and build-plan changes are usually all sitting uncommitted in the same working
tree at once (they were written in the same session). PR 1's commit intentionally excludes the
docs and build-plan files, which leaves them **still uncommitted** after PR 1 is pushed — they
need to move to two fresh branches off `main` without being committed to the feature branch at
all.

**`git stash` by name, not `git stash pop`.** The naive approach — `git stash`, switch branches,
`git stash pop` — breaks the moment any other stash exists on the stack, because `pop` always
grabs `stash@{0}` (the most recent), not necessarily the one you just created. Instead, each group
gets its own stash with a unique, greppable message, and is restored by searching for that message
rather than by position:

```sh
# While still on the feature branch, after PR 1's commit+push:
git stash push -m "build-plan-wip:phase-7-audit-log" -- BUILD_PLAN.md
git stash push -u -m "docs-wip:phase-7-audit-log" -- docs/LOCAL_DEVELOPMENT.md
```

- `-u` on the docs stash: needed whenever the docs changes include **new, untracked** files —
  plain `git stash push` ignores untracked files by default, so without `-u` a new `docs/*.md`
  file would be silently left behind in the working tree instead of being stashed.
- The message format (`<kind>-wip:<description>`) exists purely so the restore step can find it
  unambiguously later, via a revset:

```sh
git checkout main
git checkout -b chore/update-build-plan-phase-7-audit-log
git stash apply "stash^{/build-plan-wip:phase-7-audit-log}"
git add BUILD_PLAN.md
git commit -m "chore: update BUILD_PLAN.md for Phase 7 audit log"
git stash drop "stash^{/build-plan-wip:phase-7-audit-log}"
git push -u origin chore/update-build-plan-phase-7-audit-log
```

Same pattern for the docs branch, substituting the docs stash's message and files.

**`apply` + `drop`, never `pop`, around the commit.** `stash pop` applies and drops in one step —
if `apply` hits a merge conflict, `pop` leaves the stash *already dropped* despite the conflict,
so the only copy of those changes is now sitting unresolved in the working tree with no stash
fallback. Splitting it into `apply` (fails safely, stash stays if there's a conflict) → resolve →
commit → `drop` (only once the commit has safely captured the content) means a conflict never
costs you the stash.

**Why `git stash apply "stash^{/message}"` and not `git checkout <feature-branch> -- <files>`.**
`checkout <branch> -- <file>` pulls that file as it exists in the branch's **last commit** — but
PR 1's commit on the feature branch never included `BUILD_PLAN.md`/the docs files at all (that's
the whole point of excluding them), so this would silently restore an *old*, pre-change version of
the file instead of the edits that are sitting in the stash. The stash is the only place those
uncommitted edits exist once you've switched away from the feature branch.

**When the docs/build-plan changes are already committed on the feature branch instead** (the
less common case — e.g. a prior session committed everything together), the stash dance isn't
needed at all; checking out the file from the feature branch tip is correct as-is:

```sh
git checkout main
git checkout -b docs/phase-7-audit-log
git checkout feature/phase-7-audit-log -- docs/LOCAL_DEVELOPMENT.md
git add docs/LOCAL_DEVELOPMENT.md
git commit -m "docs: ..."
git push -u origin docs/phase-7-audit-log
```

## Applying a phase tag once its PRs are merged

`BUILD_PLAN.md`'s phase tags table (e.g. `v0.7-phase7-api-advanced`) is only applied once every
checklist item *in that phase's own section* is checked off and merged to `main` — not once the
feature you were just working on is done, since a phase can have other sections that were already
complete (or that remain open) independently of the feature you just shipped.

### 1. Confirm the PRs actually merged

```sh
git fetch origin --quiet
git log origin/main --oneline -8
```

Look for the PR merge commits by number/title — don't assume a PR merged just because it was
opened; confirm it's actually in `origin/main`'s history first.

### 2. Check for unchecked items *within that phase's section only*

`BUILD_PLAN.md` has one `- [ ]` syntax for every phase, so grepping the whole file for unchecked
boxes tells you nothing about which *phase* they belong to. Scope the search to the line range
between that phase's heading and the next one, reading the **merged `origin/main` copy** of the
file (not your local working copy, which may have unrelated in-progress edits from other phases):

```sh
git show origin/main:BUILD_PLAN.md | awk '/^## Phase 7 —/{p=1} /^## Phase 8 —/{p=0} p && /^- \[ \]/'
```

No output means every item in that phase's section is checked off. Any line printed is a
remaining blocker — the tag should wait.

### 3. Tag `origin/main`, not your local `main`

```sh
git tag -a v0.7-phase7-api-advanced origin/main -m "Phase 7: API layer — advanced REST features"
git push origin v0.7-phase7-api-advanced
```

Tagging `origin/main` directly (rather than switching to local `main` and pulling first) avoids a
window where the tag could land on a local `main` that's behind the remote — the annotated tag
message mirrors the milestone name from `BUILD_PLAN.md`'s Phase tags table, so it reads the same
way on GitHub as it does in the doc.

Pushing a tag is a visible, shared action (every clone now sees it) — confirm with whoever owns
the repo before pushing, the same way you would before force-pushing or deleting a branch, even
though a tag push itself isn't destructive.
