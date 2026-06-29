---
description: GitHub issue workflow and mandatory manual-test handoff for UI/behavior changes
alwaysApply: true
---

# Issue workflow & manual testing

Full detail: `.claude/skills/issue-workflow/SKILL.md`. On GitHub issue work (`#N` or issue URL), load that skill first.

## Issue flow (summary)

1. Read issue **and** comments (`gh issue view <n> --comments`).
2. Branch off `main` (`feat/…` / `fix/…`) — never commit issue work on `main`.
3. Build: `dotnet build XnaFiddle.BlazorGL/XnaFiddle.BlazorGL.csproj`
4. **Manual testing** (below) when the change affects the running app.
5. Open PR immediately (`Closes #<n>`); don't wait for manual sign-off.

## Manual testing — do not skip or bury in the PR

If the change needs hand testing (most UI/behavior work — compile banner, export dialog, editor tabs, game run, etc.):

1. **Tell the user explicitly they should test it** — don't only list steps in the PR body.
2. **Open the solution first** (before or while writing steps), so VS loads in parallel:
   ```powershell
   Start-Process "XnaFiddle.sln"
   ```
   Startup project: **XnaFiddle.BlazorGL** → F5.
3. **Then** give concise numbered steps: what to run/click, expected behavior, and what failure looked like before.

Skip manual-test handoff only when the change is genuinely untestable by hand (pure refactor, version bump with no UI surface, etc.) — say so briefly.

## Common mistake

Opening the PR with a test-plan checkbox is **not** a substitute for telling the user to test and opening `XnaFiddle.sln` for them.
