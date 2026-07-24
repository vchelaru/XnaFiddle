---
description: How XnaFiddle's project-export subsystem turns a fiddle into a downloadable, buildable project for a chosen platform/runtime (KNI, MonoGame, or FNA; Desktop/Windows/Android/Blazor). Load when working on export targets, the generated csproj/zip, or questions about "supporting library X" in an exported project.
---

# Project Export

## Read this first: in-browser runtime vs. export targets

XnaFiddle **always** runs the user's code in the browser via KNI's **BlazorGL** platform — pure-managed, rendering to WebGL through JS interop, no native code. There is exactly one browser runtime.

"Supports MonoGame and KNI" does **not** mean two browser runtimes. KNI, MonoGame, and FNA are **export targets**: the export feature generates a buildable project for a chosen platform/runtime. A fiddle authored against KNI-in-browser can be exported to, e.g., MonoGame DesktopGL — a different library and a different runtime that builds and runs outside the browser.

**Consequence:** a question about "supporting library X" is about the *exported project's generated csproj*, not about running X in the browser. Do not analyze native P/Invoke-in-WASM constraints for an export target — that is the wrong question. (A past agent wrongly assumed FNA would need to run in the browser and went down exactly that dead end. Preventing that mistake is half the reason this skill exists.)

## Where to look

- `XnaFiddle.Core/ProjectExporter.cs` — generates the zip: builds the package list, emits `.slnx` / `.csproj` / entry points / `Game1.cs` per target.
- `XnaFiddle.Core/IExportableLibrary.cs` — plugin hook. Third-party libraries implement it to declare (a) how to detect their usage in source and (b) which NuGet packages to emit per `ExportTarget`. Also defines `ExportTargetExtensions.IsKni()`.

## The target matrix

`ExportTarget` enum is the full list of supported targets:
- **KNI:** DesktopGL, WindowsDX, Android, BlazorGL
- **MonoGame:** DesktopGL, WindowsDX, Android
- **FNA:** Desktop only (single target `FnaDesktop`, via the `FNA.NET` NuGet package — an opinionated third-party fork that bundles native libs)

## One runtime family per export — platforms never mix

The export dialog has a single **runtime** selector (`_exportRuntime`: KNI / MonoGame / FNA); the platform checkboxes are *within* that family. So a multi-platform export is always one family — **KNI and MonoGame can never be combined**, and FNA is single-target. This is enforced in the UI (`SetExportRuntime`, the runtime radios), so `ExportMultiPlatform`'s shared common project only ever sees one family. The exporter itself hard-blocks only the FNA-mixing case (`Export(targets,…)` throws); the KNI/MonoGame split is a UI guarantee. Design consequence: per-target logic never has to reconcile two framework families in one solution.

## MonoGame — the `.Native` shared-reference convention

MonoGame (currently 3.8.5, GA) is a first-class export target: `Directory.Build.props` has a single `MonoGameFrameworkVersion`, and `WindowsDX12`/`DesktopVK` (the DX12/Vulkan `MonoGame.Framework.Native` + `MonoGame.Runtime.*` backends) are regular, always-available targets like DesktopGL/WindowsDX/Android — no version gating, no preview/stable split anywhere in the codebase.

