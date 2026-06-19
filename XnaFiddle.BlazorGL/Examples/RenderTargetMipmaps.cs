using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// RENDER-TARGET MIPMAPS — a DIAGNOSTIC, not a tutorial. It exists to let a human
// visually confirm two WebGL-runtime capabilities an upcoming bloom port depends on:
//   (1) a RenderTarget2D created with mipMap:true actually gets its mip chain
//       generated, and
//   (2) a shader can sample explicit mip LODs from it (Texture.SampleLevel).
//
// HOW IT WORKS: we render a high-frequency checkerboard INTO a 512x512 mipmapped
// render target, switch back to the screen (which is what triggers KNI's
// GL.GenerateMipmap for a target whose LevelCount > 1), then draw that SAME target
// to the screen six times in a row, asking the shader for LOD 0,1,2,3,4,5 each time.
//
// PASS = the six thumbnails go from a SHARP checkerboard (LOD 0, left) to
//        progressively BLURRIER/greyer (LOD 5, right), proving the render target's
//        mipmaps were generated and an explicit-LOD shader sampled them.
// FAIL = all six look identical (mips were not generated), or the LOD>0 thumbnails
//        are black/garbage (LOD sampling is not working). If it FAILS, the faithful
//        mip-atlas bloom is not viable on this runtime and the downsampled-pyramid
//        Bloom example is the path to take instead.
//
// This is the ONE example that intentionally USES mipmaps and SampleLevel — that is
// the whole point. (Contrast the Bloom example, which deliberately avoids both.)
public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;
    // A 1x1 white texture: the standard way to draw solid-color rectangles with
    // SpriteBatch. Tint it any color and stretch it to any Rectangle.
    Texture2D pixel;

    // Explicit-LOD sampling shader. Loaded by the key "MipLod" — the .fx filename
    // (RenderTargetMipmaps.MipLod.fx) minus the example prefix and extension —
    // mirroring how BlurPostProcess loads "Blur".
    Effect mipEffect;

    // The mipmapped render target under test. Created ONCE: it is a fixed
    // power-of-two size, independent of the window. POT keeps the mip test
    // unambiguous (every level halves cleanly to a 1x1 top), but note the real bloom
    // case is a screen-sized NPOT target, which WebGL2 also allows — so POT here is a
    // test convenience, not a requirement of the technique.
    RenderTarget2D mipTarget;

    const int TargetSize = 512;   // power-of-two => a clean 512,256,128,...,1 mip chain
    const int CheckerPx = 32;     // checkerboard cell size, in target pixels
    const int LodCount = 6;       // thumbnails / LOD levels to display (0..5)

    static readonly Color ScreenBackground = new Color(48, 48, 56); // neutral gray
    static readonly Color CheckerDark = new Color(24, 24, 32);

    public Game1()
    {
        graphics = new GraphicsDeviceManager(this);
        // HiDef matches the other examples and guarantees render-target + mipmap
        // support; the guard falls back gracefully on adapters that only expose Reach.
        if (GraphicsAdapter.DefaultAdapter.IsProfileSupported(GraphicsProfile.HiDef))
            graphics.GraphicsProfile = GraphicsProfile.HiDef;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        spriteBatch = new SpriteBatch(GraphicsDevice);

        // In-memory GPU resource — no file I/O. One white pixel draws every checker cell.
        pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });

        // Compiled in-browser from RenderTargetMipmaps.MipLod.fx when you press Run.
        mipEffect = Content.Load<Effect>("MipLod");

        // mipMap:true is the whole point — it allocates the full mip chain so KNI has
        // levels to populate when we unbind the target. Param order matches the KNI
        // ctor RenderTarget2D(gd, width, height, bool mipMap, SurfaceFormat, DepthFormat).
        // Fixed size, so it is built once here rather than rebuilt on resize.
        mipTarget = new RenderTarget2D(GraphicsDevice, TargetSize, TargetSize,
            true, SurfaceFormat.Color, DepthFormat.None);
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        // PASS 1 — draw a HIGH-FREQUENCY checkerboard INTO the mipmapped target. High
        // frequency is essential: it is what makes the mip blurring visibly obvious
        // (a smooth image would look the same at every LOD and prove nothing).
        GraphicsDevice.SetRenderTarget(mipTarget);
        DrawCheckerboard();

        // PASS 2 — unbind the target. In KNI this SetRenderTarget(null) is what
        // triggers GL.GenerateMipmap on a render target whose LevelCount > 1, filling
        // levels 1..N from level 0. Then clear the screen to a neutral gray.
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(ScreenBackground);

        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;

        // Six square thumbnails laid left->right. Size each to fit a row of LodCount
        // with a margin, and cap height so they stay on screen on short windows.
        int thumb = (int)Math.Min(w / (float)(LodCount + 1), h * 0.6f);
        int gap = Math.Max(4, thumb / 12);
        int totalWidth = LodCount * thumb + (LodCount - 1) * gap;
        int x0 = (w - totalWidth) / 2;
        int y0 = (h - thumb) / 2;

        // PASS 3 — draw the SAME mipTarget six times, asking the shader for LOD 0..5.
        // Immediate sort + a fresh Begin/End per thumbnail so each per-draw Lod value
        // actually takes effect (a batched/deferred Begin would collapse them to one
        // Lod). LinearClamp so within a level we see clean filtering, not point noise.
        for (int i = 0; i < LodCount; i++)
        {
            mipEffect.Parameters["Lod"]?.SetValue((float)i);

            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque,
                SamplerState.LinearClamp, null, null, mipEffect);
            Rectangle dest = new Rectangle(x0 + i * (thumb + gap), y0, thumb, thumb);
            spriteBatch.Draw(mipTarget, dest, Color.White);
            spriteBatch.End();
        }

        base.Draw(gameTime);
    }

    // Fills the currently bound target with a high-frequency checkerboard of
    // CheckerPx-sized squares, alternating white / dark. Nested loops drawing the
    // white `pixel` texture as rectangles; the dark clear underneath supplies the
    // other color, so we only draw the white cells.
    void DrawCheckerboard()
    {
        GraphicsDevice.Clear(CheckerDark);

        spriteBatch.Begin();
        int cells = TargetSize / CheckerPx;
        for (int row = 0; row < cells; row++)
        {
            for (int col = 0; col < cells; col++)
            {
                if (((row + col) & 1) == 0)
                {
                    Rectangle cell = new Rectangle(col * CheckerPx, row * CheckerPx,
                        CheckerPx, CheckerPx);
                    spriteBatch.Draw(pixel, cell, Color.White);
                }
            }
        }
        spriteBatch.End();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            mipTarget?.Dispose();
            mipEffect?.Dispose();
            pixel?.Dispose();
        }

        base.Dispose(disposing);
    }
}
