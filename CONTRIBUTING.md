# Contributing to XnaFiddle

## Security Notice

**All PRs that add new third-party libraries are closely scrutinized for security reasons.** XnaFiddle runs user-submitted code in the browser — every library we include becomes part of the attack surface. Expect thorough review of the library's source, dependencies, and any APIs it exposes to user code.

---

## Adding a Third-Party Library

XnaFiddle supports game libraries like Gum, Apos.Shapes, FontStashSharp, and others. Adding a new library touches several files across the project. This guide walks through each integration point.

### Prerequisites

- The library must have a published NuGet package
- KNI and/or MonoGame variants should be available
- The library must be safe to expose to untrusted user code (no file system access, no networking, no reflection)

### Step-by-Step Checklist

#### 1. Version Property and PackageReference (`XnaFiddle.BlazorGL.csproj`)

Add a version property in the `LIBRARY VERSIONS` property group (near the top of the file):

```xml
<NewLibraryVersion>1.0.0</NewLibraryVersion>
```

Add a PackageReference for the KNI variant (so XnaFiddle can compile user code that uses it):

```xml
<PackageReference Include="NewLibrary.KNI" Version="$(NewLibraryVersion)" />
```

Add the version constant to the `GeneratePackageVersions` MSBuild target (near the bottom of the file):

```
public const string NewLibrary = "$(NewLibraryVersion)"%3B
```

This auto-generates a `PackageVersions.NewLibrary` constant that the exporter uses.

#### 2. Export Detection (`ProjectExporter.cs`)

In `BuildPackageList()`, add a source-scanning block that detects the library by namespace or type name. Do not require a `using` prefix — the detection should work with fully qualified references too:

```csharp
if (source.Contains("NewLibrary.SomeNamespace"))
{
    packages.Add(new NuGetPackage
    {
        Id = isKni ? "NewLibrary.KNI" : "NewLibrary",
        Version = PackageVersions.NewLibrary
    });
}
```

Pick a detection string that is specific enough to avoid false positives but general enough to catch all usage patterns.

#### 3. Roslyn Assembly Resolution (`CompilationService.cs`)

Add the library's assembly name(s) to the `KniAssemblyNames` array so Roslyn can resolve types at compile time:

```csharp
"NewLibrary.KNI",
```

If the library has multiple assemblies (e.g. a base package + platform package), add all of them.

Optionally, add an entry to `versionTargets` to display the library version in the diagnostics panel:

```csharp
("NewLibrary", ["NewLibrary.KNI"]),
```

#### 4. Example (`Examples/`)

Create an example `.cs` file in `XnaFiddle.BlazorGL/Examples/`. The file:

- Must **not** define a namespace (class goes at the top level)
- Should use `Game1` as the class name
- Should guard `GraphicsProfile.HiDef` with `IsProfileSupported` if used
- Should demonstrate the library's key features with minimal, clear code

Register the example in `ExampleGallery.cs` so it appears in the dropdown.

#### 5. Welcome Page Library List (`Pages/Index.razor`)

The welcome screen displays an "Available libraries" list near the bottom. Add a link for your library following the existing pattern — a `·` dot separator followed by an `<a>` tag pointing to the library's docs or repository:

```html
<span style="margin: 0 8px; color: #444;">·</span>
<a href="https://example.com/newlibrary-docs" target="_blank"
   style="color: #666; text-decoration: none;"
   onmouseover="this.style.color='#999'" onmouseout="this.style.color='#666'">NewLibrary</a>
```

Search for the `Available libraries:` span in `Index.razor` and append your entry at the end of the list.

#### 6. Optional: Snippet Preset

If the library requires boilerplate that users shouldn't have to write every time (initialization, per-frame calls, etc.), you can add a **preset**:

1. **`SnippetModel.cs`** — Add a `public bool IsNewLibrary` property
2. **`SnippetExpander.cs`** — Add an `if (model.IsNewLibrary)` block that injects usings, member fields, and method calls into the appropriate lifecycle methods
3. **`SnippetReverter.cs`** — Add detection logic so existing full-class code can be identified as using the preset (using arrays, injected member checks, injected statement predicates)
4. **`Pages/Index.razor`** — Add a preset checkbox in the UI
5. **`Pages/Index.razor.cs`** — Add library assignment in SnippetModel creation (there are 2 locations)

Most libraries do **not** need a preset — presets are only for libraries that inject significant boilerplate (like Gum's `GumService.Initialize` / `Update` / `Draw` pattern).

#### 7. Optional: Static State Cleanup

If the library maintains global/static state between game runs (like Gum's `GumService.Default`), implement the `ILibraryPlugin` interface in a new class under `Plugins/` and register it in the `LibraryRegistry` (see `Index.razor.cs` — `CreateLibraryRegistry()`). Your plugin's `CleanUp()` method should use reflection to reset the relevant statics. See `GumPlugin.cs` for an example. The cleanup must be idempotent — safe to call when the library hasn't been initialized and safe to call multiple times.

#### 8. Optional: Security Exceptions

If the library uses namespaces or types that collide with blocked patterns in `SecurityChecker.cs`, you may need to add exceptions. This is rare and will receive extra scrutiny during review.

---

## Adding Examples

Add a `.cs` file to `XnaFiddle.BlazorGL/Examples/`. It will be embedded as a resource automatically. Register it in `ExampleGallery.cs` with a name, category, and description.

See existing examples for the expected structure.

---

## General Guidelines

- Keep PRs focused — one library or feature per PR
- Test your changes across at least KNI DesktopGL and one MonoGame target
- Test the export to verify the correct NuGet packages are included
- Run `dotnet build XnaFiddle.BlazorGL/XnaFiddle.BlazorGL.csproj` and ensure zero warnings
