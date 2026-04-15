---
description: Roslyn-backed language services (completion, hover, signature help, live diagnostics) hosted in-browser via Blazor WASM and wired to Monaco. Load when the user asks about IntelliSense, autocomplete, hover tooltips, signature help, diagnostics squiggles, Monaco editor integration, or any Roslyn-backed editor feature in this repo.
---

## Architecture

One `IntellisenseService` singleton (DI, app lifetime) holds an `AdhocWorkspace` + a single `User.cs` document. Every JSInvokable call swaps document text via `Solution.WithDocumentText(_documentId, ...)` + `_workspace.TryApplyChanges(...)`, then drives Roslyn:

- Completion: `CompletionService.GetService(document).GetCompletionsAsync(...)`
- Hover: `SemanticModel.GetDeclaredSymbol` / `GetSymbolInfo` + custom `SymbolDisplayFormat`
- Signature help: walk syntax tree for enclosing `InvocationExpressionSyntax`, resolve via `SemanticModel.GetSymbolInfo`, format manually
- Live diagnostics: `Project.GetCompilationAsync().GetDiagnostics()`

`SignatureHelpService` and `QuickInfoService` are **internal** in Roslyn 4.14, so hover and sig help bypass them and drive the SemanticModel directly. Metadata references are pulled from `CompilationService.GetMetadataReferencesAsync` so the completion surface exactly matches the compile surface.

## Key files

| File | Purpose |
|---|---|
| `XnaFiddle.BlazorGL/IntellisenseService.cs` | Singleton; workspace, CTS-per-op, all four JSInvokables |
| `XnaFiddle.BlazorGL/Intellisense/XmlDocFormatter.cs` | XML doc -> Monaco-flavor markdown |
| `XnaFiddle.BlazorGL/Intellisense/SignatureFormatter.cs` | Builds label + per-param offset ranges |
| `XnaFiddle.BlazorGL/Intellisense/ActiveParameterLocator.cs` | Top-level-comma counter for active param |
| `XnaFiddle.BlazorGL/wwwroot/js/monaco-interop.js` | Monaco providers, debounce, cache, marker layers |
| `XnaFiddle.BlazorGL/Pages/Index.razor[.cs]` | DI wiring, warmup kickoff, loading indicator |
| `XnaFiddle.Tests/Intellisense/*Tests.cs` | Tests for the pure-logic helpers only |

## WASM single-thread constraint (critical)

All Roslyn work runs on the one WASM thread; a long call blocks typing. Three layered mitigations:

1. **JS-side tiered debounce** in `_registerCompletions`:
   - `.` (member access): 50ms
   - Ctrl+Space on empty word: 0ms (fire immediately)
   - Typing with word length >= 3: 400ms
   - Typing with word length < 3: **skipped entirely** (returns `{suggestions: []}`)
2. **Word-start cache** (`_completionCache`, keyed by `{sourcePrefix, wordStartOffset}`): while the user types inside the same word, Monaco filters client-side against the cached result and no .NET call happens.
3. **Per-op `CancellationTokenSource`** in `IntellisenseService` (`_currentCompletionCts`, `_currentSignatureHelpCts`, `_currentHoverCts`, `_currentDiagnosticsCts`): a new request on the same channel cancels the prior one mid-flight. Separate CTS per op means sig help and completion don't cancel each other.

## Gotcha: Monaco trigger kinds

Monaco sends `CompletionTriggerKind.Invoke (0)` for **both** Ctrl+Space **and** normal auto-trigger-while-typing. There is no "typing" kind. Never branch on `Invoke` alone to pick a fast path — branch on `triggerCharacter === '.'` first, then fall through to the word-length rules. `TriggerForIncompleteCompletions (2)` must return cached-or-empty; never call .NET there.

## MEF + WASM persistent storage fix

