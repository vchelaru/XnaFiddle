using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Apos.Shapes;

using MonoGameGum;
using MonoGameGum.GueDeriving;
using Gum.Wireframe;
using Gum.Forms;
using Gum.Mvvm;
using Gum.Forms.Controls;

public class MyGame : Game
{
    GraphicsDeviceManager graphics;
    ShapeBatch shapeBatch;

    float time;

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
        shapeBatch = new ShapeBatch(GraphicsDevice, Content);
    }

    protected override void Update(GameTime gameTime)
    {
        time += (float)gameTime.ElapsedGameTime.TotalSeconds;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);

        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;
        float cx = w / 2f;
        float cy = h / 2f;

        shapeBatch.Begin();

        // Orbiting circles
        for (int i = 0; i < 6; i++)
        {
            float angle = time + i * MathF.PI / 3f;
            float radius = 120f;
            float x = cx + MathF.Cos(angle) * radius;
            float y = cy + MathF.Sin(angle) * radius;

            float hue = i / 6f;
            Color fill = HueToColor(hue, 0.8f);
            Color border = HueToColor(hue, 1f);

            shapeBatch.DrawCircle(new Vector2(x, y), 30f, fill, border, 2f);
        }

        // Center filled circle
        shapeBatch.FillCircle(new Vector2(cx, cy), 40f, Color.White);

        // Rotating rectangle
        float rectAngle = time * 0.5f;
        float rx = cx + MathF.Cos(rectAngle) * 200f;
        float ry = cy + MathF.Sin(rectAngle) * 200f;
        shapeBatch.BorderRectangle(new Vector2(rx, ry), new Vector2(50, 30), Color.Coral, 2f);

        // Lines radiating from center
        for (int i = 0; i < 12; i++)
        {
            float angle = i * MathF.PI / 6f;
            float len = 250f + MathF.Sin(time * 2f + i) * 30f;
            Vector2 end = new Vector2(
                cx + MathF.Cos(angle) * len,
                cy + MathF.Sin(angle) * len);

            shapeBatch.BorderLine(new Vector2(cx, cy), end, 1f, Color.Gray);
        }

        shapeBatch.End();
        base.Draw(gameTime);
    }

    static Color HueToColor(float hue, float brightness)
    {
        float r = MathF.Abs(hue * 6f - 3f) - 1f;
        float g = 2f - MathF.Abs(hue * 6f - 2f);
        float b = 2f - MathF.Abs(hue * 6f - 4f);
        r = MathHelper.Clamp(r, 0f, 1f) * brightness;
        g = MathHelper.Clamp(g, 0f, 1f) * brightness;
        b = MathHelper.Clamp(b, 0f, 1f) * brightness;
        return new Color(r, g, b);
    }
}
