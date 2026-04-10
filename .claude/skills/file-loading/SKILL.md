---
description: How file/asset loading works in XnaFiddle — embedded example assets, user drag-and-drop, InMemoryContentManager, TitleContainer XHR intercept, and exported project content. Load when working on content loading, asset handling, drag-and-drop, TitleContainer.OpenStream, project export, or adding file format support.
---

# File Loading

XnaFiddle has no disk or traditional content pipeline. All assets live in memory and are loaded through `InMemoryContentManager`, a custom `ContentManager` subclass that serves files from a static `Dictionary<string, byte[]>`.

## Three entry paths, one destination

| Source | Mechanism | Ends up in | SourceUrl set? |
|---|---|---|---|
| **Example assets** | Embedded resources in the assembly, loaded by `ExampleGallery.LoadAssets()` | `RegisterContentFile()` | Yes — points to `wwwroot/examples/{ExampleName}/{file}` |
| **URL-fetched assets** | `FetchAndAddAssetUrl(url)` via `&assets=` share param or URL input | `RegisterContentFile()` | Yes — the fetched URL |
| **User drag-and-drop** | JS `fileDropInterop` -> `OnFileDropped` JSInvokable | `RegisterContentFile()` | No — cannot be re-fetched |

All three paths converge on `RegisterContentFile(fileName, bytes)` in `Index.razor.cs`, which does two things:
1. `InMemoryContentManager.AddFile(fileName, bytes)` — stores data under the original filename AND the extension-stripped name (so `Content.Load<Texture2D>("KniIcon")` works without knowing the extension)
2. `contentFileCache.register(fileName, base64)` via JS interop — registers the file in the JS-side XHR cache so `TitleContainer.OpenStream()` can resolve it (see below)

## Supported formats

Controlled by `SupportedAssetExtensions` in `Index.razor.cs`:

- **`.png`** — loaded as `Texture2D` via `Texture2D.FromStream()`
- **`.wav`** — loaded as `SoundEffect` via `SoundEffect.FromStream()`
- **`.fnt`** — BMFont text format; stored as raw bytes. The UI parses `page` lines to show which texture files the font references (so users know what companion `.png` files to also drop)
- **`.ttf`** — TrueType font; stored as raw bytes for FontStashSharp
- **`.ember`** — stored as raw bytes

`InMemoryContentManager.Load<T>()` has explicit branches for `Texture2D` and `SoundEffect`. Any other type falls through to `base.Load<T>()`, which will fail (no disk content pipeline exists).

## TitleContainer XHR intercept

`TitleContainer.OpenStream(path)` is the standard XNA/MonoGame/KNI way to load raw files. In KNI's Blazor platform, it performs a **synchronous XHR GET** to the relative URL `path`. Since XnaFiddle's content files exist only in memory (not on a web server), a JS-side XHR monkey-patch intercepts these requests.

**JS side** (`wwwroot/index.html`, `contentFileCache` IIFE):
- `contentFileCache.register(path, base64)` decodes base64 to a binary string and stores it keyed by path
- `contentFileCache.unregister(path)` removes a cached entry; `clear()` removes all
- `XMLHttpRequest.prototype.open` is patched to capture the method and URL on each instance
- `XMLHttpRequest.prototype.send` is patched: if the request is a GET and the URL matches a registered path, it sets `_cfcIntercepted = true` and stores the cached data instead of hitting the network
- Property getters for `status`, `responseText`, and `readyState` are overridden to return cached values when `_cfcIntercepted` is set; non-intercepted XHRs fall through to the original native getters

**C# side** (`Index.razor.cs`):
- `RegisterContentFile(fileName, data)` calls both `InMemoryContentManager.AddFile()` and `contentFileCache.register()` via `IJSInProcessRuntime`
- `UnregisterContentFile(fileName)` does the reverse with `RemoveFile()` and `unregister()`

**Path gotcha — `Content.RootDirectory` differences:**
- In XnaFiddle, `Content.RootDirectory` is `""` (empty), so `TitleContainer.OpenStream("DroidSans.ttf")` requests `"DroidSans.ttf"` — files are registered under bare filenames
- In exported projects, `Content.RootDirectory` is `"Content"`, so the path becomes `"Content/DroidSans.ttf"` and the file is served from disk/wwwroot
- Export-compatible user code should use `Path.Combine(Content.RootDirectory, "file.ext")` to work in both environments

## Embedded example assets

Naming convention: `Examples/{ExampleName}.{AssetFile}` in the filesystem becomes embedded resource `XnaFiddle.Examples.{ExampleName}.{AssetFile}`.

Example: `Examples/TextureLoading.KniIcon.png` is the asset `KniIcon.png` for the `TextureLoading` example.

The `.csproj` has two wildcard `EmbeddedResource` includes — one for `*.cs` (example code) and one for everything else (assets). No manual `.csproj` edits are needed when adding a new asset file.

`ExampleGallery.LoadAssets(name)` finds all embedded resources that share the example's prefix but are not the `.cs` file, strips the prefix, and returns them as `ExampleAsset[]` (filename + byte array).

### Static copies for sharing

Every example asset is **also** served as a static file under `wwwroot/examples/{ExampleName}/{AssetFile}`. This duplicate is what makes share links work: `LoadExampleAssets()` sets `AssetInfo.SourceUrl` to `{Navigation.BaseUri}examples/{ExampleName}/{file}`, so `GetAssetUrlsFragment()` includes those URLs in the `&assets=` share fragment.