`CreateWasmSafeHostServices` in `IntellisenseService.cs` builds `MefHostServices` from `MefHostServices.DefaultAssemblies` but filters out `Microsoft.CodeAnalysis.Host.DefaultPersistentStorageConfiguration`. Its static ctor calls `Process.GetCurrentProcess()`, which throws `PlatformNotSupportedException` on WASM. With the part excluded, Roslyn falls back to `NoOpPersistentStorageService` — correct for in-browser (no disk anyway).

## Unimported-namespace completions

`TryEnableUnimportedNamespaceCompletions` reflects against the internal `Microsoft.CodeAnalysis.Completion.CompletionOptionsStorage.ShowItemsFromUnimportedNamespaces` and flips it via `OptionSet.WithChangedOption(key, LanguageNames.CSharp, true)`. Works on Roslyn 4.14. This is what lets users see e.g. `SpriteSortMode` without first writing `using Microsoft.Xna.Framework.Graphics;`. If a Roslyn upgrade silently breaks it, candidate fixes are `IgnoresAccessChecksTo` codegen or pinning the Roslyn version.

## Warmup + readiness gate

`WarmupAsync` ([IntellisenseService.cs:275](../../../XnaFiddle.BlazorGL/IntellisenseService.cs)) runs a throwaway completion against `"class C { void M() { System. } }"` to prime Roslyn's MEF composition and semantic caches (~5s first call). On success sets `IsReady=true` and raises `ReadyChanged`. `Index.razor.cs:150` subscribes before starting warmup to avoid missing the event; handler marshals to the Blazor sync context and calls `monacoInterop.setIntellisenseReady(true)`. `Index.razor:401` shows "Loading IntelliSense..." while `!Intellisense.IsReady`.

All JS providers gate on `_isIntellisenseReady` and return empty/undefined until it flips — keystrokes must never queue behind warmup.

## Signature help: active parameter highlighting

Monaco's `parameter.label` accepts either a string (substring match, brittle when two params share a type) or a **tuple `[startOffset, endOffset]`** indexing into the signature label. We use the tuple form. `SignatureFormatter.Format` builds the label character-by-character and records exact `ParameterRange` per parameter.

`ActiveParameterLocator.FindActiveParameter` counts **only** direct-child commas via `argumentList.Arguments.GetSeparators()`. Counting descendant commas would wrongly include those inside nested calls like `Foo(Bar(1, 2), |)`.

## Hover formatting

`HoverFormat` in `IntellisenseService.cs:447` is a custom `SymbolDisplayFormat` — do NOT use `MinimallyQualifiedFormat`, which omits the type-kind keyword and is too terse for a hover tooltip. Output is `signature + "\n\n" + XmlDocFormatter.Format(GetDocumentationCommentXml())`.

`XmlDocFormatter` parses the `<member>`-wrapped XML Roslyn emits and renders summary/typeparam/param/returns/remarks/exception to markdown. Inline `<see cref>` values get their `T:`/`M:`/`P:`/`F:`/`E:`/`N:` prefixes stripped. Returns empty string on any parse failure.

Expect empty XML doc output for BCL/KNI symbols — those assemblies are loaded as raw bytes without `.xml` companions. User-written `///` comments in fiddle source DO render.

## Diagnostics: two marker layers

- **Live squiggles** (typing): `monaco.editor.setModelMarkers(model, 'roslyn', ...)` — debounced 700ms in `_scheduleDiagnostics`.
- **Post-compile diagnostics**: owner `'compilation'` via `setDiagnostics` / `clearDiagnostics`.

Different owners means the two layers coexist without clobbering each other. The `_completionInFlight` flag defers a pending diagnostics run by 300ms while a completion .NET call is active, so squiggles don't fight completion for the single thread.

## Testing

Pure-logic helpers (`XmlDocFormatter`, `SignatureFormatter`, `ActiveParameterLocator`) are unit-tested in `XnaFiddle.Tests/Intellisense/`. The `IntellisenseService` workspace/CTS/JSInvokable orchestration is **not** unit-tested — it is validated manually in the browser. New pure logic belongs in the `Intellisense/` subfolder so it stays testable outside WASM.
