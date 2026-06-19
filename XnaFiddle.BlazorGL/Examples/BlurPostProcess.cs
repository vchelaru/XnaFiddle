using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;
    Texture2D logo;
    Effect blur;

    RenderTarget2D sceneTarget;      // the whole scene is rendered here first
    RenderTarget2D horizontalTarget; // result of the horizontal blur pass

    static readonly Color Background = new Color(30, 30, 46);

    // Blur reach in SCREEN pixels. Because the blur runs on a screen-sized render
    // target (not a single sprite), this is independent of any texture's size --
    // the same value looks the same no matter what is on screen. That is the key
    // difference from the per-sprite Blur example.
    //
    // Controls: Space toggles the blur on/off; Up/Down change the strength.
    float blurRadius = 6f;
    bool blurEnabled = true;
    KeyboardState previousKeyboard;

    public Game1()
    {
        graphics = new GraphicsDeviceManager(this);
        if (GraphicsAdapter.DefaultAdapter.IsProfileSupported(GraphicsProfile.HiDef))
            graphics.GraphicsProfile = GraphicsProfile.HiDef;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        spriteBatch = new SpriteBatch(GraphicsDevice);
        logo = Content.Load<Texture2D>("KniIcon");

        // The very same Blur.fx the per-sprite example uses -- only the harness
        // around it changes. Compiled in-browser when you press Run.
        blur = Content.Load<Effect>("Blur");
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keyboard = Keyboard.GetState();

        if (keyboard.IsKeyDown(Keys.Space) && previousKeyboard.IsKeyUp(Keys.Space))
            blurEnabled = !blurEnabled;

        if (keyboard.IsKeyDown(Keys.Up))
            blurRadius = MathHelper.Clamp(blurRadius + 0.25f, 0f, 30f);
        if (keyboard.IsKeyDown(Keys.Down))
            blurRadius = MathHelper.Clamp(blurRadius - 0.25f, 0f, 30f);

        previousKeyboard = keyboard;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        EnsureRenderTargets();

        // Render the whole scene into a screen-sized render target first.
        GraphicsDevice.SetRenderTarget(sceneTarget);
        DrawScene();

        if (blurEnabled && blurRadius > 0f)
        {
            int w = sceneTarget.Width;
            int h = sceneTarget.Height;

            // Horizontal pass: sceneTarget -> horizontalTarget.
            blur.Parameters["Offset"]?.SetValue(new Vector2(blurRadius / w, 0f));
            GraphicsDevice.SetRenderTarget(horizontalTarget);
            GraphicsDevice.Clear(Background);
            spriteBatch.Begin(effect: blur);
            spriteBatch.Draw(sceneTarget, Vector2.Zero, Color.White);
            spriteBatch.End();

            // Vertical pass: horizontalTarget -> the screen.
            blur.Parameters["Offset"]?.SetValue(new Vector2(0f, blurRadius / h));
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Background);
            spriteBatch.Begin(effect: blur);
            spriteBatch.Draw(horizontalTarget, Vector2.Zero, Color.White);
            spriteBatch.End();
        }
        else
        {
            // Blur off: present the scene unchanged for comparison.
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Background);
            spriteBatch.Begin();
            spriteBatch.Draw(sceneTarget, Vector2.Zero, Color.White);
            spriteBatch.End();
        }

        base.Draw(gameTime);
    }

    // Draws the scene into whichever render target is currently set. A plain
    // SpriteBatch pass here also primes SpriteBatch's vertex shader, which the
    // pixel-only blur effect relies on being active.
    void DrawScene()
    {
        GraphicsDevice.Clear(Background);

        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;
        Vector2 origin = new Vector2(logo.Width / 2f, logo.Height / 2f);

        // Several overlapping logos so the blur visibly crosses sprite boundaries
        // -- the whole point of a post-process versus a per-sprite blur.
        spriteBatch.Begin();
        DrawLogo(w * 0.40f, h * 0.45f, 0.90f, Color.White, origin);
        DrawLogo(w * 0.55f, h * 0.55f, 0.70f, new Color(255, 180, 180), origin);
        DrawLogo(w * 0.50f, h * 0.40f, 0.50f, new Color(180, 220, 255), origin);
        DrawLogo(w * 0.62f, h * 0.42f, 0.40f, new Color(200, 255, 200), origin);
        DrawLogo(w * 0.38f, h * 0.58f, 0.45f, new Color(255, 240, 170), origin);
        spriteBatch.End();
    }

    void DrawLogo(float x, float y, float relativeScale, Color color, Vector2 origin)
    {
        float baseScale = GraphicsDevice.Viewport.Height * 0.35f / logo.Height;
        spriteBatch.Draw(logo, new Vector2(x, y), null, color, 0f, origin,
            baseScale * relativeScale, SpriteEffects.None, 0f);
    }

    // Recreate the render targets whenever the back buffer size changes (e.g. the
    // user resizes the window), so they always match the screen.
    void EnsureRenderTargets()
    {
        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;
        if (sceneTarget == null || sceneTarget.Width != w || sceneTarget.Height != h)
        {
            sceneTarget?.Dispose();
            horizontalTarget?.Dispose();
            sceneTarget = new RenderTarget2D(GraphicsDevice, w, h);
            horizontalTarget = new RenderTarget2D(GraphicsDevice, w, h);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            sceneTarget?.Dispose();
            horizontalTarget?.Dispose();
            blur?.Dispose();
        }

        base.Dispose(disposing);
    }
}
