// MonoGame.Extended Camera2D demo
// Pan  : WASD / Arrow keys, or click-and-drag
// Zoom : Mouse scroll wheel, or +/- keys (top row or numpad)
// Reset: R key

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;

using MonoGameGum;
using MonoGameGum.GueDeriving;
using Gum.Wireframe;
using Gum.Forms;
using Gum.Mvvm;
using Gum.Forms.Controls;

public class MyGame : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;
    OrthographicCamera camera;

    // 1x1 white texture — the classic XNA "draw anything" trick
    Texture2D pixel;

    struct WorldRect { public int X, Y, W, H; public Color Color; }
    WorldRect[] _rects;

    int _prevScroll;
    MouseState _prevMouse;

    public MyGame()
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
        spriteBatch = new SpriteBatch(GraphicsDevice);

        pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });

        // OrthographicCamera is the core MonoGame.Extended 2D camera.
        // It wraps the view matrix, so any SpriteBatch draw call that
        // uses camera.GetViewMatrix() will be in "world space".
        camera = new OrthographicCamera(GraphicsDevice);

        // Scatter 70 coloured rectangles around the world
        var rng = new Random(7);
        _rects = new WorldRect[70];
        for (int i = 0; i < _rects.Length; i++)
        {
            _rects[i] = new WorldRect
            {
                X = rng.Next(-700, 700),
                Y = rng.Next(-500, 500),
                W = rng.Next(24, 90),
                H = rng.Next(18, 70),
                Color = new Color(rng.Next(80, 256), rng.Next(80, 256), rng.Next(80, 256))
            };
        }
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        KeyboardState keys = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();

        // ── Pan with keyboard ─────────────────────────────────────────
        // Divide speed by Zoom so pan feels consistent at all zoom levels.
        float speed = 320f / camera.Zoom * dt;
        Vector2 move = Vector2.Zero;
        if (keys.IsKeyDown(Keys.W) || keys.IsKeyDown(Keys.Up))    move.Y -= 1;
        if (keys.IsKeyDown(Keys.S) || keys.IsKeyDown(Keys.Down))   move.Y += 1;
        if (keys.IsKeyDown(Keys.A) || keys.IsKeyDown(Keys.Left))   move.X -= 1;
        if (keys.IsKeyDown(Keys.D) || keys.IsKeyDown(Keys.Right))  move.X += 1;
        if (move != Vector2.Zero) camera.Move(Vector2.Normalize(move) * speed);

        // ── Pan with left-mouse drag ──────────────────────────────────
        if (mouse.LeftButton == ButtonState.Pressed &&
            _prevMouse.LeftButton == ButtonState.Pressed)
        {
            Vector2 delta = new Vector2(mouse.X - _prevMouse.X, mouse.Y - _prevMouse.Y);
            camera.Move(-delta / camera.Zoom);
        }

        // ── Zoom with scroll wheel ────────────────────────────────────
        int scrollDelta = mouse.ScrollWheelValue - _prevScroll;
        if (scrollDelta > 0) camera.ZoomIn(scrollDelta * 0.001f);
        else if (scrollDelta < 0) camera.ZoomOut(-scrollDelta * 0.001f);
        _prevScroll = mouse.ScrollWheelValue;

        // ── Zoom with +/- keys (top row: OemPlus/OemMinus, numpad: Add/Subtract) ──
        float keyZoom = 1.5f * dt;
        if (keys.IsKeyDown(Keys.OemPlus)  || keys.IsKeyDown(Keys.Add))      camera.ZoomIn(keyZoom);
        if (keys.IsKeyDown(Keys.OemMinus) || keys.IsKeyDown(Keys.Subtract)) camera.ZoomOut(keyZoom);

        // ── Reset ─────────────────────────────────────────────────────
        if (keys.IsKeyDown(Keys.R))
        {
            camera.LookAt(Vector2.Zero);
            camera.Zoom = 1f;
        }

        _prevMouse = mouse;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);

        // Pass the camera's view matrix to SpriteBatch — everything drawn
        // between Begin/End will be in world space, automatically transformed
        // by the camera's position, zoom, and rotation.
        spriteBatch.Begin(transformMatrix: camera.GetViewMatrix());

        // Background grid (helps make pan/zoom obvious)
        DrawGrid();

        // World-space coloured rectangles
        foreach (WorldRect r in _rects)
            spriteBatch.Draw(pixel, new Rectangle(r.X, r.Y, r.W, r.H), r.Color * 0.85f);

        // Origin marker so players can find (0,0)
        spriteBatch.Draw(pixel, new Rectangle(-10, -2, 20, 4), Color.White);
        spriteBatch.Draw(pixel, new Rectangle(-2, -10, 4, 20), Color.White);

        spriteBatch.End();
        base.Draw(gameTime);
    }

    void DrawGrid()
    {
        int step = 100;
        int extent = 1000;
        for (int i = -extent; i <= extent; i += step)
        {
            bool axis = (i == 0);
            Color c = axis ? new Color(80, 80, 120) : new Color(35, 35, 55);
            // Horizontal line
            spriteBatch.Draw(pixel, new Rectangle(-extent, i, extent * 2, 1), c);
            // Vertical line
            spriteBatch.Draw(pixel, new Rectangle(i, -extent, 1, extent * 2), c);
        }
    }
}
