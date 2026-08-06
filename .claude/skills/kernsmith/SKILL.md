---
name: kernsmith
description: KernSmith runtime font generation for Gum text. Triggers: BmFont, KernSmithFontCreator, StbTrueTypeRasterizer, DynamicFonts example, KernSmithPlugin.
---

# KernSmith

Runtime (in-memory) font rasterization library, wired into Gum's text rendering — not a general-purpose graphics library. See `Examples/DynamicFonts.cs` for the canonical usage pattern: `BmFont.RegisterFont(name, ttfBytes)` to register a font by name, then `BmFont.GenerateFromSystem(...)` to rasterize it, or let Gum trigger generation lazily via `CustomSetPropertyOnRenderable.InMemoryFontCreator = new KernSmithFontCreator(GraphicsDevice, RasterizerBackend.StbTrueType)`.

## Font resolution: "system" fonts don't exist in the browser

`GenerateFromSystem(family, options)` is the one API for resolving-by-family-name — it does not distinguish an OS-installed font from an in-memory one. In a normal .NET process it scans OS font directories/registry for `family`. In Blazor WASM there is no filesystem/registry to scan, so that path never finds anything — `BmFont.RegisterFont(family, ttfBytes)` is the only way to make a family resolvable, and registered fonts are checked *before* any OS scan. So in this repo (and any browser target), every font is effectively "uploaded," not "installed": a user's `.ttf` gets registered under a family name (see `DynamicFonts.cs` registering the bundled `DroidSans.ttf` before calling `GenerateFromSystem("Droid Sans", ...)`), and that same call would silently fail to find a real system font like `"Arial"` unless it happens to also be registered. Style variants (Bold/Italic) register separately per family+style.

KernSmith also ships a `KernSmith.Fonts.Web` package for fetching WOFF fonts from CDNs (Google/Bunny Fonts) as a WASM-friendly alternative to bundling `.ttf` bytes — not currently referenced by XnaFiddle's `.csproj`.

## Landmine: rasterizer must be force-registered in WASM

`Program.cs` uses `RuntimeHelpers.RunClassConstructor(typeof(StbTrueTypeRasterizer).TypeHandle)` for KernSmith instead of the plain `typeof(X)` touch used for every other optional library (see the block above it). The `StbTrueTypeRasterizer` backend self-registers via a static constructor side effect, not by being referenced — a mere `typeof` touch loads the assembly but never runs the constructor, so the backend silently stays unregistered at runtime. If font generation fails only in the published/WASM build (not locally), check this line first.

StbTrueType is the only rasterizer backend usable in Blazor WASM — there's no native FreeType binary available there.

## Plugin (export)

`XnaFiddle.Core/Plugins/KernSmithPlugin.cs` — package set is target-dependent: `KernSmith.KniGum` for KNI exports, `KernSmith.MonoGameGum` for MonoGame exports, plus `KernSmith` and `KernSmith.Rasterizers.StbTrueType` always. See the **add-library** skill for the general plugin/export checklist this follows.

`Directory.Build.props` splits versioning: core `KernSmith`/`KernSmith.Rasterizers.StbTrueType` track their own release version, while the Gum-integration packages (`KniGum`/`MonoGameGum`) track Gum's version instead — bumping one does not bump the other.
