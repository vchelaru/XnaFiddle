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

## The core design contract

An exported zip must **build and run as-is via `dotnet restore`** — no manual setup steps. Every target is wired up purely through NuGet `<PackageReference>` entries (framework packages, platform package, content-pipeline package, plus per-target third-party packages). **Any new target must honor this constraint.**

Third-party library packages are added per-target by scanning the user's source through registered `IExportableLibrary` plugins.

## Not yet documented (grow only on confusion)

Multi-platform common-project split, Android resource embedding, `RawContentManager` / premultiply logic, the Blazor `index.html` JS bootstrap, and per-package version plumbing (`PackageVersions`, generated from the BlazorGL csproj) all exist in `ProjectExporter.cs`. Left shallow on purpose — deepen later if they actually cause confusion.
