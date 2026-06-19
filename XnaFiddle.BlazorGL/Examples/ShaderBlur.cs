using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;
    Texture2D logo;
    Effect blur;
    RenderTarget2D sceneTarget;      // the logo captured here, ready to blur
    RenderTarget2D horizontalTarget; // result of the horizontal blur pass

    // Reused as the render-target clear color so the blurred result composites
    // seamlessly over the matching screen background.
    static readonly Color Background = new Color(30, 30, 46);

    // How far the blur reaches, in source pixels (the "strength"). Pairs with
    // the SampleCount quality knob inside Blur.fx: SampleCount taps are spread
    // across this radius, so a bigger radius wants a higher SampleCount to stay
    // smooth. Strength can be a plain runtime value like this; the tap count
    // must live in the shader because the GPU unrolls the sample loop at
    // compile time.
    const float BlurRadius = 8.0f;

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

        // Blur.fx is compiled to a runnable Effect in-browser when you press Run.
        blur = Content.Load<Effect>("Blur");

        // A separable blur needs somewhere to hold the in-between result. Both
        // targets match the source size, so one texel equals one logo pixel.
        sceneTarget = new RenderTarget2D(GraphicsDevice, logo.Width, logo.Height);
        horizontalTarget = new RenderTarget2D(GraphicsDevice, logo.Width, logo.Height);
    }

    protected override void Draw(GameTime gameTime)
    {
        // Pass 1: capture the logo into a render target. Drawing with a plain
        // SpriteBatch here also primes SpriteBatch's vertex shader, which the
        // pixel-only blur effect relies on being active.
        GraphicsDevice.SetRenderTarget(sceneTarget);
        GraphicsDevice.Clear(Background);
        spriteBatch.Begin();
        spriteBatch.Draw(logo, Vector2.Zero, Color.White);
        spriteBatch.End();

        // Pass 2: horizontal blur, sceneTarget -> horizontalTarget.
        blur.Parameters["Offset"]?.SetValue(new Vector2(BlurRadius / logo.Width, 0f));
        GraphicsDevice.SetRenderTarget(horizontalTarget);
        GraphicsDevice.Clear(Background);
        spriteBatch.Begin(effect: blur);
        spriteBatch.Draw(sceneTarget, Vector2.Zero, Color.White);
        spriteBatch.End();

        // Back to the screen for the final pass.
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Background);

        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;
        Vector2 origin = new Vector2(logo.Width / 2f, logo.Height / 2f);
        float scale = Math.Min(w * 0.35f / logo.Width, h * 0.6f / logo.Height);
        Vector2 leftPos = new Vector2(w * 0.28f, h / 2f);
        Vector2 rightPos = new Vector2(w * 0.72f, h / 2f);

        // Original on the left.
        spriteBatch.Begin();
        spriteBatch.Draw(logo, leftPos, null, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
        spriteBatch.End();

        // Blurred on the right: the vertical pass runs as the horizontally-blurred
        // target is drawn to the screen, completing the separable Gaussian blur.
        blur.Parameters["Offset"]?.SetValue(new Vector2(0f, BlurRadius / logo.Height));
        spriteBatch.Begin(effect: blur);
        spriteBatch.Draw(horizontalTarget, rightPos, null, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
        spriteBatch.End();

        base.Draw(gameTime);
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
