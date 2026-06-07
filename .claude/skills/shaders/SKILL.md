---
description: The in-browser HLSL shader (.fx) subsystem in XnaFiddle — ShadowDusk.Wasm compiling .fx to profile-agnostic .mgfx, the SHADOWDUSK build gate, shader editor tabs, Content.Load<Effect> resolution, example shaders, and how shaders travel in share/snippet/gist payloads. Load when working on shader compilation, .fx tabs, ShadowDusk, Effect loading, CompileRegisteredShadersAsync, the net8.0-browser vs net8.0 split, or shader examples.
---

# Shaders

XnaFiddle compiles user HLSL `.fx` to KNI `.mgfx` entirely in the browser via the **ShadowDusk.Wasm** NuGet package (DXC + SPIRV-Cross WASM modules), then loads the result as an `Effect` through `InMemoryContentManager`. No MGCB/content pipeline, no server round-trip.

## Build wiring

- `ShadowDusk.Wasm` is a `PackageReference` **conditioned to `net8.0-browser`** (`XnaFiddle.BlazorGL.csproj`). It targets `net8.0-browser` (its `[JSImport]` backends), so a plain `net8.0` reference fails NU1201. This is the same browser-vs-test-build split as `XnaFiddle.Core` (issue #26): the test-only `net8.0` build cannot reference it.
- The `SHADOWDUSK` `DefineConstants` is set **only** for `net8.0-browser`. All shader-compile code is wrapped in `#if SHADOWDUSK`; the `net8.0` build `#if`s it out and `CompileRegisteredShadersAsync` becomes a no-op returning `null`.
- Version is pinned by `$(ShadowDuskWasmVersion)` in `Directory.Build.props` (single source of truth). Unlike the other library versions there, it is **intentionally NOT** added to the generated `PackageVersions` class — exported projects ship pre-compiled and don't need the compiler.
- The DXC + SPIRV-Cross wasm modules ship inside the package as static web assets served automatically at `_content/ShadowDusk.Wasm/`. Nothing to copy or configure.

## Compile flow (`Index.razor.cs` -> `CompileRegisteredShadersAsync`)

Called from `DoCompileAndRun` **before** the game is built (around line 1007), so user code can `Content.Load<Effect>("Name")` immediately. Its `await` is deliberately **outside** the synchronous game-swap window (see the `game-lifecycle` skill for that window).

For each tab in `_shaderTabs`:
1. Pull current HLSL via `monacoInterop.getModelValue`.
2. `new ShadowDusk.Wasm.WasmShaderCompiler().CompileAsync(source, options, _compileCts.Token)` with `CompilerOptions { Target = PlatformTarget.OpenGL, SourceFileName = fileName }`.
3. On failure, returns a formatted error string (joined `FxcFormattedMessage` lines) -> caller shows "Shader compilation failed." and aborts the run.
4. On success, `RegisterContentFile(bareName, result.Value.Data)` registers the `.mgfx` bytes under the **extension-stripped** name.

**Lazy DXC download:** the loop is guarded by `if (_shaderTabs.Count > 0)`. With no shader tabs, the `WasmShaderCompiler` is never constructed and the ~17 MB DXC wasm is never fetched.

**Profile-agnostic output:** `PlatformTarget.OpenGL` emits one `.mgfx` that loads under KNI Reach (WebGL1), HiDef (WebGL2), and desktop GL alike — a single compile works regardless of the game's `GraphicsProfile`.

**Stale-effect cleanup:** `_lastCompiledShaders` (a `HashSet`) tracks what was compiled last Run. After compiling, any bare name in the old set not in the current set is `InMemoryContentManager.RemoveFile`d, so a removed/renamed shader can't resolve old bytes via a stale `Content.Load<Effect>`.

## Effect resolution (`InMemoryContentManager`)

`Load<Effect>` has a branch: `new Effect(GetGraphicsDevice(), bytes)`. The `.mgfx` bytes carry MGFB magic (never XNB), so the XNB short-circuit skips them. Because `RegisterContentFile` stores under the bare name, `Content.Load<Effect>("Grayscale")` resolves the bytes registered for `Grayscale.fx`. See the `file-loading` skill for `RegisterContentFile` / the static `_files` store.

## Editor tabs

The editor is tabbed: one C# program tab (`CSharpTabName`) plus one Monaco model per `.fx`, tracked in `_shaderTabs` (filenames including extension, e.g. `Grayscale.fx`). Shader models use the **`hlsl`** Monaco language.

| Operation | Notes |
|---|---|
| `OpenShaderTabFromSourceAsync` | Creates/replaces the Monaco model; used by `[+]`, example load, gist/snippet import, and drag-drop |
| `AddShaderTab` | `[+]` button; seeds `DefaultShaderTemplate`, auto-names `Shader.fx`, `Shader1.fx`, ... |
| `CloseShaderTab` | **Confirms first** (`window.confirm`) — closing disposes the model, source is unrecoverable |
| `CommitRenameAsync` | Inline rename; the filename minus `.fx` is the `Content.Load<Effect>` key, so renaming changes how user code refers to it |
| `ResetShaderTabsAsync` | Drops all shader tabs (used when loading an example/fiddle that brings its own or none) |
| `OnFileDropped` | A dropped `.fx` opens as a shader tab (10 MB limit) instead of routing to the asset list |

## Examples

Shader examples live in `Examples/Shader*.{cs,fx}` plus a `KniIcon.png` (HiDef triples, registered in `ExampleGallery.Catalog` under category `"Shaders"`). The example `.cs` files set `GraphicsProfile.HiDef` like every other example — shaders run HiDef. (The `ExampleGallery.cs` line-37 comment "run as Reach/WebGL1" is **stale**; the profile-agnostic `.mgfx` runs under whatever profile the game selects.)

User-facing pattern: `Content.Load<Effect>("Name")` in `LoadContent`, optionally `effect.Parameters["X"]?.SetValue(...)`, then `spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, ..., effect)`. The examples draw an un-shaded pass first to prime SpriteBatch's vertex shader, which the pixel-only effects rely on.

## How shaders travel

| Channel | Shaders carried? |
|---|---|
| **Share URL** (`#code=...&shaders=...`) | Yes — `_shareShaders` JSON in a `&shaders=` fragment |
| **Snippet** (`#snippet=...`) | Yes — `SnippetModel.Shaders` (`List<ShaderFile>`). Not consumed by `SnippetExpander`; the page applies them separately via `ApplyShaderFilesAsync` |
| **Gist** | Partly — the copy-to-clipboard puts **only the C#**; the UI warns the user to add each `.fx` as a separate gist file (same filename). Gist **import** reads every `.fx` file back into tabs |
| **Export** | No — exports do **not** include shaders yet; the export dialog warns (needs the MGCB pipeline, tracked separately) |

`ShaderFile` (`ShaderFile.cs`) is the model: `Name` (filename incl. `.fx`) + `Source` (HLSL). `CollectShaderFilesAsync` snapshots open tabs into `_shareShaders` (refreshed on share/snippet build because shader edits don't fire the C#-only content callback). `ApplyShaderFilesAsync` clears stale tabs then re-opens the payload's shaders.

## Key files

| File | Role |
|---|---|
| `XnaFiddle.BlazorGL/Pages/Index.razor.cs` | `CompileRegisteredShadersAsync`, `_shaderTabs`, tab ops, `_lastCompiledShaders`, share/snippet/gist shader handling |
| `XnaFiddle.BlazorGL/Pages/Index.razor` | Tab strip, gist/export shader warnings (lines ~200, ~244) |
| `XnaFiddle.BlazorGL/InMemoryContentManager.cs` | `Load<Effect>` branch: `new Effect(gd, bytes)` from registered `.mgfx` |
| `XnaFiddle.BlazorGL/ShaderFile.cs` | Shader-tab model for share/snippet/gist payloads |
| `XnaFiddle.BlazorGL/SnippetModel.cs` | `Shaders` field on the snippet payload |
| `XnaFiddle.BlazorGL/ExampleGallery.cs` | `Shaders` category catalog entries |
| `XnaFiddle.BlazorGL/Examples/Shader*.{cs,fx}` | Example shaders + the `Content.Load<Effect>` usage pattern |
| `XnaFiddle.BlazorGL/XnaFiddle.BlazorGL.csproj` | `ShadowDusk.Wasm` ref (net8.0-browser), `SHADOWDUSK` gate |
| `Directory.Build.props` | `$(ShadowDuskWasmVersion)` (not in `PackageVersions`) |
