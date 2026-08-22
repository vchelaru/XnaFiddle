---
description: Step-by-step checklist for adding a new third-party library to XnaFiddle — NuGet package, assembly registration, version display, intro page link, and example. Load when adding a library, integrating a new package, or making a new dependency available to user code.
---

# Add a New Library to XnaFiddle

This skill walks through making a third-party library available to user code in XnaFiddle. Adding a library touches five locations — miss one and compilation, version display, or discoverability will break.

## Checklist (all five are required)

### 1. Add the NuGet package to the .csproj

In `XnaFiddle.BlazorGL/XnaFiddle.BlazorGL.csproj`, add a `PackageReference` inside the unconditioned game-libraries `ItemGroup` alongside existing libraries (Apos.Shapes, Gum, KNI.Extended):

```xml
<ItemGroup>
  ...
  <PackageReference Include="NewLib.KNI" Version="x.y.z" />
</ItemGroup>
```

If the package ships MSBuild content targets that conflict with XnaFiddle (e.g. copying content files), add a property to skip them in the top `<PropertyGroup>` — see `<SkipAposShapeContent>true</SkipAposShapeContent>` for the pattern.

### 2. Register assemblies in `KniAssemblyNames`

In `CompilationService.cs`, add every assembly the package ships to the `KniAssemblyNames` array (lines ~28-56). This ensures Blazor's lazy loader has them in the AppDomain before Roslyn tries to resolve types.

**How to find the assembly names:** After adding the NuGet, run `dotnet build`, then inspect the `bin/` output or the package's `.nuspec` for `.dll` names (without the `.dll` extension). Include all transitive assemblies the package brings that are not already in the list.

### 3. Add a version display entry in `versionTargets`

In `CompilationService.cs`, add a tuple to the `versionTargets` array (lines ~103-109) so the library's version appears in the diagnostics panel after compilation:

```csharp
(string Label, string[] AsmNames)[] versionTargets =
[
    ...
    ("NewLib", ["NewLib.KNI"]),  // label shown in UI, assembly to read version from
];
```

`AsmNames` is checked in order — the first assembly with a real version wins. Use the primary assembly of the library.

### 4. Add a link on the welcome/intro page

In `Index.razor` (~lines 504-517), add a new library link in the "Available libraries" section. Follow the existing pattern — each link is an `<a>` tag with hover styles, separated by a `·` dot span:

```html
<span style="margin: 0 8px; color: #444;">·</span>
<a href="https://..." target="_blank"
   style="color: #666; text-decoration: none;"
   onmouseover="this.style.color='#999'" onmouseout="this.style.color='#666'">NewLib</a>
```

### 5. Create a sample example

Follow the **add-example** skill to create a new example in `Examples/`. The example should demonstrate the library's core feature in a minimal, self-contained way. This ensures the library appears in the example dropdown and users can see how to use it immediately.

## Key files

| File | What to change |
|------|---------------|
| `XnaFiddle.BlazorGL.csproj` | Add `PackageReference` (+ optional skip-content property) |
| `CompilationService.cs` | Add to `KniAssemblyNames[]` and `versionTargets[]` |
| `Pages/Index.razor` | Add link in "Available libraries" section |
| `Examples/NewLibExample.cs` | New example file (see add-example skill) |

## Licensing

If the example created in step 5 bundles third-party assets (fonts, images, etc.), update `THIRD-PARTY-NOTICES.md` — see the **add-example** skill for details.

## Gotchas

- **Transitive assemblies matter.** If the library depends on other assemblies not already in `KniAssemblyNames`, add those too — otherwise Roslyn compilation will fail with missing-type errors.
- **Assembly name != NuGet package name.** The NuGet ID (e.g. `Gum.KNI`) often differs from the assembly names it ships (e.g. `KniGum`, `GumCommon`). Always check the actual `.dll` names.
- **Order matters in `versionTargets`.** The first assembly in `AsmNames` with a valid version is displayed. Put the primary assembly first.
- **Build and test.** After all changes, `dotnet build` must succeed, and the new library should appear in the version info line after a compilation in the browser.
