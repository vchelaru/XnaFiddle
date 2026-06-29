---
description: The compile-to-run pipeline and WebGL/GraphicsDevice resource lifecycle in XnaFiddle — how a Run rebuilds the game, the per-run WebGL context leak and its UseReferenceDevice fix, Roslyn metadata-reference caching, and documented dead-ends. Load when working on DoCompileAndRun, game restart/lifecycle, GraphicsDevice/WebGL/canvas issues, render targets / mipmapped RenderTarget2D / explicit-LOD (SampleLevel) sampling / multi-texture effects, "Shader Compilation Failed"/CONTEXT_LOST_WEBGL/"Too many active WebGL contexts"/"texParameter: no texture bound" crashes, compile performance, or CompilationService.
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
2. Bump `_canvasGen` (the canvas has `@key="_canvasGen"` in `Index.razor`) + `StateHasChanged()` + `await Task.Delay(1)` so **Blazor** recreates the `<canvas>` (Blazor owns the DOM swap — don't do it with raw JS, that desyncs the renderer).
3. `setupCanvas` (JS) re-wires the fresh element; `window._canvasContextType = null` (it's unbound).
4. Rebuild the game (`Activator.CreateInstance` again, re-assign `InMemoryContentManager`) so its `BlazorGameWindow` binds the fresh canvas; fall through to `Run()`.

The critical enabler: `GameWindowPlugin.CleanUp` (run by `RunAllCleanups`) reflectively clears KNI's `Document._elementsCache`. Without that, `GetElementById("theCanvas")` returns the **stale cached `Canvas` wrapper** pointing at the detached old element -> black screen (this is what broke the earlier per-run canvas-swap attempt). Profile switches are rare (examples are all HiDef), so the double game-construction on a switch is fine.

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

## Touch UI starvation (issue #90)

Blazor WASM is single-threaded. `index.html` `tickJS` calls `TickDotNet` synchronously on every rAF frame; uncapped FPS after Run can starve Blazor `@onclick` on touch devices (toolbar buttons stop responding). **Fix:** full editor on touch stays uncapped; toolbar taps use capture-phase `touchend` → `invokeMethodAsync` (`data-touch-action` on Run/Stop/Examples/Stop-compile), and `TickDotNet` is deferred via `setTimeout(0)` on touch. `TickDotNet` already throttles `_game.Tick()` to ~4fps while `_isCompiling`.

## Why the leak "suddenly appeared"

It is pre-existing in KNI and independent of any XnaFiddle change. It surfaced only after the metadata-reference cache made compiles fast (~0.7s vs several seconds): fast iteration means a user naturally does 10+ Runs in one page session before refreshing, which is what reaches the context cap. It is **not** a GC-churn regression (see dead end #1).

## Render targets (WebGL)

- **Mipmapped render targets work.** `new RenderTarget2D(gd, w, h, mipMap:true, …)` builds a real mip chain — KNI runs `GL.GenerateMipmap` on `SetRenderTarget(null)` whenever the target's `LevelCount > 1` — and a shader reads explicit levels via `Texture.SampleLevel` (verified incl. screen-sized NPOT under WebGL2). (Earlier "mipmaps are flaky on GL" lore did **not** hold for this KNI+WebGL2 stack.)
- **Multi-texture effect + resize = crash.** An `Effect` sampling a *second* texture (bound via an Effect parameter, e.g. a bloom combine reading the scene as `BaseTexture`) leaves that texture in a `GraphicsDevice.Textures[]` slot. Disposing that render target on resize **without clearing the slot first** throws WebGL `INVALID_OPERATION: texParameter: no texture bound to target`: `ConcreteGraphicsContext.PlatformApplyTexturesAndSamplers` only re-binds *dirty* slots but applies sampler state to *every populated* slot, so the next single-texture pass hits the now-empty unit. **Fix:** null the used `GraphicsDevice.Textures[i]` before disposing the targets in your resize/`EnsureRenderTargets` path.

## Dead ends — DO NOT retry

1. **`GC.Collect()` / `GC.WaitForPendingFinalizers()` to reclaim leaked GL resources.** Useless: the leaked WebGL contexts are JS-side objects pinned in the `nkJSObject` registry, not .NET objects — GC can't touch them. (KNI's `GraphicsResource` finalizer does delete GL handles, but that was never the leak.)
2. **Recreating `theCanvas` *every run* to fix the leak.** Pointless: `theCanvas` was never the leak (the OffscreenCanvas probe is — use `UseReferenceDevice`). Recreating it per run only adds the swap's failure modes. (Recreating it *on a profile switch* is correct — see that section — but only because two specific things are handled: clear `Document._elementsCache` so the stale `Canvas` wrapper isn't reused (else black screen), and recreate via Blazor `@key` rather than raw JS removal (else Blazor DOM-diff desync). Doing a raw JS swap without those two is the dead end.)
3. **`WEBGL_lose_context.loseContext()` on the old context.** In Chrome this can reset the whole GPU process; a context created in the same synchronous turn comes up already-lost -> `CONTEXT_LOST_WEBGL` on the new device's first GL call.

## Key files

| File | Role |
|---|---|
| `XnaFiddle.BlazorGL/Pages/Index.razor.cs` | `DoCompileAndRun`, `CompileAndRun`, `TickDotNet`, swap window, `UseReferenceDevice` fix, `_canvasProfile`/`PromptProfileSwitch` |
| `XnaFiddle.BlazorGL/wwwroot/index.html` | `tickJS` rAF loop, `_tickInterval` FPS cap (touch full-editor 20fps — issue #90) |
| `XnaFiddle.BlazorGL/CompilationService.cs` | Roslyn compile, `_referenceCache`, `GetMetadataReferencesAsync`, `LogTiming` |
| `XnaFiddle.Core/LibraryRegistry.cs` | `RunAllCleanups` — per-run plugin static-state reset |
| `XnaFiddle.Core/Plugins/GameWindowPlugin.cs` | Clears KNI's static `BlazorGameWindow._instances` by reflection |
| `Submodules/KniSB/Platforms/Graphics/.BlazorGL/ConcreteGraphicsAdapter.cs` | `Platform_IsProfileSupported` — the leaking HiDef probe + the `UseReferenceDevice` short-circuit |
| `Submodules/KniSB/.../Wasm.Canvas/Canvas/OffscreenCanvas.cs` | Probe's WebGL2 context creation (no-attribs path leaks) |
| `Submodules/KniSB/Platforms/Game/.Blazor/BlazorGameWindow.cs` | Resolves `theCanvas`; static `_instances` dictionary |
