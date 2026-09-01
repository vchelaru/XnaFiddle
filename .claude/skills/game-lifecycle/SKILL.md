---
description: The compile-to-run pipeline and WebGL/GraphicsDevice resource lifecycle in XnaFiddle — how a Run rebuilds the game, the per-run WebGL context leak and its UseReferenceDevice fix, Roslyn metadata-reference caching, and documented dead-ends. Load when working on DoCompileAndRun, game restart/lifecycle, GraphicsDevice/WebGL/canvas issues, mobile restart crashes / touch UI starvation (issue #90 — Window.Current event leak, touch-toolbar bypass), diagnosing console-less mobile WASM crashes (Mono aborts, Debug.Assert vs native assertions), render targets / mipmapped RenderTarget2D / explicit-LOD (SampleLevel) sampling / multi-texture effects, "Shader Compilation Failed"/CONTEXT_LOST_WEBGL/"Too many active WebGL contexts"/"texParameter: no texture bound" crashes, compile performance, or CompilationService.
---

# Game Lifecycle

## The Run flow (`Index.razor.cs` -> `DoCompileAndRun`)

`CompileAndRun()` sets `_pendingCompile`; `TickDotNet()` (driven by JS requestAnimationFrame) picks it up and calls `DoCompileAndRun()`, so compilation runs in the rAF context rather than the Blazor sync context — this avoids a `Monitor` deadlock on the single WASM thread. Order inside `DoCompileAndRun`:

1. Get C# source from Monaco; `CompilationService.CompileAsync` (Roslyn) -> IL bytes.
2. `Assembly.Load(result.ILBytes)`; `FindGameType` locates the `Game` subclass.
3. `CompileRegisteredShadersAsync()` — compiles any `.fx` tabs to `.mgfx` via ShadowDusk. No-ops (and never downloads the ~17 MB DXC wasm) when no shader tabs exist. This `await` is **outside** the synchronous swap window below, on purpose.
4. Drop the old game: `_game = null` (NOT `Dispose()` — see below), then `LibraryRegistry.RunAllCleanups()`.
5. `GraphicsAdapter.UseReferenceDevice = true` (the leak fix — see below), then `Activator.CreateInstance(gameType)`, then `newGame.Run()` which creates the GraphicsDevice + WebGL context.

The window between `_game = null` and `_game = newGame` is deliberately await-free. WASM is single-threaded, so the only interleaving points are `await` boundaries; with none in that window, `TickDotNet()` cannot run mid-swap and its `if (_game == null) return;` guard is sufficient (no locking needed). The one `await Task.Delay(1)` before `Run()` only flushes the "Loading game..." status to the DOM.

## Each Run rebuilds the whole game

A fresh `Game`/`GraphicsDevice` is created every Run. The old game is dropped **without** `Dispose()` on purpose: disposing the old `GraphicsDevice` invalidates textures that the Gum UI library caches in static fields, breaking Gum on the next run. GC reclaims the old game eventually — acceptable in a fiddle.

`LibraryRegistry.RunAllCleanups()` (`XnaFiddle.Core/LibraryRegistry.cs`) calls `CleanUp()` on every plugin to reset static state between runs. Notably `GameWindowPlugin` (`XnaFiddle.Core/Plugins/GameWindowPlugin.cs`) reflectively clears KNI's static `BlazorGameWindow._instances` dictionary (the type lives in the browser-only KNI assembly, so it is resolved by name, not a compile-time reference).

## WebGL context model (what leaks and what does not)

| Fact | Where |
|---|---|
| `theCanvas` is a single shared DOM `<canvas>` in `Index.razor` markup. | `Index.razor` |
| KNI's `BlazorGameWindow` ctor resolves it per game via `Document.GetElementById<Canvas>("theCanvas")`. | `BlazorGameWindow.cs:114` |
| `Document.GetElementById` **caches** the managed `Canvas` wrapper by id (a `WeakReference` in `_elementsCache`). | `Wasm.Dom/Dom/Document.cs` (`GetElementById`/`FromId`) |
| `canvas.getContext(type)` on the same DOM element returns the same underlying WebGL context; KNI's `Canvas.GetContext` also caches the context per wrapper instance. | `Wasm.Canvas/Canvas/Canvas.cs` (`GetContext`) |

Conclusion: `theCanvas` does **not** leak contexts across runs — it reuses one.

## Profile switch (Reach<->HiDef) — canvas swap, no reload (issue #25)

A canvas's context **type** (webgl vs webgl2) locks on first `getContext` and can't change in place, so a Reach<->HiDef switch needs a brand-new `<canvas>` element. This is detected in `DoCompileAndRun` (`_canvasProfile` vs `GetGameProfile(newGame)`) and handled **without a page reload**:

1. Drop the just-created (mismatched) game; `LibraryRegistry.RunAllCleanups()`.
2. `LibraryRegistry.ClearCanvasElementCache()` — see below.
3. Bump `_canvasGen` (the canvas has `@key="_canvasGen"` in `Index.razor`) + `StateHasChanged()` + `await Task.Delay(1)` so **Blazor** recreates the `<canvas>` (Blazor owns the DOM swap — don't do it with raw JS, that desyncs the renderer).
4. `setupCanvas` (JS) re-wires the fresh element; `window._canvasContextType = null` (it's unbound).
5. Rebuild the game (`Activator.CreateInstance` again, re-assign `InMemoryContentManager`) so its `BlazorGameWindow` binds the fresh canvas; fall through to `Run()`.

The critical enabler: `GameWindowPlugin.ClearCanvasElementCache()` reflectively clears KNI's `Document._elementsCache`. Without that, `GetElementById("theCanvas")` returns the **stale cached `Canvas` wrapper** pointing at the detached old element -> black screen (this is what broke the earlier per-run canvas-swap attempt). Profile switches are rare (examples are all HiDef), so the double game-construction on a switch is fine.

**`ClearCanvasElementCache()` is deliberately NOT part of `CleanUp()`/`RunAllCleanups()`.** KNI's `Document.GetElementById<T>` builds a **brand-new** `Canvas` wrapper on any cache miss (`Document.cs` `CreateInstance<TElement>`), and `Canvas.GetContext<T>()` caches its result **per wrapper instance**. So clearing `_elementsCache` on an ordinary restart (same `<canvas>`) forces a fresh wrapper to re-call the JS `getContext(...)` bridge — which, for an unchanged canvas, hands back the browser's already-registered context object, and `nkJSObject.RegisterObject` (all `nkast.Wasm.JSInterop` versions, 8.0.x through 10.0.x — not a KNI-bump regression) has **no "already registered" guard**, throwing `"object already registered"` inside `Activator.CreateInstance(gameType)`. Only call this on an actual canvas swap.

## THE per-run leak and the fix

KNI's `ConcreteGraphicsAdapter.Platform_IsProfileSupported(HiDef)` (`Platforms/Graphics/.BlazorGL/ConcreteGraphicsAdapter.cs:120`) validates HiDef by creating a throwaway `new OffscreenCanvas(1,1)` and calling `GetContext<IWebGL2RenderingContext>()`. The OffscreenCanvas wrapper IS wrapped in a `using` block, but the returned WebGL2 context is a **separate JSObject constructed with a null parent**, so disposing the OffscreenCanvas never frees it — its handle stays pinned in the JS `nkJSObject` registry. So **every HiDef game init leaks one WebGL2 context**. After ~10-16 runs Chrome hits its context cap (~16), force-loses the oldest, and the next game's device setup / SpriteEffect compile fails with `CONTEXT_LOST_WEBGL` or `InvalidOperationException: Shader Compilation Failed.` — often with an **empty GLSL info log**, the signature of context loss (not bad shader code). XnaFiddle examples are HiDef, so normal use hits this.

**Diagnostic.** Console warning `WARNING: Too many active WebGL contexts. Oldest context will be lost.` plus which `getContext` it points at. The probe uses the no-attribs path `OffscreenCanvas.GetContext<IWebGL2>()` -> `nkCanvas.CreateWebGL2Context` (`OffscreenCanvas.cs:81`). The game's real device uses the **with-attribs** path `nkOffscreenCanvas.CreateWebGL2Context1` (`OffscreenCanvas.cs:147`). A warning at the no-attribs `nkCanvas.CreateWebGL2Context` site = the **probe** leaking, not the game canvas.

**The fix** (`DoCompileAndRun`, set before the game's device is created):

```csharp
GraphicsAdapter.UseReferenceDevice = true;
```

In the BlazorGL platform this flag is read in **exactly one place** — the `Platform_IsProfileSupported` short-circuit `if (GraphicsAdapter.UseReferenceDevice) return true;` (`ConcreteGraphicsAdapter.cs:122`) — so it skips the OffscreenCanvas probe and has no other BlazorGL effect. The real device still gets a genuine WebGL2 context when it runs. KNI's own test runner sets the same flag (`Tests/Runner/GameBase/TestGameBase.cs:37`). Verified by grep: `UseReferenceDevice` has no other BlazorGL reference (other hits are the property definition, the other platforms' adapters, and the test runner). Trade-off: HiDef "supported" is no longer empirically probed; a browser truly lacking WebGL2 would fail at device creation with a clear error instead — acceptable, every browser running this app has WebGL2.

## Compile pipeline & performance (`CompilationService.cs`)

`CompileAsync` = parse -> `GetMetadataReferencesAsync` -> `CSharpCompilation.Create` -> `Emit`. `concurrentBuild: false` because parallel Roslyn workers deadlock on `Monitor.Wait` in single-threaded WASM.

`GetMetadataReferencesAsync` resolves ~40+ assemblies (BCL + KNI + active plugins) via `BlazorWasmMetadataReferenceService`, which re-parses each PE's metadata — historically the dominant cost. Cross-compile cache rules (`_referenceCache`, a persistent `Dictionary<string, MetadataReference>` keyed by assembly name):

- **Successes only** are cached; failures stay uncached and retry next compile, so an assembly loaded later isn't permanently hidden.
- The reference service is reconstructed **only when there are cache misses** (warm compiles create no service and re-parse nothing).
- `"UserAssembly"` is always excluded so user code never enters the cache.

Reusing the same `MetadataReference` instances also lets Roslyn reuse decoded symbol tables across compilations, helping `Emit` too. `IntellisenseService` shares `GetMetadataReferencesAsync`, so its completion surface matches the compile surface and it benefits from the same cache.

Warm result: reference resolution ~85ms (all cached), `Emit` ~600ms — **emit now dominates** (the cache turned the reference step from the bottleneck into a rounding error). The user-facing "Compiled in Xs" message in `DoCompileAndRun` reflects the total.

## Touch UI starvation (issue #90) and touch input isolation (issue #107)

Blazor WASM is single-threaded. `index.html` `tickJS` calls `TickDotNet` synchronously on every rAF frame; uncapped FPS after Run can starve Blazor `@onclick` on touch devices.

KNI installs `window`-level touch listeners on the first game run that call `preventDefault()` unconditionally and are never removed — this kills the synthesized `click` for every DOM element on touch, page-wide, from then on. `wireTouchInputIsolation()` in `index.html` fixes this globally: a `document`-level bubble-phase listener strips `touchstart/move/end/cancel` from every element **except** `theCanvas` (identity check) before KNI's `window` listeners see it, and toggles `window._uiTouchActive` to pause the game step for the tap's duration. Because isolation is page-wide, any element — toolbar buttons included — works with a plain `@onclick` on touch; no per-element opt-in is needed. (The earlier per-element `data-touch-action`/`wireTouchToolbar` bypass this replaced is gone from the code — don't look for it.)

### Overlays/modals over a running game on touch (issue #94)

Two independent traps for any Blazor overlay (the Examples modal) that must work on touch while a game runs:

1. **A `[JSInvokable]` reached via the `data-touch-action` bypass does NOT auto-render.** Blazor only re-renders after `@onclick`/`EventCallback`, not after an arbitrary `invokeMethodAsync`. The Examples button opened the modal by setting a field with no `StateHasChanged()`, so the modal never appeared (it only showed when some *other* render — e.g. a Run — flushed the pending state; hence "opened once out of many taps"). **Any Trigger\* method that mutates UI state must call `StateHasChanged()` itself.**

2. **KNI's `window` touch listeners suppress the synthesized `click`.** `Wasm.Dom/.../Window.8.0.10.js` adds `window` `touchstart`/`touchmove`/`touchend` listeners that call `event.preventDefault()` **unconditionally** for every touch on the page — installed on the first game and **never removed, so the suppression is permanent for the page once any game has run** (this is why the modal breaks specifically "after the first launch"). Chrome makes `window` `touchstart`/`touchmove` **passive**, so their `preventDefault` is *ignored* (overlay list scrolling still works) — but `touchend` is **not** passive, so its `preventDefault` **is** honored and kills the compatibility `click`. Result: overlay buttons highlight on tap (`:active` is a passive touchstart effect) but their `@onclick` never fires. **Fix:** stop touch from reaching KNI's `window` listeners via a plain-JS **document-level bubble** listener (`wireExampleModalTouchIsolation` in `index.html`) that calls `stopPropagation()` for any touch whose `target.closest('#exampleModalOverlay')` matches. `document` sits below `window` in the bubble path, so it strips the event before KNI's `window` listeners fire — guaranteed by DOM ordering. **Stop all four phases, not just touchend** — swallowing only the release while KNI still sees the press leaves a dangling `nativeTouchId` (the #90 bug-2 crash); never `preventDefault`, so list scrolling is unaffected. This restores plain `@onclick` for the modal's close/card/category controls, so — unlike the toolbar — they need **no** `data-touch-action` (which would also swallow `touchmove` and kill list scrolling). **Dead end:** the Blazor `@ontouch*:stopPropagation="true"` modifier on the overlay did **not** work — a standalone touch modifier apparently never attaches a listener, so nothing stripped the event. Use the explicit JS listener, not the modifier.

Separately, the uncapped loop still *starves* those `@onclick`s (bug 2 fixes suppression, not starvation). **Fix:** a touch-only game-step pause — `tickJS` keeps the rAF loop + canvas sync alive but skips `TickDotNet` while `window._tickSuspended` (modal open) or `window._uiDragging` (splitter drag) is set, gated on `window._touchDevice` so desktop never freezes the game behind the modal. The splitter drag (pure-JS `pointermove` doing `applyLayout`) starves the same way on touch during a run; the same `_uiDragging` pause makes it track the finger. Resume on modal close / example-select / drag-end.

**Restart memory (mobile):** WASM cannot unload assemblies. Each unchanged Restart used to `Assembly.Load` again until mobile OOM'd (~3rd run). **Fix:** `CompileFingerprint` (C# + shader tabs) caches the loaded `Type`; unchanged restarts skip Roslyn + `Assembly.Load` and only tear down + `Activator.CreateInstance` the cached type. Desktop tolerated redundant loads; mobile did not.

### The mobile restart CRASH — two distinct bugs (both fixed)

The starvation/memory fixes above did **not** stop the crash. It was finally root-caused to two unrelated bugs; FPS throttling and the WebGL-context-leak hypothesis were both wrong (see dead ends).

**Bug 1 — `Window.Current` input-event subscription leak (the primary fix).** `nkast.Wasm.Dom.Window.Current` is a page-lifetime singleton; its input events are plain **public multicast-delegate fields** (`Wasm.Dom/Dom/Window.cs` — `OnResize/OnFocus/OnBlur`, `OnMouse*`, `OnKey*`, `OnTouchStart/Move/End/Cancel`, `OnGamepad*`). Every game's `BlazorGameWindow` ctor does `_window.OnTouchStart += closure` (etc.) with `_window = Window.Current` (`BlazorGameWindow.cs`). Old games are dropped without `Dispose()` (intentional — see "Each Run rebuilds"), so these closures **accumulate one set per Run** and are never removed. A single touch fires the whole multicast delegate (`Window.cs` `JsWindowOnTouchStart` → `handler(...)`); stale closures from dead games reach into a torn-down `TouchPanel.Current` strategy and trip a **native Mono runtime assertion** (`mono/metadata/class-accessors.c:92`) → `abort()` → `exit(1)` → dead app. Mobile-only (touch path; desktop mouse tolerates 50+ restarts); scales with restart count (died ~2nd-3rd restart). **Fix:** `GameWindowPlugin.CleanUp()` now nulls every public delegate field on `Window.Current` by reflection (in addition to its existing `_instances` / `Document._elementsCache` clears). `CleanUp` runs before the next game's ctor re-subscribes → a single subscriber per Run.

**Bug 2 — touch-toolbar bypass swallowed `touchend`, desyncing KNI's TouchPanel (DEBUG-only).** The original #90 bypass intercepted only capture-phase `touchend` on `[data-touch-action]` buttons and called `stopImmediatePropagation()`. But `touchstart` still reached KNI's window listener → `AddPressedEvent(id)` registered the press; the swallowed `touchend` meant `AddReleasedEvent(id)` never fired → a dangling `nativeTouchId`. The next tap reusing that id hit `TouchPanelStrategy.AddPressedEvent`'s `Debug.Assert("nativeTouchId already registered")` (`ConcreteTouchPanel.cs` / `TouchPanelStrategy.Legacy*.cs`). **`Debug.Assert` is `[Conditional("DEBUG")]` → stripped in Release**, so this crash is **dev-server only** (`dotnet run`); a published build would leave a harmless phantom stuck touch instead. **Fix (superseded by #107):** the original per-element bypass stopped all four touch phases for `[data-touch-action]` targets. `wireTouchInputIsolation()` now does the same thing page-wide (see above) — press/release stay balanced for every element, not just opted-in ones.

## Diagnosing mobile WASM crashes (no console)

The playbook that cracked #90 — mobile has no usable console:

- **`Console.WriteLine` is invisible.** Build a **pure-JS** on-screen ring-buffer panel that writes to the DOM directly (not via Blazor) so it survives a dead Blazor circuit (the failure mode kills the circuit).
- **Mono/Emscripten aborts throw a plain object / `ExitStatus` / number**, not an `Error` — `e.message` stringifies to `[object Object]`. Unwrap `name`/`message`/`status`/`stack` for the real reason.
- The real abort reason usually prints via `console.error` **before** the `ExitStatus` throw → tee `console.warn`/`console.error` into the panel; `window.onerror`/`unhandledrejection` alone miss it.
- **WebGL context loss is observable without a debugger** via **capture-phase** `webglcontextlost`/`webglcontextrestored`/`webglcontextcreationerror` listeners on `window` (these events don't bubble; `webglcontextcreationerror.statusMessage` carries "Too many active WebGL contexts").
- **Distinguish crash classes:** a **native Mono assertion** (`class-accessors.c`, `loader.c`, …) fires in Release too — a real runtime-invariant violation. A managed **`Debug.Assert` is DEBUG-only** (stripped in Release) — check the build config before treating a dev-server crash as production-affecting.
- **Stale-cache verification needs a *deterministic* build marker** (fixed value bumped per edit), not a random one — random only proves the RNG ran, not which build is served. No service worker here; staleness is plain HTTP browser cache. On **mobile** this bites hard: the phone caches `index.html` *and* the DLLs and Chrome has no easy hard-reload, so a normal reload silently serves the previous build and a working fix looks broken (we burned multiple cycles on this in #94). The concrete protocol — a `SPLITTER_COLOR` marker in `index.html` painted onto `#splitter`, an ordered palette, and loading in a **fresh Incognito tab** to force a clean fetch — lives in the **issue-workflow** skill ("Build marker"). Put the marker in the **JS** (`index.html`), not the DLL: the JS is the cache-prone file, so a stale page shows the old value. (The in-app ForceRefresh "Refresh" button also throws over plain HTTP — `caches`/CacheStorage requires a secure context.)
- **`chrome://inspect` gotcha:** do **not** run a standalone `adb` server alongside Chrome's bundled ADB — they fight over the device and `inspect` hangs / device shows "Offline (pending authentication)". Chrome's ADB uses its own RSA key, so authorizing standalone adb doesn't authorize Chrome's.

## Why the leak "suddenly appeared"

It is pre-existing in KNI and independent of any XnaFiddle change. It surfaced only after the metadata-reference cache made compiles fast (~0.7s vs several seconds): fast iteration means a user naturally does 10+ Runs in one page session before refreshing, which is what reaches the context cap. It is **not** a GC-churn regression (see dead end #1).

## Render targets (WebGL)

- **Mipmapped render targets work.** `new RenderTarget2D(gd, w, h, mipMap:true, …)` builds a real mip chain — KNI runs `GL.GenerateMipmap` on `SetRenderTarget(null)` whenever the target's `LevelCount > 1` — and a shader reads explicit levels via `Texture.SampleLevel` (verified incl. screen-sized NPOT under WebGL2). (Earlier "mipmaps are flaky on GL" lore did **not** hold for this KNI+WebGL2 stack.)
- **Multi-texture effect + resize = crash.** An `Effect` sampling a *second* texture (bound via an Effect parameter, e.g. a bloom combine reading the scene as `BaseTexture`) leaves that texture in a `GraphicsDevice.Textures[]` slot. Disposing that render target on resize **without clearing the slot first** throws WebGL `INVALID_OPERATION: texParameter: no texture bound to target`: `ConcreteGraphicsContext.PlatformApplyTexturesAndSamplers` only re-binds *dirty* slots but applies sampler state to *every populated* slot, so the next single-texture pass hits the now-empty unit. **Fix:** null the used `GraphicsDevice.Textures[i]` before disposing the targets in your resize/`EnsureRenderTargets` path.

## Dead ends — DO NOT retry

1. **`GC.Collect()` / `GC.WaitForPendingFinalizers()` to reclaim leaked GL resources.** Useless: the leaked WebGL contexts are JS-side objects pinned in the `nkJSObject` registry, not .NET objects — GC can't touch them. (KNI's `GraphicsResource` finalizer does delete GL handles, but that was never the leak.)
2. **Recreating `theCanvas` *every run* to fix the leak.** Pointless: `theCanvas` was never the leak (the OffscreenCanvas probe is — use `UseReferenceDevice`). Recreating it per run only adds the swap's failure modes. (Recreating it *on a profile switch* is correct — see that section — but only because two specific things are handled: clear `Document._elementsCache` so the stale `Canvas` wrapper isn't reused (else black screen), and recreate via Blazor `@key` rather than raw JS removal (else Blazor DOM-diff desync). Doing a raw JS swap without those two is the dead end.)
3. **`WEBGL_lose_context.loseContext()` on the old context.** In Chrome this can reset the whole GPU process; a context created in the same synchronous turn comes up already-lost -> `CONTEXT_LOST_WEBGL` on the new device's first GL call.
4. **FPS throttling to fix the #90 restart crash.** The 10/20fps full-editor cap tried during #90 did **not** fix the crash and was unrelated to frame rate — removed. The toolbar-unresponsive symptom is fixed by the `touchend` bypass, not a frame cap. (Embed-mode 20fps mobile / 30fps desktop is a separate, intentional, pre-existing cap — keep it.)
5. **Assuming every mobile restart crash is the WebGL context leak.** It was the leading #90 hypothesis but was **disproven** — no `webglcontext*` events fired. The actual #90 crash was the `Window.Current` subscription leak. The context-exhaustion crash is real but is a *different* bug, already covered by the `UseReferenceDevice` fix above.

## KniSB submodule pin: stay on `Work-In-Progress-(Gumknix)`

`.gitmodules` declares `branch = Work-In-Progress-(Gumknix)` for `Submodules/KniSB`, but that field is only a hint — nothing stops the pinned commit from actually sitting on a different branch of `squarebananas/kniSB`. **Landmine:** squarebananas' `main` branch lacks a Gumknix-only fix (commit `b2c25f8c22104aa090483f44cd3455e94b7431b4`, "Fixes updating ClientBounds & mouse/touch positions") that makes `BlazorGameWindow.ClientBounds` track the canvas's real on-page position via `_canvasHolder.GetBoundingClientRect()`. Without it, `ClientBounds` is hardcoded to `(0, 0, canvas.Width, canvas.Height)`, so `ConcreteMouse.cs`/`ConcreteTouchPanel.cs` treat every click/touch as relative to the browser window instead of the canvas — visibly wrong whenever the canvas doesn't fill the window, which XnaFiddle's editor-pane layout always does. Gumknix is ~168 commits behind `main` (unrelated upstream fixes) but builds and runs clean under net10.0. **Do not bump the submodule pin to a commit off Gumknix** without first confirming the target still has this fix (or that squarebananas has merged Gumknix into `main`).

## Key files

| File | Role |
|---|---|
| `XnaFiddle.BlazorGL/Pages/Index.razor.cs` | `DoCompileAndRun`, `CompileAndRun`, `TickDotNet`, swap window, `UseReferenceDevice` fix, `_canvasProfile`/`PromptProfileSwitch` |
| `XnaFiddle.BlazorGL/wwwroot/index.html` | `tickJS` rAF loop, `_tickInterval` FPS cap (embed only: 20fps mobile / 30fps desktop), `wireTouchInputIsolation` — page-wide touch bypass for every non-canvas element (issue #107) |
| `XnaFiddle.BlazorGL/CompilationService.cs` | Roslyn compile, `_referenceCache`, `GetMetadataReferencesAsync`, `LogTiming` |
| `XnaFiddle.Core/LibraryRegistry.cs` | `RunAllCleanups` (every run) — per-run plugin static-state reset. `ClearCanvasElementCache` (profile switch only) — see above |
| `XnaFiddle.Core/Plugins/GameWindowPlugin.cs` | `CleanUp()` (every run): reflectively clears `BlazorGameWindow._instances` and every public delegate field on `Window.Current`. `ClearCanvasElementCache()` (profile switch only, called directly by `Index.razor.cs`, NOT via `RunAllCleanups`): clears `Document._elementsCache` |
| `Submodules/KniSB/Platforms/Graphics/.BlazorGL/ConcreteGraphicsAdapter.cs` | `Platform_IsProfileSupported` — the leaking HiDef probe + the `UseReferenceDevice` short-circuit |
| `Submodules/KniSB/Platforms/Game/.Blazor/BlazorGameWindow.cs` | Resolves `theCanvas`; static `_instances` dictionary; ctor subscribes per-game closures to `Window.Current` events, never unsubscribed (no Dispose) |

**Whether `nkast.Wasm.*` (Window, Document, Canvas, JSObject/`nkJSObject`) builds from NuGet or from the `Submodules/KniSB/Submodules/WasmSB` git submodule depends on which KniSB commit is pinned.** On `main`'s KniSB pin it's NuGet (`nkast.Wasm.Dom`/`.Canvas`/`.JSInterop`, via `Kni.Platform.Blazor.GL.csproj`) — decompile `lib/net10.0/*.dll` under `~/.nuget/packages/nkast.wasm.*/<version>/` to inspect it. On the `Work-In-Progress-(Gumknix)` pin (see above), `Submodules/WasmSB` is still a live, actively-built submodule (check `Submodules/KniSB/.gitmodules`), shipping its own JS asset version — check the actual version with `ls artifacts/e2e-publish/wwwroot/_content/nkast.Wasm.*/js/` after a publish, don't assume. **Landmine:** `XnaFiddle.BlazorGL/wwwroot/index.html`'s hardcoded `<script>` version literals and `Directory.Build.props`'s `KniWasmJsVersion` must match whatever the current pin actually ships — a mismatch 404s every script silently, starves `window.nkJSObject`, and every game hangs at boot (`Activator.CreateInstance` throws `nkJSObject not found`) with no console error beyond the 404s.
