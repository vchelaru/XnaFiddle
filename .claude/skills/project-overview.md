# XnaFiddle Project Overview

XnaFiddle is a **standalone web-based KNI game runner** with an in-browser C# code editor. It lets users write XNA/MonoGame-style C# game code in the browser, compile it with Roslyn (fully client-side, no server needed), and run it immediately in a WebGL canvas — all within a Blazor WebAssembly app.

## What it does

- Users write a C# class that extends `Microsoft.Xna.Framework.Game`
- They click **Compile & Run** — Roslyn compiles the code to IL in-browser
- The compiled assembly is loaded via `Assembly.Load(ilBytes)` and the game starts in the WebGL canvas
- Monaco editor provides syntax highlighting and inline error markers
- Example gallery ships several built-in demos (ColorCycle, BouncingBall, Checkerboard, MouseTrail, GumUI, AposShapes)
- Users can drag-and-drop asset files (images, etc.) which are loaded into `InMemoryContentManager`
- Share buttons encode the current code into a gzip+base64 URL hash (`#code=...`)

## Tech stack

- **Blazor WASM**, .NET 8
- **KNI** (fork of MonoGame) via the `KniSB` git submodule — provides `Game`, `GraphicsDevice`, WebGL rendering, input
- **Roslyn** (`Microsoft.CodeAnalysis.CSharp`) for in-browser compilation
- **MetadataReferenceService.BlazorWasm** for fetching assembly metadata over HTTP (needed by Roslyn)
- **Monaco Editor** (v0.45.0, loaded from CDN) for the code editor, with JS interop via `wwwroot/js/monaco-interop.js`

## Development status

- Phase 1 (KNI Canvas): COMPLETE
- Phase 2 (Compilation + Editor UI): COMPLETE
- Phase 3a (Monaco Editor): COMPLETE
- Phase 3b (Example Gallery): COMPLETE
- Phase 3c (IntelliSense, drag+drop file loading): TODO

## Build & run

```bash
dotnet build XnaFiddle.BlazorGL/XnaFiddle.BlazorGL.csproj
# App runs on https://localhost:60440 (or http://localhost:60441)
```
