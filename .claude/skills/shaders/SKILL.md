---
description: The in-browser HLSL shader (.fx) subsystem in XnaFiddle — ShadowDusk.Wasm compiling .fx to profile-agnostic .mgfx, the SHADOWDUSK build gate, shader editor tabs, Content.Load<Effect> resolution, example shaders, and how shaders travel in share/snippet/gist payloads. Load when working on shader compilation, .fx tabs, ShadowDusk, Effect loading, CompileRegisteredShadersAsync, the net8.0-browser vs net8.0 split, or shader examples.
---

# Shaders

XnaFiddle compiles user HLSL `.fx` to KNI `.mgfx` entirely in the browser via the **ShadowDusk.Wasm** NuGet package (DXC + SPIRV-Cross WASM modules), then loads the result as an `Effect` through `InMemoryContentManager`. No MGCB/content pipeline, no server round-trip.

## Build wiring

- `ShadowDusk.Wasm` is a `PackageReference` **conditioned to `net8.0-browser`** (`XnaFiddle.BlazorGL.csproj`). It targets `net8.0-browser` (its `[JSImport]` backends), so a plain `net8.0` reference fails NU1201. This is the same browser-vs-test-build split as `XnaFiddle.Core` (issue #26): the test-only `net8.0` build cannot reference it.
- The `SHADOWDUSK` `DefineConstants` is set **only** for `net8.0-browser`. All shader-compile code is wrapped in `#if SHADOWDUSK`; the `net8.0` build `#if`s it out and `CompileRegisteredShadersAsync` becomes a no-op returning `null`.
- Version is pinned by `$(ShadowDuskVersion)` in `Directory.Build.props` — the **only** file to touch when bumping (the number quoted in this skill drifts; read the prop for the live value). All ShadowDusk packages release in lockstep on one version, so this property feeds **both** the editor's `ShadowDusk.Wasm` reference **and** the exporter's `PackageVersions.ShadowDusk` (generated const) — because exported projects now compile their `.fx` at runtime via ShadowDusk too (issue #39, see "Export" below). 0.5.0+ is required for the exported path: it added `InitializeAsync()` + a synchronous `Compile()`.
- All three packages (`ShadowDusk.Core` / `.Wasm` / `.Compiler`) come from **nuget.org** — *not* the KniSB submodule (the `nuget.config` comment about "sibling ShadowDusk projects" is misleading; that's a historical CPM/NU1507 note). Confirm a version exists before bumping: `https://api.nuget.org/v3-flatcontainer/shadowdusk.core/index.json`. After a bump, only `XnaFiddle.BlazorGL` exercises the new package at build time (web/editor side); exported projects are built later by the end user, so the exporter just writes the version string.
- The DXC + SPIRV-Cross wasm modules ship inside the package as static web assets served automatically at `_content/ShadowDusk.Wasm/`. Nothing to copy or configure.

## Compile flow (`Index.razor.cs` -> `CompileRegisteredShadersAsync`)

Called from `DoCompileAndRun` **before** the game is built (around line 1007), so user code can `Content.Load<Effect>("Name")` immediately. Its `await` is deliberately **outside** the synchronous game-swap window (see the `game-lifecycle` skill for that window).

For each tab in `_shaderTabs`:
1. Pull current HLSL via `monacoInterop.getModelValue`.
2. `new ShadowDusk.Wasm.WasmShaderCompiler().CompileAsync(source, options, _compileCts.Token)` with `CompilerOptions { Target = PlatformTarget.OpenGL, SourceFileName = fileName }`.
3. On failure, returns a formatted error string (joined `FxcFormattedMessage` lines) -> caller shows "Shader compilation failed." and aborts the run.
4. On success, `RegisterContentFile(bareName, result.Value.Data)` registers the `.mgfx` bytes under the **extension-stripped** name.

**Lazy DXC download:** the loop is guarded by `if (_shaderTabs.Count > 0)`. With no shader tabs, the `WasmShaderCompiler` is never constructed and the ~17 MB DXC wasm is never fetched.

**Profile-agnostic output:** `PlatformTarget.OpenGL` emits one `.mgfx` that loads under KNI Reach (WebGL1), HiDef (WebGL2), and desktop GL alike — a single compile works regardless of the game's `GraphicsProfile`. **Web is GL-only:** ShadowDusk in the browser can compile only OpenGL targets — it cannot emit DirectX output, and even the upcoming FNA DX9 `.fxb` support won't produce `.fxb` on web. Any DirectX/`.fxb` target requires a desktop-side compile.

**Stale-effect cleanup:** `_lastCompiledShaders` (a `HashSet`) tracks what was compiled last Run. After compiling, any bare name in the old set not in the current set is `InMemoryContentManager.RemoveFile`d, so a removed/renamed shader can't resolve old bytes via a stale `Content.Load<Effect>`.

## Headless compile-checking & HLSL gotchas

- **Compile-gate `.fx` without the browser** with the official `ShadowDusk.Cli` dotnet tool: `ShadowDuskCLI <in.fx> <out.mgfx> /Profile:OpenGL`. Its output is byte-identical to the editor's `ShadowDusk.Wasm`, so a green run is an authoritative check for a shader edit (gate edits without launching the app). `/Profile:OpenGL` is **mandatory** — the default is `DirectX_11`, which is not what the editor targets. Pin the tool to `$(ShadowDuskVersion)`.
- **Legacy LOD/sampling intrinsics are rejected on the OpenGL target.** `tex2Dlod` (and friends) won't compile; use the modern texture-object form `Texture.SampleLevel(sampler, uv, lod)`. The compiler error names the rewrite.

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
| **Export** | Yes (issue #39) — the `.fx` **source** ships in the export `Content/` and the exported project recompiles it at runtime via ShadowDusk (no XNB/MGCB), exactly like the editor. `ProjectExporter.Export` takes a `shaders` (`name.fx -> HLSL`) map; the export call site re-collects via `CollectShaderFilesAsync()`. Wired for desktop GL/DX (`ShadowDusk.Compiler`, `PlatformTarget.OpenGL`/`DirectX`), Blazor (`ShadowDusk.Wasm`, awaits `InitializeAsync` before the render loop), and FNA (`ShadowDusk.Compiler`, `PlatformTarget.Fna` → legacy D3D9 `.fxb`, loaded by FNA's MojoShader `Effect` ctor — issue #54). Android/iOS and MonoGame DX12/VK are **gated** (ship `.fx`, no compiler wired) — issue #52. See the `project-export` skill. |

`ShaderFile` (`ShaderFile.cs`) is the model: `Name` (filename incl. `.fx`) + `Source` (HLSL). `CollectShaderFilesAsync` snapshots open tabs into `_shareShaders` (refreshed on share/snippet build because shader edits don't fire the C#-only content callback). `ApplyShaderFilesAsync` clears stale tabs then re-opens the payload's shaders.

## Key files

| File | Role |
|---|---|
| `XnaFiddle.BlazorGL/Pages/Index.razor.cs` | `CompileRegisteredShadersAsync`, `_shaderTabs`, tab ops, `_lastCompiledShaders`, share/snippet/gist shader handling |
| `XnaFiddle.BlazorGL/Pages/Index.razor` | Tab strip, gist shader warning (~200), export-dialog runtime-compile message (~244) |
| `XnaFiddle.Core/ProjectExporter.cs` | Runtime shader export (issue #39): `GetShaderExportInfo`/`SupportsRuntimeShaders`, `WriteShaderSources`, the `IShaderCompiler`-seam content manager + per-platform compiler injection |
| `XnaFiddle.BlazorGL/InMemoryContentManager.cs` | `Load<Effect>` branch: `new Effect(gd, bytes)` from registered `.mgfx` |
| `XnaFiddle.BlazorGL/ShaderFile.cs` | Shader-tab model for share/snippet/gist payloads |
| `XnaFiddle.BlazorGL/SnippetModel.cs` | `Shaders` field on the snippet payload |
| `XnaFiddle.BlazorGL/ExampleGallery.cs` | `Shaders` category catalog entries |
| `XnaFiddle.BlazorGL/Examples/Shader*.{cs,fx}` | Example shaders + the `Content.Load<Effect>` usage pattern |
| `XnaFiddle.BlazorGL/XnaFiddle.BlazorGL.csproj` | `ShadowDusk.Wasm` ref (net8.0-browser), `SHADOWDUSK` gate |
| `Directory.Build.props` | `$(ShadowDuskVersion)` — single source of truth for editor + exported `PackageVersions.ShadowDusk` |
