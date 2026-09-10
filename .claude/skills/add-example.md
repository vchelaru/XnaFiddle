# Add a New Example to XnaFiddle

This skill walks through adding a new built-in example to the XnaFiddle example gallery.

## Steps

### 1. Create the example file

Add a new `.cs` file under `XnaFiddle.BlazorGL/Examples/`:

```csharp
// XnaFiddle.BlazorGL/Examples/MyExample.cs
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public class MyExampleGame : Game
{
    GraphicsDeviceManager _graphics;

    public MyExampleGame()
    {
        _graphics = new GraphicsDeviceManager(this);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        base.Draw(gameTime);
    }
}
```

**Rules for example code:**
- Must have exactly one class extending `Game` (or `Microsoft.Xna.Framework.Game`)
- No `namespace` declaration (or use a top-level-friendly one) — the runner scans all types
- No file I/O or disk access — use `InMemoryContentManager` for assets (they are stored in `InMemoryContentManager.Files`)
- Keep `using` statements minimal; Roslyn resolves from the hardcoded `KniAssemblyNames` list

### 2. No csproj edit needed — it's a wildcard glob

`XnaFiddle.BlazorGL.csproj` embeds every file under `Examples/` via wildcards, not a per-file
entry (there is no `ExcludeFromCompile` metadata anywhere in the project):

```xml
<Compile Remove="Examples\**" />
<EmbeddedResource Include="Examples\*.cs" />
<EmbeddedResource Include="Examples\*.*" Exclude="Examples\*.cs" />
```

Dropping a new file directly under `Examples/` (not a subdirectory — the glob is `Examples\*.*`,
non-recursive) is enough; nothing to add to the `.csproj`.

### 3. Verify the gallery picks it up

`ExampleGallery.cs` reads embedded resources matching `*.Examples.*.cs` and exposes them by filename stem. The new example will automatically appear in the dropdown on the page — no code change needed in `ExampleGallery.cs` or `Index.razor`.

### 4. Asset files (if the example has assets) — no manual wwwroot copy needed

Non-code asset files (`.png`, `.fnt`, `.ttf`, `.fx`, `.slang`, ...) follow the same flat naming
convention: `Examples/{ExampleName}.{AssetFile}`. That's the only file you add — the
`CopyExampleStaticAssets` MSBuild target (`BeforeBuild`) automatically mirrors it to
`wwwroot/examples/{ExampleName}/{AssetFile}` (split on the *first* dot) so share links can
re-fetch it over HTTP; there is nothing to `mkdir`/`cp` by hand (issue #56). Keep example names
themselves dot-free, since the split assumes exactly one dot boundary between the example name
and the asset filename.

`LoadExampleAssets()` sets `AssetInfo.SourceUrl` to `{baseUri}examples/{ExampleName}/{file}`, which `GetAssetUrlsFragment()` includes in share URLs.

### 5. Update third-party notices (if bundling external assets)

If the example bundles third-party assets (fonts, images, etc.) from external projects, check whether their license requires attribution (e.g. Apache 2.0, CC-BY). If so, add a row to the table in `THIRD-PARTY-NOTICES.md` at the repo root:

```markdown
| `Examples/MyExample.AssetName.ext` | License Name | Copyright Holder | [Source](https://...) |
```

Assets under licenses that don't require attribution (MIT, CC0, Unlicense, public domain) are covered by the file's general intro paragraph and don't need an explicit entry.

### 6. Test

```bash
dotnet build XnaFiddle.BlazorGL/XnaFiddle.BlazorGL.csproj
```

Open the app, select the new example from the gallery dropdown, and click **Compile & Run**.

## Key files

- `XnaFiddle.BlazorGL/Examples/` — example source files
- `XnaFiddle.BlazorGL/ExampleGallery.cs` — loads embedded resources, returns source by name
- `XnaFiddle.BlazorGL/XnaFiddle.BlazorGL.csproj` — must include new file as EmbeddedResource
- `XnaFiddle.BlazorGL/Pages/Index.razor` — gallery dropdown (no change needed)
- `XnaFiddle.BlazorGL/InMemoryContentManager.cs` — use for asset loading in examples
