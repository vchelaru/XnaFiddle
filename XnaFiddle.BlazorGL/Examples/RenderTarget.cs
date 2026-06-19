using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// RENDER TARGET — the render-to-texture round trip.
//
// Normally everything you draw goes straight to the "back buffer": the off-screen
// image the GPU shows on the canvas each frame. A RenderTarget2D is a *texture you
// can draw into* instead. You point the GraphicsDevice at it, render your scene as
// usual, then switch back to the screen — and now that whole scene exists as a
// single Texture2D you can sample, transform, and reuse like any other texture.
//
// Why bother?
//   - Post-processing: capture the scene, then run a blur/bloom/color shader over
//     the entire image at once (see the BlurPostProcess example).
//   - Minimaps / picture-in-picture: render a second view, then draw it small in a
//     corner.
//   - Reflections / portals: render from another viewpoint into a texture, then map
//     it onto a surface.
//   - Capturing the scene as a reusable texture: once it's a texture you can scale,
//     rotate, and warp the *whole frame* as one object — which is impossible if you
//     drew everything directly to the screen.
//
// This is the foundational example: no custom shaders, no mipmaps — just the round
// trip. We render a RIGID, non-moving scene (a static color test card) into one
// render target, switch back to the screen, then draw the captured scene back as a
// SINGLE sprite that we rotate and scale. The whole composition turns and grows as
// one image — and the only reason a fixed scene can move like that is that it is now
// a texture. That is the entire point.
public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;
    // A 1x1 white texture: the standard way to draw solid-color rectangles with
    // SpriteBatch. Tint it by any color and stretch it to any Rectangle.
    Texture2D pixel;

    // The one render target this example needs: the scene is rendered here first,
    // then read back as a texture. It is sized to match the screen (see
    // EnsureRenderTargets) so the capture is a 1:1 snapshot of the back buffer.
    RenderTarget2D sceneTarget;

    static readonly Color SceneBackground = new Color(30, 30, 46);
    // Deliberately a different hue from SceneBackground so the edges of the captured
    // sprite stand out against the screen behind it — making it obvious the scene is
    // now a separate, movable image rather than painted directly on the back buffer.
    static readonly Color ScreenBackground = new Color(64, 40, 72);

    public Game1()
    {
        graphics = new GraphicsDeviceManager(this);
        // HiDef matches the other examples and guarantees render-target support; the
        // guard falls back gracefully on adapters that only expose Reach.
        if (GraphicsAdapter.DefaultAdapter.IsProfileSupported(GraphicsProfile.HiDef))
            graphics.GraphicsProfile = GraphicsProfile.HiDef;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        spriteBatch = new SpriteBatch(GraphicsDevice);
        // In-memory GPU resource creation, NOT file I/O — so this example loads no
        // content at all. One white pixel is enough to draw every rectangle below.
        pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        EnsureRenderTargets();

        // PASS 1 — draw the scene INTO the render target instead of the screen.
        // After this SetRenderTarget call, every draw lands in sceneTarget's texture.
        // The scene itself never animates; we re-render the same rigid image each
        // frame, which is the round trip running continuously.
        GraphicsDevice.SetRenderTarget(sceneTarget);
        DrawScene();

        // PASS 2 — switch back to the screen. Passing null means "render to the back
        // buffer again" (the actual canvas). sceneTarget now holds a finished texture
        // of the scene we just drew, ready to be sampled like any other Texture2D.
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(ScreenBackground);

        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;
        float time = (float)gameTime.TotalGameTime.TotalSeconds;

        // Draw the captured scene back to the screen as ONE transformed sprite — the
        // only thing that moves in this example. origin = the texture's center, so the
        // rotation and scale happen about the middle rather than the top-left corner.
        Vector2 origin = new Vector2(sceneTarget.Width / 2f, sceneTarget.Height / 2f);
        Vector2 center = new Vector2(w / 2f, h / 2f);

        // A slow, continuous spin. We size the sprite by its DIAGONAL so that at any
        // rotation angle it still fits on screen: the diagonal is the largest extent a
        // rotated rectangle ever reaches, so a sprite whose diagonal equals the smaller
        // screen dimension stays on-screen at every angle and aspect ratio.
        float rotation = time * 0.5f;
        float diagonal = (float)Math.Sqrt(
            sceneTarget.Width * (float)sceneTarget.Width +
            sceneTarget.Height * (float)sceneTarget.Height);
        float fitScale = Math.Min(w, h) / diagonal;

        // A pronounced scale oscillation so it clearly reads as the whole captured
        // image growing and shrinking — between 0.5x and 0.9x of the fit-scale, not a
        // subtle pulse. Combined with the spin, the rigid scene visibly moves as one
        // unit, which is the proof it is now a single reusable texture.
        float t = 0.5f * (1f + (float)Math.Sin(time * 1.2f));
        float scale = fitScale * MathHelper.Lerp(0.5f, 0.9f, t);

        spriteBatch.Begin();
        spriteBatch.Draw(sceneTarget, center, null, Color.White, rotation, origin,
            scale, SpriteEffects.None, 0f);
        spriteBatch.End();

        base.Draw(gameTime);
    }

    // Draws a RIGID, static scene into whichever render target is currently bound.
    // Nothing here depends on gameTime: a static SMPTE-style color test card — seven
    // full-width vertical color bars across the top, and a contrasting castellation of
    // dark blocks along the bottom. That top/bottom contrast gives the card an obvious
    // "up" orientation, so when PASS 2 spins the captured texture the rotation reads
    // unambiguously. Because the composition never changes, any motion you see on
    // screen comes entirely from transforming the captured texture in PASS 2 — which
    // is exactly what we want to demonstrate.
    void DrawScene()
    {
        GraphicsDevice.Clear(SceneBackground);

        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;

        spriteBatch.Begin();

        // TOP REGION — seven full-width vertical bars in classic SMPTE order. Each bar
        // is tiled with integer math (x0..x1) rather than a float width, so adjacent
        // bars share an exact pixel boundary and never leave a seam or overlap, even
        // when w doesn't divide evenly by 7.
        int topH = (int)(h * 0.72f);
        Color[] bars =
        [
            Color.White,
            Color.Yellow,
            Color.Cyan,
            new Color(0, 200, 0),   // a bright but slightly toned-down green
            Color.Magenta,
            Color.Red,
            Color.Blue,
        ];
        for (int i = 0; i < bars.Length; i++)
        {
            int x0 = i * w / 7;
            int x1 = (i + 1) * w / 7;
            spriteBatch.Draw(pixel, new Rectangle(x0, 0, x1 - x0, topH), bars[i]);
        }

        // BOTTOM STRIP — four dark blocks, gap-free with the same integer tiling. Being
        // much darker than the bars above, this strip makes the card's top and bottom
        // plainly different so the rotation orientation is never ambiguous.
        Color[] blocks =
        [
            new Color(20, 30, 90),  // deep blue
            new Color(12, 12, 18),  // near-black
            new Color(70, 20, 70),  // deep magenta
            new Color(12, 12, 18),  // near-black again
        ];
        for (int i = 0; i < blocks.Length; i++)
        {
            int x0 = i * w / 4;
            int x1 = (i + 1) * w / 4;
            spriteBatch.Draw(pixel, new Rectangle(x0, topH, x1 - x0, h - topH), blocks[i]);
        }

        spriteBatch.End();
    }

    // Recreate the render target whenever the back buffer size changes (e.g. the user
    // resizes the window). A render target has a fixed pixel size baked in at
    // creation, so a stale one would no longer match the screen — the capture would
    // be the wrong resolution and stretch or crop. Rebuilding on resize keeps it 1:1.
    void EnsureRenderTargets()
    {
        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;
        if (sceneTarget == null || sceneTarget.Width != w || sceneTarget.Height != h)
        {
            sceneTarget?.Dispose();
            // Basic constructor — no mipmaps, no custom format. Just a color texture
            // the size of the screen that we can both draw into and sample from.
            sceneTarget = new RenderTarget2D(GraphicsDevice, w, h);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            sceneTarget?.Dispose();
            pixel?.Dispose();
        }

        base.Dispose(disposing);
    }
}
