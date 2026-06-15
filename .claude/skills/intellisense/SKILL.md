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

`CreateWasmSafeHostServices` in `IntellisenseService.cs` builds `MefHostServices` from `MefHostServices.DefaultAssemblies` but filters out `Microsoft.CodeAnalysis.Host.DefaultPersistentStorageConfiguration`. Its static ctor calls `Process.GetCurrentProcess()`, which throws `PlatformNotSupportedException` on WASM. For completion/hover/diagnostics Roslyn then falls back to `NoOpPersistentStorageService` — fine, because no code path on those requests `IPersistentStorageConfiguration` directly.

`SymbolFinder.FindDeclarationsAsync` (used by the Add-using code action) is different: it explicitly requires `IPersistentStorageConfiguration` and throws `InvalidOperationException: Service ... is required ... but is not available from 'Custom' workspace` if no part provides it. To cover that path we supply our own WASM-safe export via `WasmSafePersistentStorageProvider` (`XnaFiddle.BlazorGL/Intellisense/WasmSafePersistentStorageProvider.cs`). Because `IPersistentStorageConfiguration` and its `SolutionKey` parameter are `internal` in Roslyn 4.14, the type can't be authored in C#; instead the provider emits it via `Reflection.Emit` at runtime with `[ExportWorkspaceService(typeof(IPersistentStorageConfiguration), ServiceLayer.Host)]` + `[Shared]`. The emitted implementation returns `ThrowOnFailure = false` and `TryGetStorageLocation(_) = null` — equivalent to "no on-disk storage available," which is the truth in the browser. The original filter against `DefaultPersistentStorageConfiguration` stays, so the `Process.GetCurrentProcess()` cctor never runs.

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

## Add using hover link

