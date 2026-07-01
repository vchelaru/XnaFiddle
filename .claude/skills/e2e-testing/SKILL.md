---
description: How to author Playwright .NET (NUnit) end-to-end tests for the XnaFiddle Blazor WASM app — the publish→in-process-Kestrel→headless-Chromium harness, the SwiftShader GL args, the observable JS/DOM signals a test can synchronize on, and the null-and-wait per-run sync pattern. Load when adding or debugging an e2e test in XnaFiddle.E2E.Tests (e.g. issue #95 sample-switch guard), touching the SmokeTest fixture / StaticSiteHost / PublishOutput, or the e2e.yml CI job.
---

# E2E Testing

Test project: `XnaFiddle.E2E.Tests` (NUnit + `Microsoft.Playwright.NUnit`). `Nullable` is **enabled** here (unlike BlazorGL/Core — don't copy their nullable-off assumptions). `dotnet test` auto-discovers every `[Test]`, so **adding a test needs no CI edit**. Runtime domain facts (leaks, lifecycle, `UseReferenceDevice`) live in the **game-lifecycle** skill; this skill is only about authoring tests.

## The harness flow

1. `dotnet publish XnaFiddle.BlazorGL -c Release -o <dir>` produces the standalone WASM app.
2. `PublishOutput.ResolveWebRoot()` finds the served web root: honors env var `E2E_PUBLISH_DIR`, else falls back to `<repoRoot>/artifacts/e2e-publish`. Accepts either the publish root (has a `wwwroot`) or the `wwwroot` itself; throws if no `index.html`.
3. `StaticSiteHost.StartAsync(webRoot)` serves it in-process via Kestrel on an OS-assigned port (`http://127.0.0.1:0`). Uses `UseBlazorFrameworkFiles` so `_framework` Webcil assemblies get `application/wasm` (a plain static server serves octet-stream, which the browser refuses). `MapFallbackToFile("index.html")` for deep links.
4. Playwright drives headless Chromium against `host.BaseUrl`.

`OneTimeSetUp`/`OneTimeTearDown` in `SmokeTest.cs` own host + browser + page lifetime; individual tests just navigate and assert.

**Locally:** if `artifacts/e2e-publish/wwwroot/index.html` already exists, skip publish — just `dotnet test`. CI (`.github/workflows/e2e.yml`) does publish → build test proj → `playwright.ps1 install chromium` → `dotnet test --no-build`.

## Windows-only publish (CI gotcha)

The e2e CI job runs on **windows-latest**, not Linux. Reason (summarized from `e2e.yml`'s top comment): a vendored KNI csproj references `Xna.Framework.Media.csproj` but the file on disk is `XNA.Framework.Media.csproj` (uppercase). Case-insensitive Windows resolves it; case-sensitive Linux fails the publish with CS0234/CS0246. Windows is also the app's real deploy target.

## SwiftShader Chromium args (headless software GL)

CI has no GPU, so launch Chromium with ANGLE-over-SwiftShader:

```
--use-gl=angle  --use-angle=swiftshader  --enable-unsafe-swiftshader
```

`--enable-unsafe-swiftshader` is **required** on recent Chromium to permit SwiftShader for WebGL (without it, WebGL context creation is blocked). These are set in `LaunchAsync` `Args`.

## Observable signals — what to synchronize on

Prefer these existing signals; they need no app-code change. What each proves:

| Signal | Proves |
|---|---|
| `#theCanvas` present **and** `window.theInstance != null` | App booted, render bridge wired (`theInstance` set by `initRenderJS`). Boot gate. |
| `[data-testid="run-button"]` clickable | The Run/Restart button. **Same testid for both labels** (label toggles on `_game != null`). While `_isCompiling` it's replaced by a **testid-less Stop button**, so Playwright's `ClickAsync` actionability auto-wait naturally waits out the compile. |
| `window._canvasContextType === 'webgl'` (Reach) or `'webgl2'` (HiDef) | **THE success signal**: a game ran and a WebGL context initialized (Roslyn compiled + KNI ran + GL came up). Written at the **end of every successful run** in `LaunchGameFromTypeAsync` (`Index.razor.cs`); reset to `null` **only on a profile switch**. |
| `#blazor-error-ui` **not** visible | No unhandled error killed the circuit (`display:none` via CSS unless an error surfaces). Assert it stays hidden. |

## The null-and-wait per-run sync pattern (keystone)

Because `_canvasContextType` **stays set across same-profile restarts**, its mere presence can't prove that a *specific* restart finished. To gate on one run: null it before the click, then wait for the app to re-set it.

```csharp
await page.EvaluateAsync("() => { window._canvasContextType = null; }");
await ClickRunAsync();                 // auto-waits out the compile
await WaitForWebGlContextAsync();      // passes only when THIS run re-sets it
```

Deterministic, no app-code change. The restart loop in `RepeatedRestart_UnchangedSource_StaysAliveAndKeepsWebGl` is the reference example.

## When to add an app-code hook

Rule of thumb: use the existing observable signals above; add a new app debug hook **only** for input/game-state assertions the DOM can't express. Example: issue #95's planned `GetInputDebugState` (exposing `Mouse.GetState()` position / touch count via JS interop) for a deterministic input assertion instead of a flaky pixel-read. Don't add hooks for anything already observable.

## Choosing N for restart/stress loops

Chrome caps live WebGL contexts at ~16. A loop guarding the per-run context-leak fix (`UseReferenceDevice` — see **game-lifecycle**) must **exceed** that so a regression trips context loss around run ~16 and the test catches it. The existing restart test uses **20**.

## Timeouts

Existing consts are **60s** (`BootTimeoutMs`, `RunTimeoutMs`) — the WASM payload is ~15-20MB and in-browser Roslyn is slow. If flaky, **tune these up**, don't weaken the assertion.

## Related

- **game-lifecycle** — the runtime facts these tests guard (leak, restart rebuild, profile switch).
- **issue-workflow** — branch/PR process when a test lands with an issue.
