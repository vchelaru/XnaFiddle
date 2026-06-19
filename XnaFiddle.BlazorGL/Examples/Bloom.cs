using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// BLOOM — multi-scale glow via a DOWNSAMPLED RENDER-TARGET PYRAMID.
//
// Bloom makes bright areas bleed light into their surroundings. The classic way to
// get a soft, WIDE glow cheaply is to blur at several SCALES and add the results
// together: a small blur on a half-size image already reaches as far (in screen
// pixels) as a much larger blur on the full image, and a tiny blur on a 1/32-size
// image reaches across most of the screen. Summing a few of these gives the smooth,
// far-spreading falloff that a single blur can't afford.
//
// HOW THE SCALES ARE BUILT — and what this example deliberately does NOT use.
// A common trick is to generate mipmaps and sample coarse levels with LOD. This
// example avoids that entirely: mipmaps, RenderTarget2D(mipMap:true), tex2Dlod /
// SampleLevel, and float surface formats are all UNVERIFIED on this WebGL runtime,
// so instead we build the pyramid by hand as a chain of progressively smaller plain
// Color render targets and downsample by simply drawing each one into the next with
// linear filtering. Everything here is already proven to render in this app.
//
// PIPELINE (each step renders into a RenderTarget2D, then is sampled by the next):
//   1. Scene      -> sceneTarget     : draw the bright shapes on a near-black field.
//   2. Bright-pass -> extractTarget  : Bloom.BloomExtract.fx keeps only what is above
//                                      Threshold, so only the shapes will glow.
//   3. Pyramid     -> levelA[0..N-1] : repeatedly halve the image, and separably blur
//                                      (Bloom.Blur.fx, horizontal then vertical) at
//                                      each scale. levelA[i] holds the blurred result.
//   4. Composite   -> screen         : draw the original scene, then ADD every blurred
//                                      level back on top (BlendState.Additive), each
//                                      stretched to full screen and weighted.
//
// TUNABLE KNOBS (first-pass defaults — meant to be tuned visually by a human later):
//   threshold  how bright a pixel must be to bloom (higher = only the brightest glow)
//   radiusPx   blur reach per level, in that level's pixels (a few taps is plenty)
//   Weights    how much each scale contributes; coarser (wider) levels weighted up
//   Levels     how many halvings, i.e. how far the widest glow reaches
public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;

    // 1x1 white texture: the standard way to draw a solid-color rectangle with
    // SpriteBatch (tint it any color, stretch it to any Rectangle).
    Texture2D pixel;
    // Hard-edged opaque masks generated in memory (no content files): white where
    // the shape is filled, fully transparent outside. Tinted when drawn.
    Texture2D circleTex;
    Texture2D triangleTex;

    Effect bloomExtract; // Bloom.BloomExtract.fx — the bright-pass (Threshold)
    Effect blur;         // Bloom.Blur.fx — separable Gaussian reused at each scale (Offset)

    RenderTarget2D sceneTarget;   // the whole scene, full screen size
    RenderTarget2D extractTarget; // bright-pass result, full screen size

    // The pyramid. levelA[i] holds level i's blurred result; levelB[i] is the
    // ping-pong target between the horizontal and vertical blur passes. levelW/levelH
    // cache each level's pixel size (needed to turn radiusPx into a UV-space Offset).
    const int Levels = 5;
    RenderTarget2D[] levelA;
    RenderTarget2D[] levelB;
    int[] levelW;
    int[] levelH;

    // First-pass defaults; a human will tune these against the real visual.
    float threshold = 0.45f;
    float radiusPx = 2.5f;
    static readonly float[] Weights = [0.5f, 0.7f, 0.9f, 1.1f, 1.3f];

    static readonly Color SceneBackground = new Color(8, 8, 12); // near-black so the glow reads

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

        // In-memory GPU resources, NOT file I/O — this example loads no content files.
        pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });

        circleTex = CreateCircleTexture(256);
        triangleTex = CreateTriangleTexture(256);

        // Compiled in-browser when you press Run. The Content.Load key is the shader
        // tab's filename minus ".fx"; the example loader strips the "Bloom." prefix, so
        // Bloom.BloomExtract.fx -> tab "BloomExtract.fx" -> key "BloomExtract", and
        // Bloom.Blur.fx -> tab "Blur.fx" -> key "Blur".
        bloomExtract = Content.Load<Effect>("BloomExtract");
        blur = Content.Load<Effect>("Blur");
    }

    protected override void Draw(GameTime gameTime)
    {
        EnsureRenderTargets();

        int w = sceneTarget.Width;
        int h = sceneTarget.Height;
        Rectangle fullScreen = new Rectangle(0, 0, w, h);

        // 1) Render the scene into a full-screen render target.
        GraphicsDevice.SetRenderTarget(sceneTarget);
        DrawScene(w, h);

        // 2) Bright-pass: keep only what is above the threshold. Opaque blend because
        //    we are fully replacing extractTarget with the filtered scene.
        bloomExtract.Parameters["Threshold"]?.SetValue(threshold);
        GraphicsDevice.SetRenderTarget(extractTarget);
        GraphicsDevice.Clear(Color.Black);
        spriteBatch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.LinearClamp, effect: bloomExtract);
        spriteBatch.Draw(sceneTarget, fullScreen, Color.White);
        spriteBatch.End();

        // 3) Build the pyramid. Each level downsamples the PREVIOUS level's blurred
        //    result (the bright-pass for level 0), then blurs it separably in place.
        for (int i = 0; i < Levels; i++)
        {
            RenderTarget2D source = i == 0 ? extractTarget : levelA[i - 1];
            Rectangle levelRect = new Rectangle(0, 0, levelW[i], levelH[i]);

            // Downsample: drawing a larger image into a smaller render target with
            // LinearClamp averages neighbouring texels — a cheap box downsample.
            GraphicsDevice.SetRenderTarget(levelA[i]);
            GraphicsDevice.Clear(Color.Black);
            spriteBatch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.LinearClamp);
            spriteBatch.Draw(source, levelRect, Color.White);
            spriteBatch.End();

            // Horizontal blur: levelA[i] -> levelB[i]. Offset is in UV units, so we
            // divide the pixel radius by this level's width.
            blur.Parameters["Offset"]?.SetValue(new Vector2(radiusPx / levelW[i], 0f));
            GraphicsDevice.SetRenderTarget(levelB[i]);
            GraphicsDevice.Clear(Color.Black);
            spriteBatch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.LinearClamp, effect: blur);
            spriteBatch.Draw(levelA[i], Vector2.Zero, Color.White);
            spriteBatch.End();

            // Vertical blur: levelB[i] -> levelA[i]. levelA[i] now holds this level's
            // final, fully-blurred result, ready to be added in during composite.
            blur.Parameters["Offset"]?.SetValue(new Vector2(0f, radiusPx / levelH[i]));
            GraphicsDevice.SetRenderTarget(levelA[i]);
            GraphicsDevice.Clear(Color.Black);
            spriteBatch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.LinearClamp, effect: blur);
            spriteBatch.Draw(levelB[i], Vector2.Zero, Color.White);
            spriteBatch.End();
        }

        // 4) Composite to the screen: the crisp scene first, then ADD every blurred
        //    level on top. Additive blend is what turns the overlapping glows into a
        //    soft buildup of light instead of painting over each other.
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);

        spriteBatch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.LinearClamp);
        spriteBatch.Draw(sceneTarget, fullScreen, Color.White);
        spriteBatch.End();

        spriteBatch.Begin(blendState: BlendState.Additive, samplerState: SamplerState.LinearClamp);
        for (int i = 0; i < Levels; i++)
            spriteBatch.Draw(levelA[i], fullScreen, Color.White * Weights[i]);
        spriteBatch.End();

        base.Draw(gameTime);
    }

    // Draws the bright shapes into whichever render target is currently bound. A near-
    // black clear keeps the background from blooming; only the shapes are above the
    // bright-pass threshold. A plain SpriteBatch pass here also primes SpriteBatch's
    // vertex shader, which the pixel-only effects later rely on being active.
    void DrawScene(int w, int h)
    {
        GraphicsDevice.Clear(SceneBackground);

        int unit = Math.Min(w, h);
        spriteBatch.Begin();

        // Blue square (the 1x1 pixel stretched to a rectangle), on the left.
        int sq = (int)(unit * 0.18f);
        spriteBatch.Draw(pixel, CenteredRect(w * 0.22f, h * 0.50f, sq, sq), new Color(80, 160, 255));

        // Red/orange circle, in the middle.
        int cs = (int)(unit * 0.22f);
        spriteBatch.Draw(circleTex, CenteredRect(w * 0.50f, h * 0.42f, cs, cs), new Color(255, 140, 60));

        // Green triangle, on the right.
        int ts = (int)(unit * 0.24f);
        spriteBatch.Draw(triangleTex, CenteredRect(w * 0.78f, h * 0.56f, ts, ts), new Color(110, 255, 110));

        spriteBatch.End();
    }

    // A destination rectangle of the given size centered on (cx, cy).
    static Rectangle CenteredRect(float cx, float cy, int width, int height)
    {
        return new Rectangle((int)(cx - width / 2f), (int)(cy - height / 2f), width, height);
    }

    // White inside the circle, fully transparent outside — a hard edge (no AA needed,
    // the bloom blur softens everything anyway). Generated in memory via SetData.
    Texture2D CreateCircleTexture(int size)
    {
        Color[] data = new Color[size * size];
        float center = size / 2f;
        float radius = center - 1f;
        float radiusSquared = radius * radius;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - center;
                float dy = y + 0.5f - center;
                bool inside = dx * dx + dy * dy <= radiusSquared;
                data[y * size + x] = inside ? Color.White : new Color(0, 0, 0, 0);
            }
        }
        Texture2D texture = new Texture2D(GraphicsDevice, size, size);
        texture.SetData(data);
        return texture;
    }

    // White inside an upward-pointing triangle (apex at the top, base along the bottom),
    // fully transparent outside. At row y the filled half-width grows linearly from 0 at
    // the apex to half the texture at the base.
    Texture2D CreateTriangleTexture(int size)
    {
        Color[] data = new Color[size * size];
        float center = size / 2f;
        for (int y = 0; y < size; y++)
        {
            float t = (float)y / (size - 1); // 0 at the top apex, 1 at the bottom base
            float halfWidth = t * center;
            for (int x = 0; x < size; x++)
            {
                bool inside = Math.Abs(x + 0.5f - center) <= halfWidth;
                data[y * size + x] = inside ? Color.White : new Color(0, 0, 0, 0);
            }
        }
        Texture2D texture = new Texture2D(GraphicsDevice, size, size);
        texture.SetData(data);
        return texture;
    }

    // Recreate every render target when the back buffer size changes (e.g. the user
    // resizes the window). A render target's pixel size is fixed at creation, so a stale
    // one would no longer match the screen. The pyramid levels are 1/2, 1/4, ... of the
    // screen; max(1, ...) keeps them at least one pixel on very small or very wide windows.
    void EnsureRenderTargets()
    {
        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;
        if (sceneTarget != null && sceneTarget.Width == w && sceneTarget.Height == h)
            return;

        sceneTarget?.Dispose();
        extractTarget?.Dispose();
        if (levelA != null)
        {
            for (int i = 0; i < Levels; i++)
            {
                levelA[i]?.Dispose();
                levelB[i]?.Dispose();
            }
        }

        // Plain Color render targets — no mipmaps, no custom/float format.
        sceneTarget = new RenderTarget2D(GraphicsDevice, w, h);
        extractTarget = new RenderTarget2D(GraphicsDevice, w, h);

        levelA = new RenderTarget2D[Levels];
        levelB = new RenderTarget2D[Levels];
        levelW = new int[Levels];
        levelH = new int[Levels];
        for (int i = 0; i < Levels; i++)
        {
            int lw = Math.Max(1, w >> (i + 1));
            int lh = Math.Max(1, h >> (i + 1));
            levelW[i] = lw;
            levelH[i] = lh;
            levelA[i] = new RenderTarget2D(GraphicsDevice, lw, lh);
            levelB[i] = new RenderTarget2D(GraphicsDevice, lw, lh);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            sceneTarget?.Dispose();
            extractTarget?.Dispose();
            if (levelA != null)
            {
                for (int i = 0; i < Levels; i++)
                {
                    levelA[i]?.Dispose();
                    levelB[i]?.Dispose();
                }
            }
            pixel?.Dispose();
            circleTex?.Dispose();
            triangleTex?.Dispose();
            bloomExtract?.Dispose();
            blur?.Dispose();
        }

        base.Dispose(disposing);
    }
}
