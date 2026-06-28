---
description: XnaFiddle's process for turning a GitHub issue into a merged PR — read the issue + its comments, load matching skills, invoke the right agent, branch, build-verify, and open the PR immediately. Load when the user references a GitHub issue (a URL or #N) and asks to work on, fix, or implement it.
---

# Issue Workflow

The flow when the user points at a GitHub issue (URL or `#N`) and asks to fix or implement it.
This grows organically — add a step or a gotcha when one actually bites; keep it lean.

## 1. Read the issue AND its comments first

Run BOTH `gh issue view <n>` (title + body) and `gh issue view <n> --comments`. Plain `view`
omits the discussion thread, and comments routinely narrow scope, add files to touch, or settle
open questions raised in the body. Read both before touching code.

## 2. Load matching skills, then invoke the agent

- Load every skill whose trigger matches the area the issue touches (see `CLAUDE.md` → *Skills*).
  If none fit, **say so briefly** and continue — don't silently skip the check.
- Announce and invoke the matching agent from `.claude/agents/` ("Invoking coder agent…") and
  re-read its file at the start of the task; long sessions drift (see `CLAUDE.md` → *Agent Workflow*).

## 3. Branch — never commit issue work to `main`

Create a branch off `main` first (`feat/…`, `fix/…`).

## 4. Build to verify

`dotnet build XnaFiddle.BlazorGL/XnaFiddle.BlazorGL.csproj`

**Stale-submodule gotcha:** the build can fail with dozens of
`CS0579: Duplicate '…AssemblyInfo' attribute` errors, all pathed under `Submodules/KniSB/**`.
These are leftover `obj`/`bin` from earlier multi-TFM builds (net45 / netstandard2.0) being
globbed into the net8.0 build — **not** your change. Clear them and rebuild:

```
find Submodules/KniSB -type d \( -name obj -o -name bin \) -prune -exec rm -rf {} +
```

This removes only generated artifacts; the submodule pointer and tracked source are untouched
(it also cleans the dirty `M Submodules/KniSB` working-tree state those artifacts cause).

## 5. Set up manual testing — prioritize testing and speed

If the change needs manual testing in the running app (most UI/behavior changes do), get the
user testing as fast as possible:

1. **Open the solution first**, before writing anything: `Start-Process "XnaFiddle.sln"` (PowerShell).
   Launch it up front so Visual Studio loads in the background while you write the steps — don't
   make the user wait on your prose before the IDE is even opening.
2. **Then write concise, numbered manual steps** — what to run, what to click, and what correct
   behavior looks like (and the failure mode the fix addresses, so the user knows what they're
   confirming).

Skip this step only when the change is genuinely untestable by hand (pure refactor, build-only,
or covered entirely by unit tests) — say so briefly instead.

## 6. Open the PR as soon as the work is finished

Don't wait for manual-test sign-off — CI runs in parallel with the user's testing. Commit →
`git push -u origin <branch>` → `gh pr create`. Put `Closes #<n>` in the PR body so the merge
auto-closes the issue. This is the standing workflow; open the PR without per-task approval.

## 7. Bundle incidental skill-file improvements

Skill tweaks made while working the issue go in the **same** PR, unless something obvious says
otherwise. These small increments are how the skills — including this one — grow.

## 8. Report the PR URL back to the user.
