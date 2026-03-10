# XnaFiddle Architecture Guide

## Repository layout

```
XnaFiddle/
├── XnaFiddle.sln
├── TECHNICAL_PLAN.md               ← comprehensive design doc
├── Submodules/
│   └── KniSB/                      ← KNI (MonoGame fork) as git submodule
│       └── Submodules/WasmSB/      ← nested submodule: browser API bindings
└── XnaFiddle.BlazorGL/             ← the app
    ├── Pages/
    │   ├── Index.razor             ← UI layout: canvas + splitter + editor panel
    │   └── Index.razor.cs          ← game loop, compile/run, example loading, share/load
    ├── CompilationService.cs       ← Roslyn compile pipeline
    ├── ExampleGallery.cs           ← loads embedded .cs example files
    ├── InMemoryContentManager.cs   ← ContentManager backed by in-memory byte[]
    ├── SampleGame.cs               ← Phase 1 test game (obsolete, no longer used)
    ├── Examples/                   ← embedded resource .cs files shown in gallery
    │   ├── ColorCycle.cs
    │   ├── BouncingBall.cs
    │   ├── Checkerboard.cs
    │   ├── MouseTrail.cs
    │   ├── GumUI.cs
    │   └── AposShapes.cs
    └── wwwroot/
        ├── index.html              ← Wasm JS libs, rAF game loop bootstrap, Monaco CDN loader
        ├── css/app.css
        └── js/
            ├── monaco-interop.js   ← Monaco init, getValue/setValue, diagnostics markers
            └── decode.min.js       ← Brotli decompressor
```

## Key flows

### Compile & Run
1. User clicks button → `CompileAndRun()` sets `_isCompiling = true`, `_pendingCompile = true`
2. **`_pendingCompile` is consumed inside `TickDotNet()`**, which runs in the `requestAnimationFrame` context — this avoids a Blazor sync-context Monitor deadlock that occurs when compiling directly from a button click handler
3. `DoCompileAndRun()` calls `CompilationService.CompileAsync()`:
   - Fetches metadata references for all loaded assemblies + hardcoded `KniAssemblyNames` list (bypasses lazy-loading gap)
   - Compiles with Roslyn to IL bytes
   - Returns `CompilationResult` with IL, log, and `DiagnosticInfo` list
4. IL bytes are loaded via `Assembly.Load(ilBytes)`
5. `FindGameType()` scans for a class with base type `Microsoft.Xna.Framework.Game`
6. The old game is dropped (**not Disposed** — `Dispose()` would invalidate shared GraphicsDevice textures)
7. `CleanUpGameWindowRegistry()` and `CleanUpGumService()` reset static singletons so the next game starts clean
8. New game is instantiated, given an `InMemoryContentManager`, and `.Run()` is called

### Game loop
- `index.html` boots a `requestAnimationFrame` loop calling `tickJS` → `DotNet.invokeMethod("TickDotNet")`
- `TickDotNet()` (in `Index.razor.cs`) calls `_game.Tick()` each frame
- Canvas resize events invoke `OnCanvasResized()` which updates `GraphicsDeviceManager`

### Monaco editor
- Initialized in `OnAfterRender` via `monacoInterop.init(containerId, initialCode)`
- Editor value read/written via `monacoInterop.getValue()` / `monacoInterop.setValue(code)`
- Compiler diagnostics pushed as inline markers via `monacoInterop.setDiagnostics(list)`

### Asset loading
- User drags files onto the canvas → `fileDropInterop` invokes `OnFileDropped(fileName, base64Data)` on the .NET side
- Files stored in static `InMemoryContentManager.Files` dictionary
- `InMemoryContentManager` subclasses `ContentManager`, overrides `OpenStream` to serve from that dictionary

### Share / load from URL
- Share: code → GZip → Base64url → `#code=...` hash; URL copied to clipboard
- Load: on page load, reads `window.location.hash`, decompresses, sets Monaco value

## Critical gotchas

| Issue | Fix |
|-------|-----|
| Monitor deadlock when compiling | Route compile through `_pendingCompile` flag, consumed in rAF tick |
| KNI assemblies not loaded at compile time | Hardcoded `KniAssemblyNames` list in `CompilationService` |
| Gum statics accumulate across hot-reloads | `CleanUpGumService()` clears Root children, resets `SystemManagers.Default`, `LoaderManager` cache, `IsInitialized` |
| BlazorGameWindow `_instances` dict not cleared | `CleanUpGameWindowRegistry()` clears it via reflection |
| Old game Dispose() breaks next run | Set `_game = null` without calling `Dispose()` |
