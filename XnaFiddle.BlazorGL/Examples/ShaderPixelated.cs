using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;
    Texture2D logo;
    Effect effect;

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

        // Pixelated.fx is compiled to an Effect in-browser when you press Run.
        effect = Content.Load<Effect>("Pixelated");
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 30, 46));

        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;
        Vector2 origin = new Vector2(logo.Width / 2f, logo.Height / 2f);
        float scale = Math.Min(w * 0.35f / logo.Width, h * 0.6f / logo.Height);
        Vector2 leftPos = new Vector2(w * 0.28f, h / 2f);
        Vector2 rightPos = new Vector2(w * 0.72f, h / 2f);

        // Original on the left. This first pass also primes SpriteBatch's vertex shader,
        // which the pixel-only effect relies on being active.
        spriteBatch.Begin();
        spriteBatch.Draw(logo, leftPos, null, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
        spriteBatch.End();

        // Same image on the right, with the shader applied.
        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, null, null, null, effect);
        spriteBatch.Draw(logo, rightPos, null, Color.White, 0f, origin, scale, SpriteEffects.None, 0f);
        spriteBatch.End();

        base.Draw(gameTime);
    }
}
