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

#### 2. Create a Plugin Class (`Plugins/`)

Create a class in `XnaFiddle.BlazorGL/Plugins/` that implements `ILibraryPlugin` and `IExportableLibrary`. This single class handles assembly registration, version display, export detection, package mapping, and static state cleanup.

Here is a minimal example (Apos.Shapes — no cleanup needed):

```csharp
public class AposShapesPlugin : ILibraryPlugin, IExportableLibrary
{
    public string Name => "Apos.Shapes";
    public string[] RequiredAssemblies => ["Apos.Shapes.KNI"];
    public (string Label, string[] AssemblyNames) VersionInfo => ("Apos.Shapes.KNI", ["Apos.Shapes.KNI"]);

    public void CleanUp() { }

    public bool IsUsedInSource(string source) => source.Contains("Apos.Shapes");

    public List<ExportPackage> GetExportPackages(ExportTarget target, string source) =>
    [
        new() { Id = target.IsKni() ? "Apos.Shapes.KNI" : "Apos.Shapes", Version = PackageVersions.AposShapes }
    ];
}
```

**`ILibraryPlugin`** members:

- **`Name`** — Display name shown in diagnostics.
- **`RequiredAssemblies`** — KNI assembly names that Roslyn needs for compilation. If the library has multiple assemblies (e.g. a base package + platform package), list all of them.
- **`VersionInfo`** — Label and assembly names for the version banner in the diagnostics panel.
- **`CleanUp()`** — Reset any global/static state the library holds between game runs. If the library has no static state, leave the method body empty. If it does, use reflection to reset the relevant statics (see `GumPlugin.cs` for a thorough example). Cleanup must be idempotent — safe to call when the library hasn't been initialized, and safe to call multiple times.

**`IExportableLibrary`** members:

- **`IsUsedInSource(string source)`** — Return true if the source code uses this library. Typically `source.Contains("SomeNamespace")`. Pick a detection string specific enough to avoid false positives but general enough to catch fully qualified references.
- **`GetExportPackages(ExportTarget target, string source)`** — Return the NuGet packages to include in the exported project. Use `target.IsKni()` to choose between KNI and MonoGame package IDs. For libraries with optional sub-packages, inspect `source` to conditionally include them (see `MlemPlugin.cs` for an example).

#### 3. Register the Plugin (`Program.cs`)

Add your plugin to the registration block in `Program.cs`:

```csharp
libraryRegistry.Register(new NewLibraryPlugin());
```

If the library's assemblies need eager loading (to support shared `#code=` links before the library is actually used), add a `typeof()` touch at the top of `Main()`:

```csharp
_ = typeof(NewLibrary.SomeType);  // NewLibrary.KNI
```

#### 4. Example (`Examples/`)

Create an example `.cs` file in `XnaFiddle.BlazorGL/Examples/`. The file:

- Must **not** define a namespace (class goes at the top level)
- Should use `Game1` as the class name
- Should guard `GraphicsProfile.HiDef` with `IsProfileSupported` if used
- Should demonstrate the library's key features with minimal, clear code

Register the example in `ExampleGallery.cs` so it appears in the dropdown. You can also add them to the table of examples in the [README](README.md).

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

#### 7. Optional: Security Exceptions

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