Surfaces `using NS;` suggestions for an unresolved identifier as clickable markdown links inside the Roslyn hover tooltip. Triggered by hover, not Ctrl+. — hovering an unresolved identifier (e.g. `SpriteBatch` before any `using Microsoft.Xna.Framework.Graphics;`) shows the normal hover (empty if Roslyn couldn't bind), then a `---` separator, then an "Add using:" list. Clicking a link fires the `xnafiddle.addUsing` Monaco command, which inserts the `using` line at the location computed by `AddUsingSuggester.ComputeInsertion`.

**Why hover, not code-action widget.** Initial implementation used `monaco.languages.registerCodeActionProvider` + the Ctrl+. lightbulb. In this CDN-loaded Monaco 0.45 build the action-widget's internal `ListWidget` virtualization allocates widget height but renders zero visible rows — the items were there in the data model but never painted. No payload-shape fix resolved it; we pivoted to hover links which already worked reliably for everything else. The `registerCodeActionProvider` path has been fully removed — no feature flag, no fallback.

**Pipeline**:
1. `IntellisenseService.GetHoverAsync` resolves the symbol as usual; if the name is unresolved or binds to an `IErrorTypeSymbol`, it calls the private `ComputeAddUsingSuggestionsAsync` helper.
2. The helper runs the same Roslyn pipeline used by the standalone JSInvokable `GetAddUsingSuggestionsAsync`: `SymbolFinder.FindDeclarationsAsync` + `AddUsingSuggester.IsAllowedNamespace` filter + existing-usings dedupe.
3. Results are formatted by `BuildAddUsingMarkdown` into markdown `[` \`using NS;\` `](command:xnafiddle.addUsing?<urlencoded-json>)` links and appended to the hover content with a `---` separator.
4. The JS hover provider (`_registerHover` in `monaco-interop.js`) wraps the content as `{ value, isTrusted: true, supportHtml: false }`. **`isTrusted: true` is required** — Monaco silently strips `command:` URIs from untrusted markdown.
5. Clicking a link fires `xnafiddle.addUsing` (registered in `_registerAddUsingCommand`), which resolves the `__ACTIVE_MODEL__` sentinel to the current editor's model and applies a `pushEditOperations` insert.

**Why the `__ACTIVE_MODEL__` sentinel.** The command argument shape is `[modelUri, insertOffset, insertText]`. The .NET side doesn't know the model URI when generating the hover markdown, so it passes this sentinel; the JS command handler treats it as "use the current editor's active model". This avoids a round-trip purely to learn the URI.

**JSInvokable**: `GetAddUsingSuggestionsAsync(source, position)` remains on `IntellisenseService` even though the hover path calls the internal helper directly. Kept for a possible future surface (right-click menu, keyboard shortcut) that would want the suggestions without a hover.

**Not `CSharpAddImportCodeFixProvider`.** The Roslyn `Microsoft.CodeAnalysis.(CSharp.)Features` assemblies are not referenced. Reasons: (1) WASM payload is already ~15–20MB, (2) the provider is `internal` and requires reflection/IVT gymnastics, (3) composing the full CodeFixProvider/CodeRefactoringProvider MEF graph would leak dozens of unwanted quick-fixes (generate constructor, introduce variable, convert-to-switch, etc.). Instead we hand-roll a minimal path: `SymbolFinder.FindDeclarationsAsync` + allowlist filter. `SymbolFinder.FindDeclarationsAsync` requires `IPersistentStorageConfiguration`, which is why `WasmSafePersistentStorageProvider` exists (see "MEF + WASM persistent storage fix" above).

**Namespace allowlist** (`AddUsingSuggester.NamespaceAllowlist`):
- `Microsoft.Xna.Framework` — XNA/KNI types (`SpriteBatch`, `Vector2`, `GraphicsDevice`, etc.). Without the allowlist, `Vector2` would ambiguously match both `System.Numerics` and `Microsoft.Xna.Framework`, and we want the game one.
- `Gum` — Gum UI layer root namespace. The 2026-June Gum release unified the former
  `MonoGameGum.*` types into `Gum.*`, so the allowlist now contains only
  `Microsoft.Xna.Framework` and `Gum` (child-namespace matching covers `Gum.GueDeriving`,
  `Gum.Forms`, etc.).

Everything else — including `System.*` — is intentionally excluded for v1. Expand deliberately, only on concrete reports of missing useful cases.

**Prefix-match rule**: a namespace `ns` matches prefix `p` iff `ns == p || (ns.Length > p.Length && ns[p.Length] == '.' && ns.StartsWith(p))`. A bare `StartsWith` would wrongly match `GumFoo` against `Gum`. Covered by `AddUsingSuggesterTests`.

**Insertion-point calculation** (`AddUsingSuggester.ComputeInsertion`):
1. If the file has any top-level `using` directives, insert after the last one (at `Usings[^1].FullSpan.End`, which includes trailing EOL trivia).
2. Otherwise, if the file has a `FileScopedNamespaceDeclarationSyntax` (`namespace Foo;`), insert at `fileScoped.FullSpan.Start` — usings must precede it or C# won't compile.
3. Otherwise, insert at offset 0.

**Performance note**: the first `SymbolFinder.FindDeclarationsAsync` call is slow (hundreds of ms) while Roslyn builds its cross-assembly symbol index. Acceptable because the path is user-triggered only. Deliberately NOT added to `WarmupAsync` — doing so would extend startup time for a feature most sessions won't hit.

**What we do not do**: no live "missing using" squiggles, no auto-apply on paste, no expansion of the allowlist to `System.*` without an explicit ask.

**Hover dismiss after click — fragile, do not simplify.** After `xnafiddle.addUsing` applies its edit, it must dismiss the hover tooltip. Otherwise the user doesn't see the inserted `using` (hidden behind the still-open hover) and can re-click the same link multiple times. The `.NET`-side filter prevents actual duplicate inserts, but the UX is broken. The code in `_registerAddUsingCommand` (`monaco-interop.js`) dismisses by: (1) focusing the editor, (2) dispatching a synthetic `Escape` keydown at the editor's inner `<textarea>`. Things we tried first that did NOT work in this Monaco 0.45 CDN build:

- `ed.getContribution('editor.contrib.hover').hideContentHover()` — method exists but is a no-op in this build.
- `ed.trigger('source', 'closeHover', {})` — no matching action.
- Setting `display:none` on `.monaco-hover` DOM nodes — "works" once, but Monaco reuses the same element so **all future hovers break silently**. Regression-prone. Do not reintroduce.
- `ed.focus()` alone — pulls focus but doesn't dismiss if the hover is sticky-interaction-mode (which it is, since the user just clicked inside it).

Only the Escape dispatch reliably closes it. If a future Monaco upgrade changes this, prefer investigating the new official hover-dismiss API rather than adding DOM-hiding fallbacks.

## Testing

Pure-logic helpers (`XmlDocFormatter`, `SignatureFormatter`, `ActiveParameterLocator`, `AddUsingSuggester`) are unit-tested in `XnaFiddle.Tests/Intellisense/`. The `IntellisenseService` workspace/CTS/JSInvokable orchestration is **not** unit-tested — it is validated manually in the browser. New pure logic belongs in the `Intellisense/` subfolder so it stays testable outside WASM.
