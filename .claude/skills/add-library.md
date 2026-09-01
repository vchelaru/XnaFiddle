---
description: Step-by-step checklist for adding a new third-party library to XnaFiddle — NuGet package, ILibraryPlugin, intro page link, and example. Load when adding a library, integrating a new package, or making a new dependency available to user code.
---

# Add a New Library to XnaFiddle

This skill walks through making a third-party library available to user code in XnaFiddle. Adding a library touches four locations — miss one and compilation, version display, or discoverability will break.

## Checklist (all four are required)

### 1. Add the NuGet package to the .csproj

In `XnaFiddle.BlazorGL/XnaFiddle.BlazorGL.csproj`, add a `PackageReference` inside the unconditioned game-libraries `ItemGroup` alongside existing libraries (Apos.Shapes, Gum, KNI.Extended):

```xml
<ItemGroup>
  ...
  <PackageReference Include="NewLib.KNI" Version="x.y.z" />
</ItemGroup>
```

If the package ships MSBuild content targets that conflict with XnaFiddle (e.g. copying content files), add a property to skip them in the top `<PropertyGroup>` — see `<SkipAposShapeContent>true</SkipAposShapeContent>` for the pattern.

### 2. Create an `ILibraryPlugin` and register it

Add a plugin class in `XnaFiddle.Core/Plugins/` implementing `ILibraryPlugin` (add `IExportableLibrary` too if the library needs export support — see the **project-export** skill). This single class now covers what used to be two separate `CompilationService.cs` edits:

- `RequiredAssemblies` — every assembly the package ships (force-loaded into the AppDomain and added as Roslyn references every compile — see `ForceLoadAssemblies`/`GetMetadataReferencesAsync` in `CompilationService.cs`). Assembly name often differs from the NuGet package ID — check the package's `.nuspec`/`bin/` output for actual `.dll` names.
- `VersionAssemblies` — assemblies checked in order for the diagnostics-panel version string (`Name version` — first one with a real version wins); return `[]` to contribute no banner entry.

Register the new plugin in `XnaFiddle.BlazorGL/Program.cs` (`libraryRegistry.Register(new NewLibPlugin())`). See `GumPlugin`/`MlemPlugin` for the pattern, including `CleanUp()` for resetting library statics between runs (see **game-lifecycle**).

### 3. Add a link on the welcome/intro page

In `Index.razor` (~lines 504-517), add a new library link in the "Available libraries" section. Follow the existing pattern — each link is an `<a>` tag with hover styles, separated by a `·` dot span:

```html
<span style="margin: 0 8px; color: #444;">·</span>
<a href="https://..." target="_blank"
   style="color: #666; text-decoration: none;"
   onmouseover="this.style.color='#999'" onmouseout="this.style.color='#666'">NewLib</a>
```

### 4. Create a sample example

Follow the **add-example** skill to create a new example in `Examples/`. The example should demonstrate the library's core feature in a minimal, self-contained way. This ensures the library appears in the example dropdown and users can see how to use it immediately.

## Key files

| File | What to change |
|------|---------------|
| `XnaFiddle.BlazorGL.csproj` | Add `PackageReference` (+ optional skip-content property) |
| `XnaFiddle.Core/Plugins/NewLibPlugin.cs` | New `ILibraryPlugin` (+ `IExportableLibrary`) implementation |
| `XnaFiddle.BlazorGL/Program.cs` | `libraryRegistry.Register(new NewLibPlugin())` |
| `Pages/Index.razor` | Add link in "Available libraries" section |
| `Examples/NewLibExample.cs` | New example file (see add-example skill) |

## Licensing

If the example created in step 4 bundles third-party assets (fonts, images, etc.), update `THIRD-PARTY-NOTICES.md` — see the **add-example** skill for details.

## Gotchas

- **Transitive assemblies matter.** If the library depends on other assemblies not already covered by another registered plugin's `RequiredAssemblies`, add those too — otherwise Roslyn compilation will fail with missing-type errors.
- **Assembly name != NuGet package name.** The NuGet ID (e.g. `Gum.KNI`) often differs from the assembly names it ships (e.g. `KniGum`, `GumCommon`). Always check the actual `.dll` names.
- **Order matters in `VersionAssemblies`.** The first assembly with a valid version is displayed. Put the primary assembly first.
- **Build and test.** After all changes, `dotnet build` must succeed, and the new library should appear in the version info line after a compilation in the browser.
