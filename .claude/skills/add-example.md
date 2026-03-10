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

### 2. Mark it as an embedded resource

In `XnaFiddle.BlazorGL.csproj`, examples are included as `EmbeddedResource` with `ExcludeFromCompile`:

```xml
<EmbeddedResource Include="Examples\MyExample.cs">
  <ExcludeFromCompile>true</ExcludeFromCompile>
</EmbeddedResource>
```

Check the existing pattern in the `.csproj` and follow it exactly.

### 3. Verify the gallery picks it up

`ExampleGallery.cs` reads embedded resources matching `*.Examples.*.cs` and exposes them by filename stem. The new example will automatically appear in the dropdown on the page — no code change needed in `ExampleGallery.cs` or `Index.razor`.

### 4. Test

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
