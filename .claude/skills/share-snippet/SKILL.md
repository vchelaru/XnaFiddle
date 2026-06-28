---
description: XnaFiddle's share/snippet round-trip — the fidelity contract between SnippetReverter (full code -> compact SnippetModel) and SnippetExpander (model -> scaffolded code), what survives a #snippet= link vs a #code= link, and the scaffold-ownership rules. Load when working on share links, the Share dialog, SnippetReverter/SnippetExpander/SnippetModel, #code=/#snippet= URL handling, or the issue #83 constructor round-trip.
---

# Share / Snippet round-trip

Three ways to share a fiddle. They differ in **fidelity** — this is the part that bites.

| Mode | URL | Fidelity |
|---|---|---|
| Code (`ShareAsCode`) | `#code=<source>` | **Lossless** — stores the editor source verbatim |
| Snippet | `#snippet=<model>` | **Lossy by design** — round-trips through `SnippetReverter` -> `SnippetModel` -> `SnippetExpander`; only scaffold-shaped content survives |
| Gist | external | Partial; see the `shaders` skill |

Shaders travel in all three. That is the `shaders` skill's territory — point there, don't re-document.

## The fidelity contract (the snippet codec)

`SnippetExpander` and `SnippetReverter` are exact inverses around a fixed **scaffold** (a `FiddleGame : Game` skeleton). Only what fits a `SnippetModel` field survives the trip; anything the scaffold can't represent is dropped. The codec is symmetric: every line the expander **injects** is a line the reverter must **strip**, or sharing-then-resharing bloats.

- **Scaffold-owned boilerplate.** The expander owns the constructor and a `GraphicsDeviceManager graphics;` field, plus the canonical method-body lines (`base.Initialize/Update/Draw`, `GraphicsDevice.Clear(...)`). The reverter strips exactly these back out (`InjectedMembers` set + the `IsInjected*` statement predicates). Keep the two sides in lockstep when editing either.
- **Constructor handoff (issue #83 — the crux).** A custom constructor used to be dropped outright. Now `SnippetModel.Constructor` carries it: when present, the expander **steps aside** — emits neither its own `graphics` field nor the default ctor and reproduces the user's verbatim. The reverter captures the ctor body **only when it differs** from the canonical scaffold ctor (`IsScaffoldConstructor`, compared on whitespace-collapsed text), so scaffold-shaped snippets stay compact. Subtlety: once a custom ctor is captured, a user field literally named `graphics` must be **kept** rather than mistaken for the injected one.
- **Presets are inferred from `using` directives.** `IsGum`/`IsAposShapes`/`IsMonoGameExtended` are detected on revert from the namespaces present (not stored explicitly), and each injects its members + method-body lines on expand / is stripped on revert. The flag set drives which injected lines the reverter recognizes.

## Call-site gotcha (Index.razor.cs)

The `SnippetModel` is built field-by-field from `_revertResult` in **two** places — `RecomputeSnippetPreview` (the live preview URL) and `CopyShareUrl` (the actual copied link). A new `SnippetModel` field wired into only one is silently dropped from the shared URL. This nearly sank the #83 fix; grep both before adding a field.

## Why these classes live in XnaFiddle.Core

The codec is browser-free so the net8.0 test project can exercise it. The test project **cannot** reference net8.0-browser `BlazorGL` (same split as the `shaders` skill / issue #26), so `SnippetReverter`/`SnippetExpander`/`SnippetModel` were moved to `Core` in the #83 fix. Tests: `XnaFiddle.Tests/SnippetRoundTripTests.cs`.

## Shallow pointers (did not cause confusion — leave shallow)

- URL gzip+base64 codec: `XnaFiddle.Core/UrlCodec.cs`.
- `&assets=` and `&shaders=` fragments, gist import/export: `Index.razor.cs` fragment builders; shaders -> `shaders` skill.

## Files

| File | Role |
|---|---|
| `XnaFiddle.Core/SnippetReverter.cs` | Full code -> `SnippetModel`; strips scaffold/preset boilerplate, captures non-canonical ctor |
| `XnaFiddle.Core/SnippetExpander.cs` | `SnippetModel` -> full code; owns the scaffold, steps aside for a custom ctor |
| `XnaFiddle.Core/SnippetModel.cs` | The compact payload fields (incl. `Constructor`, `Shaders`) |
| `XnaFiddle.BlazorGL/Pages/Index.razor.cs` | Share dialog: `ShareAsCode`, `RecomputeSnippetPreview` + `CopyShareUrl` (the two build sites), `LoadFromSnippet`/`LoadFromCode`, `#code=`/`#snippet=` parsing |
| `XnaFiddle.Tests/SnippetRoundTripTests.cs` | Round-trip unit tests |
