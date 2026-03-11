# XnaFiddle Security Model & SecurityChecker

Use this skill when asked about security concerns, the SecurityChecker, what APIs are blocked, why something is forbidden, or how to extend the block list.

## Threat Model

XnaFiddle compiles and executes **user-supplied C# code** entirely in the browser via Roslyn + `Assembly.Load`. The code runs inside the same Blazor WASM process as the host app — there is no sandbox boundary. This means:

- Malicious code can call any .NET API that is loaded into the process.
- Reflection can bypass type-level restrictions at runtime.
- Background threads or timers can outlive the user's game session and interfere with subsequent runs.
- JS interop APIs give direct access to the DOM, clipboard, and browser APIs from managed code.
- Platform-specific KNI/nkast APIs give low-level access to the WebGL canvas and audio context.

The `SecurityChecker` (`XnaFiddle.BlazorGL/SecurityChecker.cs`) is a **Roslyn semantic analysis pass** that runs on the compiled syntax tree *before* the assembly is loaded. It rejects code that references forbidden APIs by walking every `IdentifierNameSyntax` and `GenericNameSyntax` node and resolving each one to its `ISymbol` via the semantic model.

## What Is Blocked and Why

### Forbidden Namespaces (entire subtree blocked)

| Namespace | Reason |
|---|---|
| `System.Reflection` | Allows runtime inspection and invocation of any private member, bypassing all type-level checks. |
| `System.Reflection.Emit` | Allows generating new IL at runtime, producing assemblies that were never security-checked. |
| `System.Linq.Expressions` | `Expression.Compile()` JIT-compiles arbitrary delegates at runtime — effectively Emit-lite. |
| `System.Runtime.InteropServices` | `Marshal` and P/Invoke can read/write raw memory and call native code. |
| `System.Runtime.InteropServices.JavaScript` | WASM-specific JS interop that bypasses `Microsoft.JSInterop` restrictions. |
| `System.Net` | Allows outbound HTTP requests (data exfiltration, C2). |
| `System.Security` | Cryptography, permissions, and security policy manipulation. |
| `Microsoft.CodeAnalysis` | Prevents user code from invoking the compiler itself (self-modification, bypass). |
| `Microsoft.JSInterop` | Direct Blazor JS interop — would give access to `eval`, DOM, cookies, etc. |
| `nkast.Wasm.*` | Low-level KNI platform APIs for canvas, audio, clipboard, XHR, XR — direct browser API access. |
| `TextCopy` | Clipboard read/write — privacy concern. |
| `Kni.Platform` | Internal KNI platform bootstrapping; not part of the public game API. |

### Forbidden Types (namespace not fully blocked)