**`.Native` is the renderer-agnostic compile reference for shared libraries.** `MonoGame.Framework.Native` is a backend-agnostic *managed* framework assembly — no graphics backend baked in — that functions like a reference assembly / `netstandard` lowest-common-denominator (the convention MonoGame's own `mg2dstartkit` template uses). `GenerateCommonCsproj` in `ProjectExporter.cs` unconditionally references `MonoGame.Framework.Native` with `PrivateAssets=All` for the shared/common project whenever any MonoGame target is present; each platform head then supplies its concrete backend (`MonoGame.Framework.DesktopGL`/`WindowsDX`/`Android` for classic backends; `MonoGame.Framework.Native` + native `MonoGame.Runtime.*.{DX12,Vulkan}` for the new backends). Compiling the shared lib against `.Native` prevents leaking renderer-specific API.

**Two independent layers — do not conflate:**
- *Managed reference* — `.Native` is the agnostic contract; ANY backend's `MonoGame.Framework.dll` satisfies it at runtime. Classic and native backends are interchangeable at this layer.
- *Compiled content* — effects / `.xnb` do NOT cross between the classic MGCB pipeline (GL/DX) and the new native Content Builder (DX12/Vulkan). This is the real incompatibility (e.g. Apos.Shapes / Gum shaders failing on DX12/VK) and it is orthogonal to the reference choice. See `BuildPackageList`'s `MonoGameWindowsDX12`/`MonoGameDesktopVK` branch.

## The core design contract

An exported zip must **build and run as-is via `dotnet restore`** — no manual setup steps. Every target is wired up purely through NuGet `<PackageReference>` entries (framework packages, platform package, content-pipeline package, plus per-target third-party packages). **Any new target must honor this constraint.**

Third-party library packages are added per-target by scanning the user's source through registered `IExportableLibrary` plugins.

## Shaders (`.fx`) — runtime ShadowDusk compilation (issue #39)

Exports honor the contract above for shaders by shipping the **`.fx` source** (into `Content/`) plus a **ShadowDusk `PackageReference`**, and recompiling at runtime — no XNB, no MGCB. `Export` takes a `shaders` (`name.fx -> HLSL`) map. The seam: the shared/common project references **`ShadowDusk.Core`** (the `IShaderCompiler` interface, net8.0, no natives) and the generated content manager has an `Effect` branch that compiles against it; each **per-platform** project references the concrete compiler (`ShadowDusk.Compiler` desktop+FNA / `ShadowDusk.Wasm` Blazor) and its entry point injects it + the `PlatformTarget` (GL vs DX vs `Fna` is just that value; FNA emits legacy D3D9 `.fxb` instead of `.mgfx`). `ProjectExporter.SupportsRuntimeShaders(target)` is the single source of truth for which targets are wired (desktop GL/DX + Blazor + FNA Desktop + Android + MonoGame DesktopVK/WindowsDX12); only iOS is gated (ship `.fx`, no compiler) — issue #52. Full detail lives in the **`shaders`** skill.

### Two orthogonal axes: `ContentBuildMode` and `ShaderCompileMode` (issue #52 follow-up)

`Export` takes two independent enums instead of one conflated choice:

- **`ContentBuildMode`** — how non-shader `Content/` assets get built: `Raw` (ship as-is, the default), `ClassicMgcb` (legacy `dotnet-mgcb`; `.png`/`.wav` compile to `.xnb` via `TextureImporter`/`WavImporter`, classic MonoGame targets only — `IsMonoGameClassic`), or `ContentBuilder` (MonoGame 3.8.5 GA's code-first Content Builder, wired for **every** MonoGame target including DX12/Vulkan since it isn't limited to a fixed platform list).
- **`ShaderCompileMode`** — how `.fx` gets compiled: `ShadowDusk` (default; ship source, recompile at runtime — same as the in-browser editor) or `Native` (compile at build time via whichever pipeline `ContentBuildMode` selected). **`Native` is a per-content-strategy opt-out from ShadowDusk, not a third content strategy** — ShadowDusk is the default shader compiler under all three `ContentBuildMode` values, including `ClassicMgcb`/`ContentBuilder`. Picking a content strategy alone does not force shaders through its native compiler.

Resolution predicates (`ProjectExporter.cs`): `UsesClassicMgcbAssets`/`UsesContentBuilder` resolve axis A per-target; `UsesClassicMgcbShaders`/`UsesContentBuilderShaders` resolve axis B *against* axis A (`Native` is a no-op unless the matching content strategy is also selected for that target); `UsesNativeShaders` is their union; `UsesShadowDuskShaders` is "supported target and Native wasn't actually selected here" (so e.g. `Native`+`ClassicMgcb` on `MonoGameDesktopVK`, which has no classic MGCB, falls through to ShadowDusk). `CompilesShippedShaders(target, shaderMode, contentMode)` is the combined "will shaders load here at all" predicate the dialog uses for the gated-platform message. `SupportsContentBuilder(target)` gates offering the Content Builder radio (every MonoGame target).

**Asset routing** (`GenerateCsproj`/`Export`): `PipelineAssetImporters` lists the only two extensions with a real stock importer (`.png`→`TextureImporter`/`TextureProcessor` with `ColorKeyEnabled=False` forced — MonoGame's default color-keys pure magenta to transparent, which would silently punch holes in a shipped `.png`; `.wav`→`WavImporter`/`SoundEffectProcessor`). `NonPipelineAssetExtensions` (`.achx`, `.fnt`, `.ttf`, `.ember`, `.xnb`, tilemap/level/text-data formats) always ships raw regardless of `ContentBuildMode` — no importer exists for them, and `.xnb` is already-compiled output. Under `ContentBuilder`, pipeline-eligible assets route into the separate `{projectName}.Content/Assets/` folder (picked up by its `WildcardRule("*")`); everything else, including a `ShadowDusk`-mode `.fx`, stays in the head's own `Content/`. Under `ClassicMgcb`, `.png`/`.wav` stay physically in `Content/` (same as `Raw`) — only the csproj's copy-to-output `<None>` exclusion changes for them, since `Content.mgcb` compiles them in place.

MGCB path mechanics (`GenerateContentMgcb`): emits one `Content.mgcb` per export covering whichever of shaders/assets route through it — an `EffectImporter`/`EffectProcessor` block per `.fx` when `useClassicMgcbShaders`, plus an importer/processor block per `PipelineAssetImporters`-matched asset independently (so a `ClassicMgcb` export with `ShadowDusk` shaders still gets a valid `.mgcb` covering just its `.png`/`.wav`). `MonoGame.Content.Builder.Task` overrides `/platform` (and out/intermediate dirs) per project from `$(MonoGamePlatform)` at build time, so the file's `/platform` line is just a default. `/profile` is **not** overridden — set to `HiDef` to match how the editor runs shaders. The existing MGCB infra (`NeedsMgcbToolManifest` = `IsMonoGameClassic`, the dotnet-mgcb manifest, the Mark-of-the-Web unblock) is reused unchanged.

**Export dialog UI** (`Index.razor`/`.cs`): two independent radio groups — "Content strategy:" (Raw / Classic MGCB / Content Builder) always shown for MonoGame, and "Shader compiler:" (ShadowDusk / Native) shown only when content strategy ≠ `Raw`. `EffectiveContentModes()` collapses back to `(ShadowDusk, Raw)` when the runtime isn't MonoGame.

**MonoGame DX12/VK backends:** ShadowDusk 0.14.0 added a DirectX12 backend, so `MonoGameWindowsDX12` shaders are wired under the default `ShaderCompileMode.ShadowDusk` too, same as `MonoGameDesktopVK`'s Vulkan backend (0.12.0+) — issue #122. `ContentBuildMode.ContentBuilder` + `ShaderCompileMode.Native` is still available as an alternative on both, just no longer the only way.

## Library MGCB content compiles into the NuGet cache at build time (not shipped)

Verified by inspecting `.nupkg` entries, `Content.mgcb`, and file timestamps.

- Libraries like **Apos.Shapes** do **not** ship a precompiled `.xnb`. The `.nupkg` ships the **`.fx` source** + a `Content.mgcb` + a `buildTransitive/*.props` that adds a `<MonoGameContentReference>`. Listing the immutable `.nupkg` entries shows `.fx` + `.mgcb` + `.props` + the lib `.dll` and **no** `.xnb`.
- At the **consuming project's build**, `MonoGame.Content.Builder.Task` runs MGCB and compiles `apos-shapes.fx` → `.xnb` (EffectImporter/EffectProcessor). `Content.mgcb`'s `/outputDir:bin/$(Platform)` + `/intermediateDir:obj/$(Platform)` are relative to the **`.mgcb`'s own location**, so MGCB writes the output AND its incremental cache **inside the package cache**: `~/.nuget/packages/<lib>/<ver>/buildTransitive/Content/{bin,obj}/<Platform>/…`. The `.xnb` is then copied into the consuming project's output `Content/`.
- So a compiled `.xnb` **does** live in the NuGet cache — as **build output, not shipped content**. To distinguish shipped vs. built: list the immutable `.nupkg` entries (not the extracted folder) and cross-check timestamps (built `.xnb` is newer than the extracted source and differs per platform/build).
- **Clean-rebuild gotcha:** deleting the consuming project's `bin`/`obj` does **not** force a content recompile — MGCB's incremental cache lives in the **package folder**, so it reuses the cached `.xnb`. To simulate a fresh user, delete `…/buildTransitive/Content/{bin,obj}` (or the whole `<lib>/<ver>/` package folder, which re-extracts source only). A genuinely fresh user has only the `.fx`, so their first build always compiles it.
- This MGCB path exists only for **classic** targets (DesktopGL/WindowsDX, GL/DX). DX12/Vulkan use the separate native Content Builder — why library effects (Apos.Shapes/Gum) don't build/load there. See the *compiled content* layer in the MonoGame `.Native` section.

## Not yet documented (grow only on confusion)

Multi-platform common-project split, Android resource embedding, `RawContentManager` / premultiply logic, the Blazor `index.html` JS bootstrap, and per-package version plumbing (`PackageVersions`, generated from the BlazorGL csproj) all exist in `ProjectExporter.cs`. Left shallow on purpose — deepen later if they actually cause confusion.
