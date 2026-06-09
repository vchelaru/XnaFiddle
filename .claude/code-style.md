# Code Style

Conventions for C# in this repo, derived from the existing code. The overriding rule is
**match the surrounding file**; the points below capture the project-specific things that
aren't obvious from generic C# style. (Workflow — minimal diffs, tests, etc. — lives in
`.claude/agents/coder.md`, not here.)

## Nullable & usings — differ by project (important)

| Project | TargetFramework | `Nullable` | `ImplicitUsings` |
|---|---|---|---|
| `XnaFiddle.BlazorGL` | `net8.0-browser` | **disable** | **disable** |
| `XnaFiddle.Core` | `net8.0` | **disable** | **disable** |
| `XnaFiddle.Tests` | `net8.0` | **enable** | **enable** |

In **BlazorGL** and **Core** (the production code):
- Write **explicit `using` directives** — there are no implicit/global usings.
- **Do not** add nullable reference annotations (`string?`, `!`, `#nullable`) — the feature
  is off; they'll read as noise or warnings. (The null-*conditional* operator `?.` / `??` is
  unrelated and fine — it's used, e.g. `effect.Parameters["X"]?.SetValue(...)`.)

In **XnaFiddle.Tests**, nullable and implicit usings are on — write idiomatic nullable-aware
test code there.

## Formatting

- **Allman braces** (opening brace on its own line), 4-space indentation.
- **Collection expressions** `[]` are preferred for new/empty collections
  (`List<string> x = [];`, `string[] a = ["BLAZORGL"];`).
- Types: the codebase is pragmatic — explicit types are common, `var` is used where the type
  is obvious. Match the surrounding code rather than converting either way.
- Fields are typically declared without an explicit `private` modifier (e.g. `Game _game;`).

## Comments — explain *why*

This is the strongest convention in the codebase. Production code is heavily commented with the
**rationale** for non-obvious KNI / Blazor-WASM / WebGL workarounds, and cites the relevant
GitHub issue where one exists (e.g. `// ... See issue #25.`). When you write a workaround,
explain *why* it's needed and what breaks without it — don't just restate the code. Avoid
noise comments that narrate obvious statements.

## Submodules are off-limits

`Submodules/KniSB/**` (KNI and the nested WasmSB) is third-party and must **not** be modified.
When you need to influence KNI behavior, do it from the app layer. If you must reach KNI
internals, resolve the type **by name via reflection** and wrap it in a swallowing `try/catch`
(see `XnaFiddle.Core/Plugins/GameWindowPlugin.cs`, which clears KNI static caches this way) —
never take a compile-time dependency on a browser-only KNI type from `XnaFiddle.Core`.

## Where code goes (so it stays testable)

- Platform-agnostic logic that should be unit-tested goes in **`XnaFiddle.Core`** (`net8.0`),
  referenced by `XnaFiddle.Tests`.
- Browser-only code (anything touching KNI's Blazor platform, WebGL, JS interop, or
  `ShadowDusk.Wasm`) lives in **`XnaFiddle.BlazorGL`** (`net8.0-browser`). Code that must also
  compile in the net8.0 test build is gated with `#if` constants (e.g. `SHADOWDUSK`, `BLAZORGL`)
  and `#if`-ed out (as a no-op) when the browser-only dependency is absent.
