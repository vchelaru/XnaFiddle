# XnaFiddle

A browser-based C# game editor and runner powered by [KNI](https://github.com/kniEngine/kni). Write XNA-style game code in a Monaco editor, compile it with Roslyn in the browser, and see it run live on a WebGL canvas.

---

## Using XnaFiddle

### URL Parameters

XnaFiddle loads code via URL so links are shareable and embeddable. There are two kinds of parameters:

**Query string** (`?param=value`) — short values and named resources:

| Parameter | Example | Description |
|---|---|---|
| `?example=<name>` | `?example=BouncingBall` | Load a built-in example by name |
| `?gist=<id>` | `?gist=abc123` | Load from a GitHub Gist (ID or full URL) |
| `?embed=true` | `?embed=true&example=AposShapes` | Canvas-only mode — hides the editor (see [Embedding](#embedding)) |
| `?hover=true` | `?hover=true&embed=true&example=GumUI` | Throttle to 2fps when mouse is not over the canvas; full speed on hover. See [Frame rate and CPU](#frame-rate-and-cpu). |
| `?fps=N` | `?fps=60&embed=true&example=BouncingBall` | Override the frame rate cap. See [Frame rate and CPU](#frame-rate-and-cpu). |

**Fragment** (`#key=value`) — large payloads, never sent to the server:

| Parameter | Description |
|---|---|
| `#code=<encoded>` | Full C# source, gzip-compressed and base64-encoded |
| `#snippet=<encoded>` | Compact snippet JSON, gzip-compressed and base64-encoded |

Fragment params can be combined with query params:

```
https://xnafiddle.net/?embed=true#code=H4sIAAAAA...
```

---

### Built-in Examples

Pass any of these names to `?example=`:

| Name | Description |
|---|---|
| `BouncingBall` | A ball that bounces off the edges of the screen |
| `MouseTrail` | Trail of circles that follow the mouse cursor |
| `SoundPlayback` | Load and play a WAV sound effect with keyboard controls |
| `TextureLoading` | Load and display a texture from a file |
| `AetherPhysics` | 2D physics simulation with a bouncing ball and keyboard controls |
| `AposShapes` | Draw shapes with the Apos.Shapes library |
| `FontStashSharp` | Dynamic text rendering with multiple sizes and colors |
| `DynamicFonts` | Runtime font generation with KernSmith — pick family, size, bold, italic, and outline |
| `GumUI` | UI layout with buttons and text using Gum |
| `MlemTextFormatting` | Text formatting using MLEM, which supports coloring, in-text icons, text animations and more |
| `MlemUi` | A mouse, keyboard, gamepad and touch ready Ui system that features automatic anchoring, sizing and several ready-to-use element types |
| `Camera2D (MonoGame.Extended)` | Pan and zoom a 2D camera with keyboard and mouse |

---

### Encoding (`#code=` and `#snippet=`)

Both `#code=` and `#snippet=` store payloads as **gzip-compressed, base64url-encoded** strings. This keeps URLs short enough to share and copy-paste while supporting arbitrarily large code files.

Use `tools/xnafiddle-encode.exe` to generate these URLs from the command line:

```bash
# Encode a full C# file
xnafiddle-encode code --file MyGame.cs
xnafiddle-encode code 'public class FiddleGame : Game { ... }'

# Encode a snippet JSON file or string
xnafiddle-encode snippet --file mygame.snippet.json
xnafiddle-encode snippet '{"members":"int _x;","draw":"..."}'

# Encode multiple items in one call — outputs one URL per item
xnafiddle-encode code 'cs1' code 'cs2' snippet '{"draw":"..."}'
xnafiddle-encode code --file a.cs snippet --file b.json code 'inline cs'
```

The tool prints one complete URL per item (`https://xnafiddle.net/#code=...` or `https://xnafiddle.net/#snippet=...`), one per line. Multiple items can be passed in a single invocation — useful when generating many links at once.

---

### Snippet Format

Snippets are a compact JSON format that expands into a full `Game` subclass. Instead of writing all the boilerplate, you only provide the parts that differ.

**Available fields:**

| Field | Injected into |
|---|---|
| `members` | Class-level field declarations |
| `initialize` | Inside `Initialize()`, after `base.Initialize()` |
| `loadContent` | Inside `LoadContent()` |
| `update` | Inside `Update()`, before `base.Update()` |
| `draw` | Inside `Draw()`, between `Clear()` and any post-draw |
| `IsGum` | `true` to inject `GumService` init/update/draw boilerplate |
| `IsMonoGameExtended` | `true` to inject `SpriteBatch _spriteBatch` and its `LoadContent` init |
| `IsAposShapes` | `true` to inject `ShapeBatch _shapeBatch`, its init, and `Begin()`/`End()` wrappers |

**Example — bouncing square:**

```json
{
  "members": "SpriteBatch _sb;\nTexture2D _pixel;\nVector2 _pos = new Vector2(200, 150);\nVector2 _vel = new Vector2(180f, 130f);",
  "loadContent": "_sb = new SpriteBatch(GraphicsDevice);\n_pixel = new Texture2D(GraphicsDevice, 1, 1);\n_pixel.SetData(new[] { Color.White });",
  "update": "float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;\n_pos += _vel * dt;\nif (_pos.X < 0 || _pos.X > GraphicsDevice.Viewport.Width - 40) _vel.X = -_vel.X;\nif (_pos.Y < 0 || _pos.Y > GraphicsDevice.Viewport.Height - 40) _vel.Y = -_vel.Y;",
  "draw": "_sb.Begin();\n_sb.Draw(_pixel, new Rectangle((int)_pos.X, (int)_pos.Y, 40, 40), Color.Coral);\n_sb.End();"
}
```

**Example — Gum UI click counter:**

```json
{
  "IsGum": true,
  "initialize": "int count = 0;\nvar label = new Label();\nlabel.Text = \"Clicks: 0\";\nlabel.AddToRoot();\nvar btn = new Button();\nbtn.Text = \"Click me!\";\nbtn.Width = 200;\nbtn.Click += (_, _) => label.Text = $\"Clicks: {++count}\";\nbtn.AddToRoot();"
}
```

Draw order for preset flags: `Clear` → preset `preDraw` (e.g. `_shapeBatch.Begin()`) → user `draw` code → preset `postDraw` (e.g. `_shapeBatch.End()`, `GumUI.Draw()`). MonoGame.Extended does **not** auto-wrap `Begin()`/`End()` since camera usage requires a `transformMatrix`.

---

### Embedding

XnaFiddle can be embedded in any website as an iframe. There are two approaches depending on whether you want to gate the load behind a button.

#### Direct embed

Add `?embed=true` to any XnaFiddle URL. The editor is hidden, the canvas fills the full iframe, and the code compiles and runs automatically on load.

```html
<iframe src="https://xnafiddle.net/?embed=true&example=AposShapes"
        width="600" height="400"></iframe>

<iframe src="https://xnafiddle.net/?embed=true&gist=abc123"
        width="600" height="400"></iframe>

<iframe src="https://xnafiddle.net/?embed=true#code=H4sIAAAAA..."
        width="600" height="400"></iframe>
```

In embed mode:
- A loading overlay shows compilation progress (progress bar + status text)
- An **"Edit in XnaFiddle"** button appears in the corner, linking to the full editor

#### Gate page (recommended for docs sites)

`embed-gate.html` is a lightweight wrapper page (no WASM) that shows a **"▶ Run Sample"** button and begins downloading the framework files in the background immediately. By the time the user clicks, the ~4 MB download is already cached.

```html
<iframe src="https://xnafiddle.net/embed-gate.html?example=AposShapes"
        width="600" height="400"></iframe>
```

The gate page accepts the same parameters as the main app (`example`, `gist`, `#code=`, `#snippet=`) and forwards them automatically.

**localStorage memory:** once a user clicks "Run Sample" on any page, the button is skipped on all future visits across any page that uses `embed-gate.html`. The framework files are also already cached at that point, so subsequent embeds load with no download cost.

#### Frame rate and CPU

Every iframe runs its own WASM game loop on the browser's single main thread. With multiple iframes on the same page, their loops compete for that thread — frame rate and compilation speed both suffer. Two parameters help manage this.

**`?fps=N` — frame rate cap**

In embed mode, XnaFiddle automatically caps frame rate to conserve CPU:

| Scenario | Default |
|---|---|
| Embed, desktop | 30 fps |
| Embed, mobile | 20 fps |
| Normal editor | Uncapped |

Override with `?fps=N` — for example `?fps=60` to restore full speed, or `?fps=15` to cap lower. Lower is almost always fine for UI and simple demos; users rarely notice the difference between 30fps and 60fps in a docs embed.

**`?hover=true` — throttle when idle**

Drops to 2fps when the mouse is not over the canvas, and jumps back to full speed (subject to `?fps=`) the moment the mouse enters. On touch devices this parameter is ignored and the game always runs at full speed.

**Recommendations by use case:**

*Single iframe on a page* — the defaults are fine. No need for `?hover=true`; the 30fps cap keeps CPU usage reasonable without any additional interaction required from the user.

```html
<iframe src="https://xnafiddle.net/embed-gate.html?example=AposShapes"
        width="600" height="400"></iframe>
```

*Multiple iframes on a page (e.g. a library showcase)* — add `?hover=true` so only the iframe the user is actively looking at runs at full speed. The others tick at 2fps, which is enough to stay visually alive without meaningfully competing for CPU.

```html
<iframe src="https://xnafiddle.net/embed-gate.html?hover=true&example=GumUI"
        width="600" height="400"></iframe>
```

*UI-heavy demos (Gum, buttons, sliders)* — these only need to respond when the user is interacting, making `?hover=true` especially well suited. Consider also lowering `?fps=` since UI typically doesn't need high frame rates even when active.

```html
<iframe src="https://xnafiddle.net/embed-gate.html?hover=true&fps=20&example=GumUI"
        width="600" height="400"></iframe>
```

*Animation or physics demos* — keep `?hover=true` off if the demo should play continuously as the user reads past it. The 30fps default is enough for smooth motion in a small embed.

#### Download and caching notes

- Framework files (~4 MB after brotli compression) are cached by the browser after the first load and shared across all XnaFiddle iframes on any page
- Each iframe runs its own WASM instance — there is no shared memory between iframes on the same page
- Compilation runs fresh per iframe load; the gate page background prefetch ensures the download is not the bottleneck

---

## Export

Click the **Export** button in the toolbar to download your fiddle as a complete, buildable project (`.zip`). Choose a runtime and platform:

| Runtime | Platforms |
|---|---|
| KNI | DesktopGL, WindowsDX, Android, BlazorGL (Browser) |
| MonoGame | DesktopGL, WindowsDX, Android |

The exported project includes the correct NuGet packages, entry point (`Program.cs` / `Activity1.cs` / `Index.razor`), and any assets you've loaded. Third-party libraries (Gum, Apos.Shapes, MonoGame.Extended, FontStashSharp, Aether.Physics2D, KernSmith) are detected automatically from your code and included in the `.csproj`.

Library versions are centralized in the `<PropertyGroup>` at the top of `XnaFiddle.BlazorGL.csproj` — update them there and both XnaFiddle and exported projects stay in sync via the auto-generated `PackageVersions` class.

---

## Sandbox Restrictions

User code runs in a sandboxed environment. Certain namespaces, types, and methods are blocked at compile time to prevent misuse. The full list is maintained in [`SecurityChecker.cs`](XnaFiddle.BlazorGL/SecurityChecker.cs).

Notable restrictions:

- **Expression trees** (`System.Linq.Expressions`) — blocked entirely. APIs like Gum's `Binding` class that offer both expression-based and string-based overloads will work with the string-based overload only (e.g. `new Binding("PropertyName")` instead of `new Binding<T>(vm => vm.PropertyName)`).
- **Reflection and dynamic** — `System.Reflection`, `System.Reflection.Emit`, and the `dynamic` keyword are all blocked.
- **Networking** — `System.Net` is blocked; user code cannot make HTTP requests or open sockets.
- **File system** — `System.IO.File`, `System.IO.Directory`, and `System.IO.FileStream` are blocked. `MemoryStream` and `BinaryReader` are available.
- **Threading** — `Thread`, `Task.Run`, `Task.Start`, and thread pool methods are blocked. The WASM runtime is single-threaded.
- **JS interop** — `Microsoft.JSInterop` and the `nkast.Wasm.*` platform namespaces are blocked.

---

## Development

### Setup

```bash
git clone --recursive https://github.com/your-org/XnaFiddle.git
cd XnaFiddle
dotnet run --project XnaFiddle.BlazorGL/XnaFiddle.BlazorGL.csproj
```

If you already cloned without `--recursive`:
```bash
git submodule update --init --recursive
```

Opens at **https://localhost:60440** (HTTP: 60441).

### Adding Examples

Add a `.cs` file to `XnaFiddle.BlazorGL/Examples/`. It will be picked up automatically by `ExampleGallery.cs` and appear in the dropdown. The file must not define a namespace — the class goes directly at the top level.

### Upgrading Apos.Shapes

The `Apos.Shapes.KNI` NuGet package includes a pre-built content file (`apos-shapes.xnb`) that must be shipped with the app. When upgrading:

1. Update the version in `XnaFiddle.BlazorGL/XnaFiddle.BlazorGL.csproj`
2. Build to restore the new package
3. Copy the XNB from the NuGet cache:
   ```bash
   cp ~/.nuget/packages/apos.shapes/<VERSION>/buildTransitive/Content/bin/DesktopGL/Content/apos-shapes.xnb XnaFiddle.BlazorGL/wwwroot/apos-shapes.xnb
   ```
4. Verify the AposShapes example still runs

### Deployment

Deployed automatically to GitHub Pages on push to `main` via `.github/workflows/deploy.yml`. Uses `dotnet publish` which generates brotli-compressed framework files.

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to add new third-party libraries, examples, and other changes.

---

## License

MIT
