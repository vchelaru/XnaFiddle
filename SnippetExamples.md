# Snippet URL Examples

Test these by appending the suffix to `http://localhost:60441/` (or the HTTPS port).

---

## 1. Raw MonoGame — Bouncing Square

No preset flags. Provides all draw machinery manually.

### Snippet JSON

```json
{
  "members": "SpriteBatch _sb;\nTexture2D _pixel;\nVector2 _pos = new Vector2(200, 150);\nVector2 _vel = new Vector2(180f, 130f);",
  "loadContent": "_sb = new SpriteBatch(GraphicsDevice);\n_pixel = new Texture2D(GraphicsDevice, 1, 1);\n_pixel.SetData(new[] { Color.White });",
  "update": "float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;\n_pos += _vel * dt;\nif (_pos.X < 0 || _pos.X > GraphicsDevice.Viewport.Width - 40) _vel.X = -_vel.X;\nif (_pos.Y < 0 || _pos.Y > GraphicsDevice.Viewport.Height - 40) _vel.Y = -_vel.Y;",
  "draw": "_sb.Begin();\n_sb.Draw(_pixel, new Rectangle((int)_pos.X, (int)_pos.Y, 40, 40), Color.Coral);\n_sb.End();"
}
```

### Expanded Code (what appears in the editor)

```csharp
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

public class FiddleGame : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch _sb;
    Texture2D _pixel;
    Vector2 _pos = new Vector2(200, 150);
    Vector2 _vel = new Vector2(180f, 130f);

    public FiddleGame()
    {
        graphics = new GraphicsDeviceManager(this);
        graphics.GraphicsProfile = GraphicsProfile.HiDef;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _sb = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _pos += _vel * dt;
        if (_pos.X < 0 || _pos.X > GraphicsDevice.Viewport.Width - 40) _vel.X = -_vel.X;
        if (_pos.Y < 0 || _pos.Y > GraphicsDevice.Viewport.Height - 40) _vel.Y = -_vel.Y;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        _sb.Begin();
        _sb.Draw(_pixel, new Rectangle((int)_pos.X, (int)_pos.Y, 40, 40), Color.Coral);
        _sb.End();
        base.Draw(gameTime);
    }
}
```

### URL Suffix

```
#snippet=H4sIAAAAAAAEAH1R226CQBT8lROelhYJ2jZpau2Dl9jnampJ25gVDrAJ7JJlvSTqv_dwUdGHJpDsGYaZObN7K8NshbqwXqxZroXBITdBAsti1f-Rc9yZtcbeGJa52GFK0CcGRukeAaqAAUjcQgOxnuc50H3y7DZtg-kNrfvsRcR78CK7bzlWqng4UtKgNJSBfBt6Kw2bap4nIijGuBEBlvp1noZ6jnlDJBd6LnR3hmbMDWf00_cv7GGkUqXdRUJGcKzSrPOQG6QgEeUyEBqyYNXZjnmGc5GhO0l5XmA4Pc1zZXg6w0DJsKi8qJn7Qb36HUkQJiJgJe5-wSt4cDhAM73BdWT3U-A2V9q4CxGaBDrw6NmVFJEH0KlPbUX_StH_R_EdRZyYtqR_lvTL5UPNt_UduEOMhWRVdTSN6QOrO3Sqxj_oLrmMU2RMSGPXyzhwGXyHXMrXdpqWR0rz9CQ4kSGJW8c_9ddUF38CAAA
```

---

## 2. Gum — Click Counter

`IsGum: true` injects `GumService` init/update/draw. Only the UI setup is needed.

### Snippet JSON

```json
{
  "IsGum": true,
  "initialize": "int count = 0;\nvar label = new Label();\nlabel.Text = \"Clicks: 0\";\nlabel.AddToRoot();\nvar btn = new Button();\nbtn.Text = \"Click me!\";\nbtn.Width = 200;\nbtn.Click += (_, _) => label.Text = $\"Clicks: {++count}\";\nbtn.AddToRoot();"
}
```

### Expanded Code (what appears in the editor)

```csharp
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGameGum;
using Gum.Forms;
using Gum.Forms.Controls;

public class FiddleGame : Game
{
    GraphicsDeviceManager graphics;
    GumService GumUI => GumService.Default;

    public FiddleGame()
    {
        graphics = new GraphicsDeviceManager(this);
        graphics.GraphicsProfile = GraphicsProfile.HiDef;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        base.Initialize();
        GumUI.Initialize(this, DefaultVisualsVersion.V3);
        int count = 0;
        var label = new Label();
        label.Text = "Clicks: 0";
        label.AddToRoot();
        var btn = new Button();
        btn.Text = "Click me!";
        btn.Width = 200;
        btn.Click += (_, _) => label.Text = $"Clicks: {++count}";
        btn.AddToRoot();
    }

    protected override void Update(GameTime gameTime)
    {
        GumUI.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(0.15f, 0.15f, 0.2f));
        GumUI.Draw();
        base.Draw(gameTime);
    }
}
```

