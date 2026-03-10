using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public class MyGame : Game
{
    GraphicsDeviceManager graphics;
    float hue;

    public MyGame()
    {
        graphics = new GraphicsDeviceManager(this);
        graphics.GraphicsProfile = GraphicsProfile.HiDef;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void Update(GameTime gameTime)
    {
        hue += (float)gameTime.ElapsedGameTime.TotalSeconds * 0.15f;
        if (hue > 1f) hue -= 1f;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        float r = MathF.Abs(MathF.Sin(hue * MathF.PI * 2f));
        float g = MathF.Abs(MathF.Sin((hue + 0.33f) * MathF.PI * 2f));
        float b = MathF.Abs(MathF.Sin((hue + 0.66f) * MathF.PI * 2f));
        GraphicsDevice.Clear(new Color(r, g, b));
        base.Draw(gameTime);
    }
}