| Type | Reason |
|---|---|
| `System.IO.File` / `Directory` / `FileStream` | Filesystem access (not meaningful in WASM but blocks intent). |
| `System.Diagnostics.Process` | Shell command execution. |
| `System.AppDomain` | Assembly loading, domain manipulation. |
| `System.Runtime.Loader.AssemblyLoadContext` | Direct assembly loading (bypasses SecurityChecker). |
| `System.Runtime.CompilerServices.Unsafe` | Raw pointer arithmetic and memory reinterpretation. |
| `System.Threading.Thread` | Explicit thread creation (WASM doesn't support threads, but blocks intent). |
| `System.Threading.Timer` / `System.Timers.Timer` | Background timers that outlive the game session ("zombie" background work). |
| `System.Threading.SynchronizationContext` | Allows posting callbacks to the Blazor sync context. |
| `System.Threading.Tasks.Parallel` | Background task fan-out. |
| `System.Diagnostics.StackTrace` | Stack inspection — can reveal internals and be used as an oracle for reflection bypasses. |
| `System.Environment` | `Environment.Exit` would kill the process; env variable access leaks host info. |
| `System.Activator` | `Activator.CreateInstance` with a `Type` handle can bypass normal constructors, pair with reflection. |

### Forbidden Methods (type not fully blocked)

`Task` and `ThreadPool` are partially allowed (async/await works fine) but background-spawning methods are blocked:

| Method | Reason |
|---|---|
| `Task.Run` | Spawns work on the thread pool — creates a task that outlives the game session. |
| `Task.Start` | Same as `Task.Run` for manually-constructed tasks. |
| `TaskFactory.StartNew` | Alternative `Task.Run` path. |
| `ThreadPool.QueueUserWorkItem` | Direct thread pool dispatch. |
| `ThreadPool.UnsafeQueueUserWorkItem` | Same, without execution-context capture. |

### Forbidden Keyword

**`dynamic`** — detected via `TypeKind.Dynamic` on `IdentifierNameSyntax`. Dynamic dispatch bypasses Roslyn's static type resolution, meaning the security checker's symbol lookup returns `null` and the check is silently skipped. This was a known exploit pattern (assign a `MethodInfo` to `dynamic`, call `Invoke` without a static `System.Reflection` reference).

## What Is Allowed

Safe I/O: `MemoryStream`, `Stream`, `BinaryReader`, `BinaryWriter` — needed for asset loading.
Diagnostics: `Debug.WriteLine` — useful for debugging games, harmless in WASM.
Tasks: `Task.Delay`, `Task.WhenAll`, `Task.WhenAny`, `TaskCompletionSource`, `async`/`await` — async game patterns work.
LINQ: `System.Linq` (but not `System.Linq.Expressions`) — standard query operators.
Collections, Math, String, etc. — all standard BCL types that don't touch platform or runtime internals.

## How SecurityChecker Works

```
SecurityChecker.Check(compilation, syntaxTree)
  ├── Walk all DescendantNodes()
  ├── Special-case: IdentifierNameSyntax "dynamic" → check TypeKind.Dynamic
  ├── Filter to IdentifierNameSyntax | GenericNameSyntax only (leaf names)
  ├── Resolve ISymbol via SemanticModel.GetSymbolInfo()
  └── GetForbiddenMessage(symbol)
       ├── Namespace check: symbol.ContainingNamespace vs ForbiddenNamespaces
       │    (prefix match: "System.Reflection" blocks "System.Reflection.Emit" too)
       ├── Type check: symbol.ContainingType (strips generics) vs ForbiddenTypes
       └── Method check: ContainingType.Name vs ForbiddenMethods
```

Errors include precise line/column positions and are surfaced as Monaco editor markers with `"error"` severity.

## Extending the Block List

- **New namespace**: add to `ForbiddenNamespaces`. Prefix matching is automatic.
- **New type within an allowed namespace**: add the fully-qualified name to `ForbiddenTypes` (e.g. `"System.IO.FileStream"`).
- **Specific method on an allowed type**: add `"FullTypeName.MethodName"` to `ForbiddenMethods`.
- **New keyword/pattern**: add a special-case before the `IdentifierNameSyntax` filter in `Check()`.

Always add a corresponding test in `XnaFiddle.Tests/SecurityCheckerTests.cs` — both a "IsBlocked" test and, if the namespace is partially allowed, an "IsAllowed" test for safe members.

## Limitations

- **Runtime-only patterns**: SecurityChecker is a static analysis pass. Sufficiently obfuscated code (e.g., calling `Type.GetType(someString).GetMethod(...)` where the string is computed) could evade it. The assumption is that XnaFiddle is a hobbyist tool, not a hardened sandbox.
- **Reflection on `typeof`/`GetType()`**: `typeof(Foo)` and `obj.GetType()` are allowed (needed for normal code). A user with a `Type` handle still can't call `Assembly.GetExecutingAssembly()` or `MethodInfo.Invoke` because those types/methods are blocked, but creative use of delegates or runtime dispatch through allowed generics might find gaps.
- **WASM-only namespaces**: `System.Runtime.InteropServices.JavaScript` cannot be tested in the .NET desktop test host (it's WASM-specific). The block is present but untested via semantic resolution.
