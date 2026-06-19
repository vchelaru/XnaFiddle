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

## MonoGame 3.8.5 (preview) — policy and the `.Native` convention

**Policy — do not relitigate, do not warn.** MonoGame 3.8.5 is in preview but near shipping. Treat it as a first-class, supported target — NOT risky/experimental to be avoided or hedged. **Never warn the user that 3.8.5 is "preview."** We actively track and support it so we are ready the moment stable drops. When work touches MonoGame, prefer adopting 3.8.5's conventions.

**`.Native` is the renderer-agnostic compile reference for shared libraries.** `MonoGame.Framework.Native` is a backend-agnostic *managed* framework assembly — no graphics backend baked in — that functions like a reference assembly / `netstandard` lowest-common-denominator. The 3.8.5 `mg2dstartkit` template references it from the shared `.Core` library with `PrivateAssets=All`; each platform head then supplies the concrete backend (`MonoGame.Framework.DesktopGL` / `WindowsDX` / etc. for classic backends; `MonoGame.Framework.Native` + native `MonoGame.Runtime.*.{DX12,Vulkan}` for the new backends). Compiling the shared lib against `.Native` prevents leaking renderer-specific API. Going forward, exported shared/common projects should follow this convention.

**Two independent layers — do not conflate:**
- *Managed reference* — `.Native` is the agnostic contract; ANY backend's `MonoGame.Framework.dll` satisfies it at runtime. Classic and native backends are interchangeable at this layer (the template compiles `.Core` against `.Native` yet ships all-classic heads).
- *Compiled content* — effects / `.xnb` do NOT cross between the classic MGCB pipeline (GL/DX) and the new native Content Builder (DX12/Vulkan). This is the real incompatibility (e.g. Apos.Shapes / Gum shaders failing on DX12/VK) and it is orthogonal to the reference choice.

**Exporter note:** `ProjectExporter.cs` currently hardwires `MonoGame.Framework.DesktopGL` as the shared-project compile reference (~line 411, the `isMonoGame` branch). The convention-correct reference is `MonoGame.Framework.Native`.

## The core design contract

An exported zip must **build and run as-is via `dotnet restore`** — no manual setup steps. Every target is wired up purely through NuGet `<PackageReference>` entries (framework packages, platform package, content-pipeline package, plus per-target third-party packages). **Any new target must honor this constraint.**

Third-party library packages are added per-target by scanning the user's source through registered `IExportableLibrary` plugins.

## Shaders (`.fx`) — runtime ShadowDusk compilation (issue #39)

Exports honor the contract above for shaders by shipping the **`.fx` source** (into `Content/`) plus a **ShadowDusk `PackageReference`**, and recompiling at runtime — no XNB, no MGCB. `Export` takes a `shaders` (`name.fx -> HLSL`) map. The seam: the shared/common project references **`ShadowDusk.Core`** (the `IShaderCompiler` interface, net8.0, no natives) and the generated content manager has an `Effect` branch that compiles against it; each **per-platform** project references the concrete compiler (`ShadowDusk.Compiler` desktop+FNA / `ShadowDusk.Wasm` Blazor) and its entry point injects it + the `PlatformTarget` (GL vs DX vs `Fna` is just that value; FNA emits legacy D3D9 `.fxb` instead of `.mgfx`). `ProjectExporter.SupportsRuntimeShaders(target)` is the single source of truth for which targets are wired (desktop GL/DX + Blazor + FNA Desktop); Android/iOS and MonoGame DX12/VK are gated (ship `.fx`, no compiler) — issue #52. Full detail lives in the **`shaders`** skill.

## Library MGCB content compiles into the NuGet cache at build time (not shipped)

Verified by inspecting `.nupkg` entries, `Content.mgcb`, and file timestamps.

- Libraries like **Apos.Shapes** do **not** ship a precompiled `.xnb`. The `.nupkg` ships the **`.fx` source** + a `Content.mgcb` + a `buildTransitive/*.props` that adds a `<MonoGameContentReference>`. Listing the immutable `.nupkg` entries shows `.fx` + `.mgcb` + `.props` + the lib `.dll` and **no** `.xnb`.
- At the **consuming project's build**, `MonoGame.Content.Builder.Task` runs MGCB and compiles `apos-shapes.fx` → `.xnb` (EffectImporter/EffectProcessor). `Content.mgcb`'s `/outputDir:bin/$(Platform)` + `/intermediateDir:obj/$(Platform)` are relative to the **`.mgcb`'s own location**, so MGCB writes the output AND its incremental cache **inside the package cache**: `~/.nuget/packages/<lib>/<ver>/buildTransitive/Content/{bin,obj}/<Platform>/…`. The `.xnb` is then copied into the consuming project's output `Content/`.
- So a compiled `.xnb` **does** live in the NuGet cache — as **build output, not shipped content**. To distinguish shipped vs. built: list the immutable `.nupkg` entries (not the extracted folder) and cross-check timestamps (built `.xnb` is newer than the extracted source and differs per platform/build).
- **Clean-rebuild gotcha:** deleting the consuming project's `bin`/`obj` does **not** force a content recompile — MGCB's incremental cache lives in the **package folder**, so it reuses the cached `.xnb`. To simulate a fresh user, delete `…/buildTransitive/Content/{bin,obj}` (or the whole `<lib>/<ver>/` package folder, which re-extracts source only). A genuinely fresh user has only the `.fx`, so their first build always compiles it.
- This MGCB path exists only for **classic** targets (DesktopGL/WindowsDX, GL/DX). DX12/Vulkan use the separate native Content Builder — why library effects (Apos.Shapes/Gum) don't build/load there. See the *compiled content* layer in the 3.8.5 section.

## Not yet documented (grow only on confusion)

Multi-platform common-project split, Android resource embedding, `RawContentManager` / premultiply logic, the Blazor `index.html` JS bootstrap, and per-package version plumbing (`PackageVersions`, generated from the BlazorGL csproj) all exist in `ProjectExporter.cs`. Left shallow on purpose — deepen later if they actually cause confusion.
