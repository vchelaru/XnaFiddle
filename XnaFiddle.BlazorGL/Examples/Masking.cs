using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// MASKING — sample TWO static texture (.png) files in ONE shader pass: one image is what
// gets drawn, the other is a mask that controls where it shows.
//
// This is the simplest possible two-texture setup. With SpriteBatch you get one texture
// "for free": whatever you pass to spriteBatch.Draw is bound to texture slot 0, which the
// shader sees as SpriteTexture. Any ADDITIONAL texture is passed in from C# as an Effect
// parameter — here MaskTexture (see Masking.Mask.fx). The pixel shader samples both with the
// same UVs and sets the image's alpha from the mask's brightness: white mask = fully visible,
// black = hidden, gray = partially transparent.
//
// (The Bloom example also mixes two textures in one pass, but its second texture is a render
// target it generates each frame. Using two plain .png files instead makes the two inputs
// easy to swap and inspect on their own — handy when a masked result isn't looking right.)
//
// The window shows the un-masked image on the LEFT and the masked result on the RIGHT: the
// mask punches the gradient down to a soft circle, so its corners drop out to the background.
// Swap Circle.png for your own black-and-white mask (drag a .png onto the page, then change
// the Content.Load name below) to mask any shape.
public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;

    Texture2D image;     // Gradient.png — the image being drawn (slot 0)
    Texture2D mask;      // Circle.png — the grayscale mask, fed to the shader as a parameter
    Effect maskEffect;   // Masking.Mask.fx — sets the image's alpha from the mask

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

        image = Content.Load<Texture2D>("Gradient");
        mask = Content.Load<Texture2D>("Circle");

        // Mask.fx is compiled to a runnable Effect in-browser when you press Run. The example
        // loader strips the "Masking." prefix, so Masking.Mask.fx loads as "Mask".
        maskEffect = Content.Load<Effect>("Mask");
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 30, 46));

        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;
        Vector2 origin = new Vector2(image.Width / 2f, image.Height / 2f);
        float scale = Math.Min(w * 0.35f / image.Width, h * 0.6f / image.Height);
        Vector2 leftPos = new Vector2(w * 0.28f, h / 2f);
        Vector2 rightPos = new Vector2(w * 0.72f, h / 2f);

        // Un-masked image on the left. This first pass also primes SpriteBatch's vertex shader,
        // which the pixel-only mask effect relies on being active.
        spriteBatch.Begin();
        spriteBatch.Draw(image, leftPos, null, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
        spriteBatch.End();

        // Same image on the right, this time through the mask. Bind the SECOND texture as a
        // parameter before drawing; the sprite we Draw (image) goes to slot 0 automatically.
        //
        // BlendState.NonPremultiplied is REQUIRED here. The shader outputs straight (non-
        // premultiplied) alpha — it lowers col.a but leaves col.rgb at full strength. The
        // default BlendState.AlphaBlend expects PREMULTIPLIED color (rgb already scaled by a),
        // so it would ADD the full image color over the background instead of hiding it, and
        // the mask would look like a darkening tint rather than a cut-out. NonPremultiplied
        // does src.rgb*src.a + dest*(1-src.a), which is the correct blend for straight alpha.
        maskEffect.Parameters["MaskTexture"]?.SetValue(mask);
        spriteBatch.Begin(blendState: BlendState.NonPremultiplied, effect: maskEffect);
        spriteBatch.Draw(image, rightPos, null, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
        spriteBatch.End();

        base.Draw(gameTime);
    }
}