**When adding a new example asset:** place the file in `Examples/` (embedded resource, picked up by wildcard), **and** copy it to `wwwroot/examples/{ExampleName}/{AssetFile}` (static web asset). Both locations are required.

Current static copies: `AetherPhysics/CircleSprite.png`, `AetherPhysics/GroundSprite.png`, `FontStashSharp/DroidSans.ttf`, `SoundPlayback/powerup.wav`, `TextureLoading/KniIcon.png`.

## Drag-and-drop flow

1. JS `fileDropInterop` listens on `#canvasHolder` for `drop` events
2. All dropped files are passed through to C# — no JS-side MIME or extension filtering
3. File is read as base64 via `FileReader`, sent to C# `OnFileDropped(fileName, base64)`
4. C# validates extension against `SupportedAssetExtensions`, enforces 10 MB limit
5. Calls `RegisterContentFile()` (InMemoryContentManager + JS XHR cache) and updates the UI asset list

## Keyboard event suppression

A capturing-phase IIFE in `monaco-interop.js` intercepts `keydown`/`keyup` on `window` and calls `stopPropagation()` when focus is inside a `.monaco-editor` element. This prevents KNI (which listens in the bubbling phase) from receiving keyboard input while the user types in the editor. **F5 is exempted** from `keydown` suppression so the compile-and-run shortcut always works.

## Build version detection

MSBuild generates `BuildInfo.g.cs` (C# const) and `wwwroot/js/build-version.js` (`window._buildVersion`) with the same UTC timestamp. On startup, C# compares `BuildInfo.BuildTime` against the JS value fetched from the browser. If they differ, the app sets `_staleAssets = true` and shows a warning banner with a Refresh button, indicating the browser is serving cached static assets from an older build.

## Exported project support

Multi-platform exports produce a solution with a common project (`{Name}Common`) holding `Game1.cs` and `RawContentManager.cs`, plus per-platform projects (`{Name}.DesktopGL`, `{Name}.BlazorGL`, etc.) each with their own entry point. Content files live in a shared `Content/` folder at the solution root.

`RawContentManager` (generated by `ProjectExporter.GenerateRawContentManager`) replaces `InMemoryContentManager` in exports. It uses `TitleContainer.OpenStream(Path.Combine(RootDirectory, assetName + ext))` for non-desktop platforms (Android, Blazor) and `File.OpenRead` for desktop. Supports `Texture2D` (`.png`, `.jpg`, `.jpeg`, `.bmp`) and `SoundEffect` (`.wav`).

**BlazorGL content linking:** BlazorGL serves content from `wwwroot/Content/`. In multi-platform exports, a post-build MSBuild target (`CopySharedContent`) copies files from the shared `Content/` folder into `wwwroot/Content/`. Other platforms reference `Content/` directly (or as Android assets).

## Static persistence

`InMemoryContentManager._files` is static — assets survive across recompilations. The loaded-asset cache (`_loaded`) is per-instance and cleared on `Unload()`. Each compile run creates a fresh `InMemoryContentManager` instance, but the underlying file store persists.

## Adding a new file format

1. Add the extension to `SupportedAssetExtensions` in `Index.razor.cs`
2. Add a type check branch in `InMemoryContentManager.Load<T>()` (alongside the existing `Texture2D` and `SoundEffect` branches)
3. Update the `GenerateRawContentManager` template in `ProjectExporter.cs` if exported projects should also support the format
4. For embedded example assets: place the file in `Examples/` with the `{ExampleName}.{filename}` naming convention; the `.csproj` wildcards will pick it up automatically

## Key files

| File | Role |
|---|---|
| `XnaFiddle.BlazorGL/InMemoryContentManager.cs` | Static file store + `ContentManager` that loads `Texture2D` and `SoundEffect` from bytes |
| `XnaFiddle.BlazorGL/ExampleGallery.cs` | Reads embedded resources; `LoadAssets()` extracts non-code files for an example |
| `XnaFiddle.BlazorGL/Pages/Index.razor.cs` | `OnFileDropped` JSInvokable, `SupportedAssetExtensions`, `LoadExampleAssets()`, stale-assets check |
| `XnaFiddle.BlazorGL/ProjectExporter.cs` | `GenerateRawContentManager` template with `Texture2D` + `SoundEffect` branches |
| `XnaFiddle.BlazorGL/wwwroot/index.html` | XHR intercept for `TitleContainer.OpenStream`, canvas tick loop, splitter layout, other JS interop |
| `XnaFiddle.BlazorGL/wwwroot/js/monaco-interop.js` | `fileDropInterop` (drag-and-drop), keyboard event suppression for Monaco |
| `XnaFiddle.BlazorGL/wwwroot/js/build-version.js` | MSBuild-generated; sets `window._buildVersion` for stale-asset detection |
| `XnaFiddle.BlazorGL/XnaFiddle.BlazorGL.csproj` | `EmbeddedResource` wildcards for `Examples/`, `GenerateBuildInfo` target |
| `XnaFiddle.BlazorGL/Examples/SoundPlayback.cs` | Example showing `Content.Load<SoundEffect>()` usage |
