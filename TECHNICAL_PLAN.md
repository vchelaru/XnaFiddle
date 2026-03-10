# Technical Plan: XnaFiddle — Standalone KNI Game Runner with Code Editor

## Goal

Extract the KNI game rendering and C# compilation capabilities from Gumknix into a standalone, embeddable component. The result is a simple web page with:

1. A **KNI game canvas** (WebGL) that renders a user-supplied `Game` subclass
2. A **code editor text area** (Blazor UI, outside the canvas) where users paste C# code
3. A **Compile & Run** button that compiles the code and hot-loads it into the canvas

**Repository**: `C:\Users\vchel\Documents\GitHub\XnaFiddle` (standalone git repo, separate from Gumknix)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────┐
│  Blazor WebAssembly App                         │
│                                                 │
│  ┌───────────────────────────────────────────┐  │
│  │  KNI Canvas (WebGL)                       │  │
│  │  Renders the user's compiled Game class   │  │
│  │  <canvas id="theCanvas">                  │  │
│  └───────────────────────────────────────────┘  │
│                                                 │
│  ┌───────────────────────────────────────────┐  │
│  │  Blazor UI (HTML/Razor)                   │  │
│  │  - <textarea> for code input              │  │
│  │  - Compile & Run button                   │  │
│  │  - Diagnostics/error output panel         │  │
│  └───────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
```

The Blazor UI and the KNI canvas coexist in the same Blazor WASM app but are separate DOM regions. The canvas is managed by KNI's `BlazorGameWindow`; the text area and button are standard Blazor/HTML elements rendered outside the canvas.

---

## What to Reuse from Gumknix

### Must keep (core KNI + compilation pipeline)

| Component | Source in Gumknix | Purpose |
|---|---|---|
| KNI Platform | `Kni.Platform.Blazor.GL` (project ref) | Game loop, GraphicsDevice, WebGL, input |
| Wasm interop libs | `Wasm.JSInterop`, `Wasm.Dom`, `Wasm.Canvas`, `Wasm.Audio` | Browser API bindings |
| Roslyn compiler | `Microsoft.CodeAnalysis.CSharp` NuGet | In-browser C# compilation |
| Metadata service | `MetadataReferenceService.BlazorWasm` NuGet | Provides assembly metadata refs for Roslyn |
| JS bootstrap | `index.html` script block (`initRenderJS`, `tickJS`) | Canvas setup + game loop via `requestAnimationFrame` |
| Wasm JS libraries | All `_content/nkast.Wasm.*/js/*.js` script tags | Low-level browser API interop |

### Do NOT need (Gumknix desktop environment)

| Component | Why it's excluded |
|---|---|
| Gum UI framework (`KniGum`, `MonoGameGum`) | The standalone app uses Blazor HTML for UI, not Gum |
| `GumknixDemo`, `Gumknix` class, Desktop, TaskBar | These compose the Gumknix OS-like shell |
| All applets (`AppletKniSCode`, `AppletKniopad`, etc.) | We're replacing the editor with a simple textarea |
| Monaco editor integration (`ModuleMonaco`) | Optional upgrade later; start with a textarea |
| `Wasm.File` (virtual filesystem) | Not needed for the minimal runner |
| Content pipeline (`GumknixContent`) | User games can use `GraphicsDevice` directly; no managed content |
| FFmpeg module | Not relevant |

---

## Project Structure (actual)

```
XnaFiddle/                          <- standalone git repo
├── .gitignore
├── .gitmodules
├── XnaFiddle.sln
├── TECHNICAL_PLAN.md
├── Submodules/
│   └── KniSB/                      <- git submodule (squarebananas/kniSB, branch: Work-In-Progress-(Gumknix))
│       └── Submodules/WasmSB/      <- nested submodule (squarebananas/Wasm)
└── XnaFiddle.BlazorGL/
    ├── XnaFiddle.BlazorGL.csproj
    ├── Program.cs
    ├── App.razor
    ├── MainLayout.razor
    ├── _Imports.razor
    ├── CompilationService.cs        <- Roslyn compilation logic (Phase 2)
    ├── ExampleGallery.cs            <- reads embedded example .cs files
    ├── SampleGame.cs               <- Phase 1 test game (color-cycling, no longer referenced)
    ├── Examples/                    <- embedded resources, excluded from compilation
    │   ├── BouncingBall.cs
    │   ├── Checkerboard.cs
    │   ├── ColorCycle.cs
    │   └── MouseTrail.cs
    ├── Pages/
    │   ├── Index.razor              <- split layout: canvas + splitter + editor panel
    │   └── Index.razor.cs           <- game loop + compile/run + example loading
    └── wwwroot/
        ├── index.html               <- Wasm JS libs + game loop bootstrap + Monaco loader
        ├── css/app.css
        └── js/
            ├── decode.min.js        <- Brotli decompressor
            └── monaco-interop.js    <- Monaco editor JS interop (init, get/set value, diagnostics)
```

---

## Step-by-Step Implementation Plan

### Phase 1: Minimal KNI Canvas (no compilation) — COMPLETE

1. ~~Create standalone git repo at `XnaFiddle/`~~
2. ~~Add KniSB as git submodule with recursive init (WasmSB, StbImage, etc.)~~
3. ~~Create solution and Blazor WASM project~~
4. ~~Set up `index.html` with Wasm JS libraries and game loop~~
5. ~~Create `Index.razor` with full-screen canvas~~
6. ~~Hardcode `SampleGame` (cycles background color to prove game loop works)~~
7. ~~Verify it builds (0 errors) and runs in browser (HTTP 200)~~

**Result**: KNI platform works standalone without Gum. Opens in Visual Studio via `XnaFiddle.sln` and runs with F5.

### Phase 2: Add Compilation + Editor UI — COMPLETE

8. ~~Add NuGet packages: `Microsoft.CodeAnalysis.CSharp`, `MetadataReferenceService.BlazorWasm`~~
9. ~~Add `NavigationManager` to `Program.cs` (needed by MetadataReferenceService)~~
10. ~~Add `CompilationService.cs` — extracted Roslyn compilation logic~~
11. ~~Change `Index.razor` layout: canvas on top (55vh), editor below (45vh)~~
12. ~~Add textarea with default sample code, Compile & Run button, diagnostics output~~
13. ~~Wire up compilation → direct in-memory assembly loading → Game instantiation~~
14. ~~Add error display panel for compilation diagnostics~~
15. ~~Handle edge cases (no Game class found, compilation errors, runtime crashes)~~
16. ~~Wrap `_game.Tick()` in try/catch to prevent runtime errors from killing the loop~~

**Result**: End-to-end compile-and-run flow works. User writes code in textarea, clicks Compile & Run, Roslyn compiles in-browser, Game subclass is instantiated and runs on the canvas. Dark-themed VS Code-style editor panel.

**Known caveat**: Recompilation (disposing an existing Game and creating a new one on the same WebGL canvas) is untested. It may work or may require a page reload fallback.

### Phase 3: Enhancements

#### 3a. Monaco Editor — COMPLETE

16. ~~Replace textarea with Monaco editor loaded from CDN (`monaco-editor@0.45.0`)~~
17. ~~Created `wwwroot/js/monaco-interop.js` — JS interop layer (init, getValue, setValue, setDiagnostics, clearDiagnostics)~~
18. ~~Added structured `DiagnosticInfo` to `CompilationService.CompilationResult` (line, column, message, severity)~~
19. ~~Compilation errors and warnings now appear as inline Monaco markers (red/yellow squiggles) in addition to the diagnostics panel~~
20. ~~Editor uses VS Code dark theme (`vs-dark` base), C# syntax highlighting, automatic layout~~

**Result**: Full Monaco editor with C# syntax highlighting, inline error markers, and VS Code-style editing experience. Replaces the plain textarea.

#### 3b. Example Gallery — COMPLETE

21. ~~Created `Examples/` folder with `.cs` files excluded from compilation, embedded as resources~~
22. ~~Examples: ColorCycle, Checkerboard (SpriteBatch tiles), BouncingBall (physics), MouseTrail (input)~~
23. ~~Created `ExampleGallery.cs` — reads embedded resources by name~~
24. ~~Added dropdown selector to toolbar; selecting an example loads it into Monaco~~
25. ~~Default code now loaded from `ColorCycle.cs` instead of inline constant~~

**Result**: Dropdown in toolbar lets users pick from built-in examples. Each example is a standalone `.cs` file in the project, easy to maintain and extend.

#### 3c. Future Enhancements

- Add IntelliSense / autocomplete via Roslyn semantic model
- Drag+drop `.cs` file loading

---

## Phase 2 Design Details (as implemented)

### Compilation Strategy: Direct In-Memory Loading

The original plan used a `?ilurl=` page-reload pattern (from Gumknix). This was abandoned because **Blob URLs are tied to the document that created them** — a `forceLoad: true` navigation destroys the page and the blob before the new page can fetch it.

**Final approach**: Direct in-memory assembly loading with no page reload.

1. User writes code in textarea, clicks **Compile & Run**
2. `CompilationService.CompileAsync()` compiles to IL bytes via Roslyn
3. `Assembly.Load(ilBytes)` loads the assembly directly in-memory
4. Reflection finds the `Game` subclass, `Activator.CreateInstance()` creates it
5. `game.Run()` starts the game on the existing canvas

### Threading Workaround: `_pendingCompile` Flag

Roslyn's internal `Monitor` usage fails in Blazor WASM's single-threaded runtime when called from Blazor's event dispatch context ("Cannot wait on monitors on this runtime"). The fix:

- The `@onclick` handler sets `_pendingCompile = true` and returns immediately
- `TickDotNet()` (called by `requestAnimationFrame`) picks up the flag and fires `_ = DoCompileAndRun()`
- This runs compilation in the rAF context (same approach Gumknix uses), avoiding the Monitor issue

### Assembly Reference Resolution: Hardcoded KNI Assembly Names

`AppDomain.CurrentDomain.GetAssemblies()` only returns assemblies that have been **lazily loaded** into the runtime. When no game is running yet, only ~1 KNI assembly (`Xna.Framework.Game`) is present out of ~18 needed. This caused `CS0234` errors ("Graphics does not exist in namespace Microsoft.Xna.Framework").

**Fix**: A hardcoded `KniAssemblyNames` array ensures all 18 KNI/Wasm assembly names are always included in the metadata reference set, regardless of AppDomain state. `MetadataReferenceService.BlazorWasm` fetches them from `_framework/` as `.wasm` (Webcil) files, converts to PE, and creates MetadataReferences. After this fix, reference count went from 53/53 to 70/70 and all CS0234 errors resolved.

### CompilationService.cs (actual)

Key differences from the original plan:
- No namespace-to-module map / `BuildNamespaceToModuleMap()` — replaced with simpler approach: include **all** loaded assemblies + hardcoded KNI list
- Returns `byte[] ILBytes` instead of a loaded `Assembly` (loading happens in the caller)
- Only logs error-severity diagnostics (not warnings)

See `XnaFiddle.BlazorGL\CompilationService.cs` for the full implementation (~130 lines).

---

## Key Risks & Mitigations

| Risk | Status | Mitigation |
|---|---|---|
| KNI platform depends on Gum at build time | **Resolved** | `Kni.Platform.Blazor.GL` has zero Gum dependencies. Phase 1 proved this. |
| `MetadataReferenceService` needs assembly DLLs served as static files | **Resolved** | Handled automatically by Blazor WASM — assemblies served as `.wasm` (Webcil) from `_framework/`. |
| Roslyn `Monitor` fails in single-threaded WASM | **Resolved** | Run compilation from `requestAnimationFrame` context via `_pendingCompile` flag, not from Blazor event handler. |
| KNI assemblies not loaded at compile time | **Resolved** | Hardcoded `KniAssemblyNames` array bypasses lazy-loading gap. |
| Blob URLs destroyed on page reload | **Resolved** | Abandoned `?ilurl=` redirect; use direct `Assembly.Load(ilBytes)` in-memory instead. |
| WebGL context can't be reused after Game.Dispose() | **Untested** | Direct in-memory reloading disposes the old game and creates a new one on the same canvas. May need page-reload fallback if WebGL context reuse fails. |
| Large WASM download size due to Roslyn | **Accepted** | Expected ~15-20MB. Brotli compression available in JS bootstrap. |
| User code crashes the runtime | **Resolved** | `_game.Tick()` wrapped in try/catch. Errors displayed in diagnostics panel, game reference nulled. |

---

## Submodule Setup

XnaFiddle uses KniSB directly as a git submodule (not through GumSB):

```
[submodule "Submodules/KniSB"]
    url = https://github.com/squarebananas/kniSB.git
    branch = Work-In-Progress-(Gumknix)
```

KniSB itself has nested submodules (initialized recursively):
- `Submodules/WasmSB` — Wasm DOM/Canvas/Audio interop
- `ThirdParty/StbImageSharp`, `StbImageWriteSharp` — image codec (compiled into KNI platform)
- `ThirdParty/Dependencies`, `NVorbis`, `SDL_GameControllerDB` — other KNI dependencies

---

## Summary

The core extraction is straightforward because Gumknix has clean layering:

- **KNI platform** (`Kni.Platform.Blazor.GL`) is independent of Gumknix/Gum — confirmed by Phase 1
- **Roslyn compilation** logic extracted into a ~130-line `CompilationService`
- **Game loop** (`initRenderJS`/`tickJS` + `TickDotNet`) is ~30 lines of JS + ~20 lines of C#
- **Game loading** uses direct `Assembly.Load(ilBytes)` — simpler than Gumknix's `?ilurl=` redirect

### Lessons Learned

1. **Blazor WASM is single-threaded**: Roslyn's `Monitor` usage fails from Blazor event handler context. Must run compilation from `requestAnimationFrame` context.
2. **Lazy assembly loading**: `AppDomain.CurrentDomain.GetAssemblies()` is unreliable for discovering KNI assemblies before a game has run. Hardcode known assembly names.
3. **Blob URLs don't survive navigation**: `forceLoad: true` destroys the blob before the new page loads. Direct in-memory loading is simpler and more reliable.
4. **MetadataReferenceService.BlazorWasm works well**: Fetches `.wasm` files from `_framework/`, converts Webcil→PE, provides MetadataReferences. The service itself is solid — the issue was always about which assembly names to ask for.