### URL Suffix

```
#snippet=H4sIAAAAAAAEAF2PywrCMBBFf2UMLlpaJLiMVFAXIriSgptC6SNgME2gnahY-u8msdTHJmTumRxuenLo9qYhDFvDYyKUQFFI8eSE2QGh0saeCdBVpm5FC7IoubSz4nc4unsQWuLTRcofbjUjOymqa8eAZmSCm7pO9Ulr9A-cqkQ1irYGUSsPbPjngYbPvMehs6jxYtmS0jF570QJBHkMeQjJGn7azD91-ijy3xkm3XcpMrwAwpLJeQwBAAA
```

---

## 3. MonoGame.Extended — Camera Pan

`IsMonoGameExtended: true` injects `SpriteBatch _spriteBatch` and its LoadContent init.
`OrthographicCamera` is added as a user member; `Begin(transformMatrix:)` is called manually in draw.

### Snippet JSON

```json
{
  "IsMonoGameExtended": true,
  "members": "Texture2D _pixel;\nOrthographicCamera _camera;",
  "loadContent": "_pixel = new Texture2D(GraphicsDevice, 1, 1);\n_pixel.SetData(new[] { Color.White });\n_camera = new OrthographicCamera(GraphicsDevice);",
  "update": "float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;\nvar keys = Keyboard.GetState();\nfloat speed = 200f * dt;\nif (keys.IsKeyDown(Keys.Left))  _camera.Move(new Vector2(-speed, 0));\nif (keys.IsKeyDown(Keys.Right)) _camera.Move(new Vector2( speed, 0));\nif (keys.IsKeyDown(Keys.Up))    _camera.Move(new Vector2(0, -speed));\nif (keys.IsKeyDown(Keys.Down))  _camera.Move(new Vector2(0,  speed));",
  "draw": "_spriteBatch.Begin(transformMatrix: _camera.GetViewMatrix());\nfor (int i = 0; i < 5; i++)\n    _spriteBatch.Draw(_pixel, new Rectangle(i * 80 - 200, -20, 60, 40), new Color(i * 50, 100, 200 - i * 30));\n_spriteBatch.End();"
}
```

### Expanded Code (what appears in the editor)

```csharp
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;

public class FiddleGame : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch _spriteBatch;
    Texture2D _pixel;
    OrthographicCamera _camera;

    public FiddleGame()
    {
        graphics = new GraphicsDeviceManager(this);
        graphics.GraphicsProfile = GraphicsProfile.HiDef;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _camera = new OrthographicCamera(GraphicsDevice);
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var keys = Keyboard.GetState();
        float speed = 200f * dt;
        if (keys.IsKeyDown(Keys.Left))  _camera.Move(new Vector2(-speed, 0));
        if (keys.IsKeyDown(Keys.Right)) _camera.Move(new Vector2( speed, 0));
        if (keys.IsKeyDown(Keys.Up))    _camera.Move(new Vector2(0, -speed));
        if (keys.IsKeyDown(Keys.Down))  _camera.Move(new Vector2(0,  speed));
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(18, 18, 28));
        _spriteBatch.Begin(transformMatrix: _camera.GetViewMatrix());
        for (int i = 0; i < 5; i++)
            _spriteBatch.Draw(_pixel, new Rectangle(i * 80 - 200, -20, 60, 40), new Color(i * 50, 100, 200 - i * 30));
        _spriteBatch.End();
        base.Draw(gameTime);
    }
}
```

### URL Suffix

