using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// BLOOM — the canonical XNA "Bloom Postprocess" pipeline: EXTRACT -> BLUR -> COMBINE.
//
// Bloom makes bright areas bleed light into their surroundings. The classic four-step
// structure (the one in the original XNA Bloom sample) is:
//   1. Scene        -> sceneTarget   : draw a 3x3 grid of bright neon squares stepping
//                                      through the hue wheel, drawn from a single white
//                                      pixel (no assets), on a near-black field.
//   2. Bright-pass  -> bloomTarget1  : Bloom.BloomExtract.fx keeps only what is above
//                                      Threshold, so only the squares survive to glow.
//   3. Separable    -> bloomTarget2  : Bloom.Blur.fx, a Gaussian run twice — horizontal
//      Gaussian     -> bloomTarget1    then vertical — softens the bright-pass into a glow.
//   4. Combine      -> screen        : Bloom.BloomCombine.fx samples BOTH the blurred glow
//                                      and the original scene and mixes them in ONE pass.
//
// WHY ONE COMBINE PASS (and not additive draws): the combine shader does a screen-style
// blend (it darkens the base where the bloom is strong, then adds). With the intensities
// at 1 that stays inside [0,1] and cannot clip, so a saturated hue can't fringe toward a
// primary the way summed additive draws do once a single channel saturates first.
//
// The bloom targets are HALF resolution — purely a standard performance/blur-width choice:
// a small blur on a half-size image reaches twice as far in screen pixels for half the
// fill cost, which is why the classic sample downsamples the bright-pass.
//
// Bloom.BloomExtract.fx gates on the MAX channel and scales the whole color uniformly,
// which PRESERVES HUE. (The original XNA extract thresholds per channel — simpler, but it
// crushes each color's weaker channel and tints saturated colors toward a pure primary.)
//
// COMBINE KNOBS (the classic sample's parameters; tune these live):
//   BloomIntensity    how strongly the glow is added
//   BaseIntensity     how strongly the original scene shows through
//   BloomSaturation   >1 makes the glow a touch more vivid, <1 washes it toward gray
//   BaseSaturation    same, applied to the underlying scene
public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;

    // 1x1 white texture: the standard way to draw a solid-color rectangle with
    // SpriteBatch (tint it any color, stretch it to any Rectangle).
    Texture2D pixel;

    Effect bloomExtract; // Bloom.BloomExtract.fx — the bright-pass (Threshold)
    Effect blur;         // Bloom.Blur.fx — separable Gaussian, run H then V (Offset)
    Effect bloomCombine; // Bloom.BloomCombine.fx — mixes the glow over the scene in one pass

    RenderTarget2D sceneTarget;   // full-res: the scene
    RenderTarget2D bloomTarget1;  // HALF-res: bright-pass, then the H+V blurred bloom
    RenderTarget2D bloomTarget2;  // HALF-res: ping-pong between the blur passes

    // Half-res bloom-target size, cached so the blur can turn a pixel radius into a
    // UV-space Offset (radius / width horizontally, radius / height vertically).
    int bloomW;
    int bloomH;

    // Tunable knobs (the classic XNA Bloom sample's parameters; tune these live).
    float threshold = 0.40f;       // bright-pass cutoff (Bloom.BloomExtract.fx)
    float blurRadiusPx = 3.0f;     // Gaussian reach per pass, in bloom-target pixels (Bloom.Blur.fx Offset)
    float bloomIntensity = 1.3f;   // how strongly the glow is added
    float baseIntensity = 1.0f;    // how strongly the original scene shows through
    float bloomSaturation = 1.2f;  // >1 makes the glow a touch more vivid
    float baseSaturation = 1.0f;

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

        // In-memory GPU resource, NOT file I/O — this example loads no content files.
        pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });

        // Compiled in-browser when you press Run. The Content.Load key is the shader
        // tab's filename minus ".fx"; the example loader strips the "Bloom." prefix, so
        // Bloom.BloomExtract.fx -> "BloomExtract", Bloom.Blur.fx -> "Blur", and
        // Bloom.BloomCombine.fx -> "BloomCombine".
        bloomExtract = Content.Load<Effect>("BloomExtract");
        blur = Content.Load<Effect>("Blur");
        bloomCombine = Content.Load<Effect>("BloomCombine");
    }

    protected override void Draw(GameTime gameTime)
    {
        EnsureRenderTargets();
        int w = sceneTarget.Width, h = sceneTarget.Height;
        Rectangle full = new Rectangle(0, 0, w, h);
        Rectangle bloomRect = new Rectangle(0, 0, bloomW, bloomH);

        // 1) Scene -> sceneTarget (full res).
        GraphicsDevice.SetRenderTarget(sceneTarget);
        DrawScene(w, h);

        // 2) Bright-pass: sceneTarget -> bloomTarget1, downsampling to half res (LinearClamp
        //    averages as it shrinks). Keeps only pixels above Threshold.
        bloomExtract.Parameters["Threshold"]?.SetValue(threshold);
        GraphicsDevice.SetRenderTarget(bloomTarget1);
        GraphicsDevice.Clear(Color.Black);
        spriteBatch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.LinearClamp, effect: bloomExtract);
        spriteBatch.Draw(sceneTarget, bloomRect, Color.White);
        spriteBatch.End();

        // 3) Horizontal blur: bloomTarget1 -> bloomTarget2. Offset is in UV units.
        blur.Parameters["Offset"]?.SetValue(new Vector2(blurRadiusPx / bloomW, 0f));
        GraphicsDevice.SetRenderTarget(bloomTarget2);
        GraphicsDevice.Clear(Color.Black);
        spriteBatch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.LinearClamp, effect: blur);
        spriteBatch.Draw(bloomTarget1, Vector2.Zero, Color.White);
        spriteBatch.End();

        // 4) Vertical blur: bloomTarget2 -> bloomTarget1. bloomTarget1 now holds the final glow.
        blur.Parameters["Offset"]?.SetValue(new Vector2(0f, blurRadiusPx / bloomH));
        GraphicsDevice.SetRenderTarget(bloomTarget1);
        GraphicsDevice.Clear(Color.Black);
        spriteBatch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.LinearClamp, effect: blur);
        spriteBatch.Draw(bloomTarget2, Vector2.Zero, Color.White);
        spriteBatch.End();

        // 5) Combine to the screen: the combine shader samples BOTH the blurred bloom
        //    (the sprite we draw, slot 0) and the original scene (BaseTexture parameter),
        //    mixes them in one pass, and writes once — no intermediate clipping.
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Color.Black);
        bloomCombine.Parameters["BaseTexture"]?.SetValue(sceneTarget);
        bloomCombine.Parameters["BloomIntensity"]?.SetValue(bloomIntensity);
        bloomCombine.Parameters["BaseIntensity"]?.SetValue(baseIntensity);
        bloomCombine.Parameters["BloomSaturation"]?.SetValue(bloomSaturation);
        bloomCombine.Parameters["BaseSaturation"]?.SetValue(baseSaturation);
        spriteBatch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.LinearClamp, effect: bloomCombine);
        spriteBatch.Draw(bloomTarget1, full, Color.White);   // sprite stretched to full screen; UVs 0..1 align bloom & base
        spriteBatch.End();

        base.Draw(gameTime);
    }

    // Draws the scene into whichever render target is currently bound: a 3x3 grid of
    // bright neon squares stepping through the hue wheel, every one the 1x1 white pixel
    // stretched and tinted (no generated textures, no loaded assets). A near-black clear
    // keeps the background from blooming; only the squares are above the bright-pass
    // threshold. The plain SpriteBatch pass here also primes SpriteBatch's vertex shader,
    // which the pixel-only effects later rely on being active.
    void DrawScene(int w, int h)
    {
        GraphicsDevice.Clear(SceneBackground);

        // Nine squares in a centered 3x3 grid with a visible gap between cells. The hue
        // sweeps row-major across the grid (red top-left -> magenta bottom-right). Square
        // cells sized off the smaller screen dimension keep the grid centered on any window.
        const int Count = 9;
        const int Cols = 3;
        const int Rows = 3;                          // Cols * Rows == Count
        int size = (int)(Math.Min(w, h) * 0.16f);    // each square
        int gap = (int)(size * 0.5f);                // spacing between squares
        int gridW = Cols * size + (Cols - 1) * gap;
        int gridH = Rows * size + (Rows - 1) * gap;
        int left = (w - gridW) / 2;
        int top = (h - gridH) / 2;

        spriteBatch.Begin();
        for (int i = 0; i < Count; i++)
        {
            int col = i % Cols;
            int row = i / Cols;
            int x = left + col * (size + gap);
            int y = top + row * (size + gap);
            Color color = FromHue(i * (360f / Count));
            spriteBatch.Draw(pixel, new Rectangle(x, y, size, size), color);
        }
        spriteBatch.End();
    }

    // Fully saturated, full-value color at the given hue (degrees, 0..360). Pure code,
    // so the grid sweeps the whole hue wheel without any art assets.
    static Color FromHue(float hueDegrees)
    {
        float h = (hueDegrees / 60f) % 6f;
        float x = 1f - Math.Abs(h % 2f - 1f);
        float r = 0f, g = 0f, b = 0f;
        switch ((int)h)
        {
            case 0: r = 1; g = x; break;
            case 1: r = x; g = 1; break;
            case 2: g = 1; b = x; break;
            case 3: g = x; b = 1; break;
            case 4: r = x; b = 1; break;
            default: r = 1; b = x; break;
        }
        return new Color(r, g, b);
    }

    // Recreate every render target when the back buffer size changes (e.g. the user
    // resizes the window). A render target's pixel size is fixed at creation, so a stale
    // one would no longer match the screen. The bloom targets are HALF res (a standard,
    // cheap way to get a wider, softer blur); max(1, ...) keeps them at least one pixel
    // on very small windows.
    void EnsureRenderTargets()
    {
        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;
        if (sceneTarget != null && sceneTarget.Width == w && sceneTarget.Height == h)
            return;

        // Release any texture slots that still reference the targets we are about to
        // dispose. The combine pass binds the scene as a SECOND sampler texture
        // (BaseTexture); the GraphicsDevice keeps that reference until something
        // overwrites it. SpriteBatch only ever re-sets slot 0, so slot 1 would still
        // point at the scene target we dispose here — and the next single-texture pass
        // then applies sampler state to an empty texture unit, which WebGL rejects with
        // INVALID_OPERATION ("no texture bound to target") on resize. Clearing the slots
        // first means we never dispose a texture the device still has bound.
        for (int i = 0; i < 4; i++)
            GraphicsDevice.Textures[i] = null;

        sceneTarget?.Dispose();
        bloomTarget1?.Dispose();
        bloomTarget2?.Dispose();

        // Plain Color render targets — no mipmaps, no custom/float format.
        sceneTarget = new RenderTarget2D(GraphicsDevice, w, h);

        bloomW = Math.Max(1, w / 2);
        bloomH = Math.Max(1, h / 2);
        bloomTarget1 = new RenderTarget2D(GraphicsDevice, bloomW, bloomH);
        bloomTarget2 = new RenderTarget2D(GraphicsDevice, bloomW, bloomH);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            sceneTarget?.Dispose();
            bloomTarget1?.Dispose();
            bloomTarget2?.Dispose();
            pixel?.Dispose();
            bloomExtract?.Dispose();
            blur?.Dispose();
            bloomCombine?.Dispose();
        }

        base.Dispose(disposing);
    }
}
