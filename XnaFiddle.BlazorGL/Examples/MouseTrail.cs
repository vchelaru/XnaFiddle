using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGameGum;
using MonoGameGum.GueDeriving;
using Gum.Forms;
using Gum.Mvvm;
using Gum.Forms.Controls;

public class MyGame : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;
    Texture2D circle;

    const int TrailLength = 60;
    const int DotRadius = 8;
    Vector2[] trail = new Vector2[TrailLength];
    int trailIndex;
    bool trailFilled;

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

        int size = DotRadius * 2;
        circle = new Texture2D(GraphicsDevice, size, size);
        Color[] data = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - DotRadius + 0.5f;
                float dy = y - DotRadius + 0.5f;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                data[y * size + x] = dist <= DotRadius ? Color.White : Color.Transparent;
            }
        }
        circle.SetData(data);
    }

    protected override void Update(GameTime gameTime)
    {
        MouseState mouse = Mouse.GetState();
        trail[trailIndex] = new Vector2(mouse.X, mouse.Y);
        trailIndex++;
        if (trailIndex >= TrailLength)
        {
            trailIndex = 0;
            trailFilled = true;
        }
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        spriteBatch.Begin();

        int count = trailFilled ? TrailLength : trailIndex;
        for (int i = 0; i < count; i++)
        {
            // Draw oldest first so newest is on top
            int idx = trailFilled
                ? (trailIndex + i) % TrailLength
                : i;
            float t = (float)i / count;
            float hue = t * 0.8f;
            float r = MathF.Abs(MathF.Sin(hue * MathF.PI * 2f));
            float g = MathF.Abs(MathF.Sin((hue + 0.33f) * MathF.PI * 2f));
            float b = MathF.Abs(MathF.Sin((hue + 0.66f) * MathF.PI * 2f));
            spriteBatch.Draw(circle,
                trail[idx] - new Vector2(DotRadius),
                new Color(r, g, b) * t);
        }

        spriteBatch.End();
        base.Draw(gameTime);
    }
}