```
#snippet=H4sIAAAAAAAEAI1S22rbQBD9lUFPq0YRituUYrcvsYwJqSnEbvrQlLDRjqSl8q7YHV9CyL93VhuHtKWmIGkvOufMmctjcukX1ti5XONsT2gUqmRMboNZssb1PTqfjJMV7mnjcFTCXa_32E1uzRdHrW2c7FtdTZnsJNxVwzpJsqSzUk2tYT1ieiTBJzC4gxctMY9sX-JWV5jBGT8pS0d4vkQqJUnBpO8_4BGmtrMu_9ZqQngacDHes-7fhv4IkAZjm15JQvZUs0UCRcwWwz5tmLTSa8xnnew9qvnhvLIkuyVW1ijPYbfSwU988My8wod7K53K50hLYmERfEVp3yMqxoyKooY3HIn_6BpEoOaXnqml3RlxFY6fsaY0hUMF84XdYsgbbrAi60bidFDLoEjTIzLXummDzj9l4L9kvvbByxE3RQbR0FGZsDuaFMvAQYZbo5zchWHxveMWX0iq2vwCG20EOWl8bd16Icnp_fhFkst-o3EXr8XghmEgtCHQXPtiwstHOOfl5CS9NUNWr_VLjinivGXDFF2zN2maDoXmpn0o4DQ0kPMd8ec9v--KNCKHcRxQ53x9FkCMZHy4ehsr_FusmVE8HsnTLzGmlkFzAwAA
```

---

## 4. Apos.Shapes — Orbiting Circles

`IsAposShapes: true` injects `ShapeBatch _shapeBatch`, its LoadContent init, and wraps draw in `Begin()`/`End()`.

### Snippet JSON

```json
{
  "IsAposShapes": true,
  "members": "float _time;",
  "update": "_time += (float)gameTime.ElapsedGameTime.TotalSeconds;",
  "draw": "int cx = GraphicsDevice.Viewport.Width / 2;\nint cy = GraphicsDevice.Viewport.Height / 2;\nfor (int i = 0; i < 5; i++)\n{\n    float a = _time + i * MathF.PI * 2f / 5f;\n    float x = cx + MathF.Cos(a) * 100f;\n    float y = cy + MathF.Sin(a) * 100f;\n    _shapeBatch.DrawCircle(new Vector2(x, y), 25f, Color.Transparent, new Color(i * 50, 200, 255 - i * 40), 3f);\n}"
}
```

### Expanded Code (what appears in the editor)

```csharp
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Apos.Shapes;

public class FiddleGame : Game
{
    GraphicsDeviceManager graphics;
    ShapeBatch _shapeBatch;
    float _time;

    public FiddleGame()
    {
        graphics = new GraphicsDeviceManager(this);
        graphics.GraphicsProfile = GraphicsProfile.HiDef;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _shapeBatch = new ShapeBatch(GraphicsDevice, Content);
    }

    protected override void Update(GameTime gameTime)
    {
        _time += (float)gameTime.ElapsedGameTime.TotalSeconds;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(0.1f, 0.1f, 0.15f));
        _shapeBatch.Begin();
        int cx = GraphicsDevice.Viewport.Width / 2;
        int cy = GraphicsDevice.Viewport.Height / 2;
        for (int i = 0; i < 5; i++)
        {
            float a = _time + i * MathF.PI * 2f / 5f;
            float x = cx + MathF.Cos(a) * 100f;
            float y = cy + MathF.Sin(a) * 100f;
            _shapeBatch.DrawCircle(new Vector2(x, y), 25f, Color.Transparent, new Color(i * 50, 200, 255 - i * 40), 3f);
        }
        _shapeBatch.End();
        base.Draw(gameTime);
    }
}
```

### URL Suffix

```
#snippet=H4sIAAAAAAAEAH2QwU7DMAyGX8XqqaWllEIvlB1gg7EDElKncZk0hdRdI7VJlGRs07R3xymbBEIihzi2vt_57UMwsw9a2aplGm1w58wGk6DH_gMNpUHTKeZg5USPZZAEG10zh1QfKhCPIByIaM16nFMpfeqYtlhPz_lcOdZVyJWsre9QG7YlvZAO-A5GMDVMt4LbCX4KjulC4FYr49J3UbsWriAvl3KA9__ALyjWrTvRjTIQeokgRVZSuIeCQhxHS3lYSqDzPRYj4DQIURfwylz7nL7N6Jk31K1oyp-4t0ue4xM3VjZkEbHXWfYb9FbJ7xmshPwDrqxf-CNzvE0ntJKxMLzDUOIWFsidMnm4S2AfJZAXTQJj1SmTzg2TVjOD0iXg0aEceutFRmTmr6KAy2Ga24zUN01EPx6D4xejDwui6QEAAA
```

---

## Notes

- All snippets use `#snippet=` (not `#code=`) in the URL hash
- The expanded code is placed in the Monaco editor so you can see and edit what runs
- Preset contributions in draw order: `Clear` → preset `preDraw` (e.g. `shapeBatch.Begin()`) → user draw code → preset `postDraw` (e.g. `shapeBatch.End()`, then `GumUI.Draw()`)
- MonoGame.Extended does **not** auto-wrap `Begin()`/`End()` since camera usage requires `transformMatrix`
