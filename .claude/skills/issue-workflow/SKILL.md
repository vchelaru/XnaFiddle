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
   **Exception — mobile testing:** if the fix must be verified on a phone (touch UI, mobile layout,
   anything Android/iOS-specific), do **not** open the `.sln`, and do **not** suggest USB /
   `chrome://inspect` debugging — that route was whack-a-mole and the user abandoned it. Instead
   have them serve over the LAN and hit it from the phone: `dotnet run --project XnaFiddle.BlazorGL
   --urls "http://0.0.0.0:60441"`, then open `http://<machine-LAN-ip>:60441` on the phone (same
   Wi-Fi). Use plain **HTTP** (avoids the dev-cert-trust problem); it's fine unless the specific
   test needs a secure context (clipboard / CacheStorage). Find the LAN IP with `Get-NetIPAddress
   -AddressFamily IPv4` (the Wi-Fi one, not the `172.x` WSL vEthernet). If the phone can't connect,
   it's almost always Windows Firewall blocking inbound 60441 — offer to add a private-profile
   inbound rule.
2. **Then write concise, numbered manual steps** — what to run, what to click, and what correct
   behavior looks like (and the failure mode the fix addresses, so the user knows what they're
   confirming).

Skip this step only when the change is genuinely untestable by hand (pure refactor, build-only,
or covered entirely by unit tests) — say so briefly instead.

### Build marker — prove the phone is running the new build (not a stale cache)

Over plain HTTP the phone caches `index.html` **and** the compiled DLLs, and mobile Chrome has no
easy hard-reload — so a normal reload can silently serve the *previous* build and make a working
fix look broken. We've burned test cycles on exactly this. So on **every build handed off for
mobile testing**, bump a **deterministic** visual marker and tell the user the value up front:

- The marker is the **main splitter color**, driven by `var SPLITTER_COLOR` in `index.html`
  (painted onto `#splitter` by `applyLayout`). It lives in the **JS** on purpose — that's the
  cache-prone file — so a stale page shows the *previous* color.
- **Advance through this ordered palette** (never reuse the immediately-previous one; the user must
  be able to name the color at a glance): orange `#e8830c` → magenta `#d6336c` → green `#2f9e44` →
  purple `#7048e8` → teal `#0ca678` → red `#e03131` → amber `#f59f00` → (wrap). This is a *pick*,
  not randomness — state the name **and** hex in your reply so the user confirms the served build.
- **Recover "previous" from git, not memory.** The palette starts at orange, and after a context
  clear you won't remember the last marker — so a fresh agent naively re-picks orange and can reuse
  it. Don't rely on memory: resetting to `#007acc` before merge is just another commit, so git
  history permanently records every marker ever used — it's fully recoverable. Before picking, run
  `git log --all -p -G "SPLITTER_COLOR = '#" -- XnaFiddle.BlazorGL/wwwroot/index.html` and read the
  most recent added (`+`) value that is **not** `#007acc`; advance to the *next* palette entry after
  it. Use **`-G`, not `-S`**: the pickaxe `-S` only fires when the *count* of the match changes, and
  since every revision has exactly one `SPLITTER_COLOR` line it reports only the commit that first
  *added* the line (always orange) — it silently misses every later color change. `-G` matches any
  diff line hitting the regex, so it catches all of them. This makes the choice stateless and
  reproducible across context clears.
- Tell the user to load in a **fresh Incognito tab** (guarantees a clean fetch) and check the
  splitter is the color you named **before** re-running the test. Wrong color = stale cache, not a
  failed fix.
- **Before the PR merges, reset `SPLITTER_COLOR` to the canonical accent `#007acc`** so `main`
  doesn't ship a random marker color.

## 6. Open the PR as soon as the work is finished

Don't wait for manual-test sign-off — CI runs in parallel with the user's testing. Commit →
`git push -u origin <branch>` → `gh pr create`. Put `Closes #<n>` in the PR body so the merge
auto-closes the issue. This is the standing workflow; open the PR without per-task approval.

## 7. Bundle incidental skill-file improvements

Skill tweaks made while working the issue go in the **same** PR, unless something obvious says
otherwise. These small increments are how the skills — including this one — grow.

## 8. Report the PR URL back to the user.
