using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using FontStashSharp;

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;
    FontSystem fontSystem;
    float time;

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

        // Load the TTF font as raw bytes from the content directory.
        // You can also drag-and-drop your own .ttf files onto the canvas.
        using var stream = TitleContainer.OpenStream(
            Path.Combine(Content.RootDirectory, "DroidSans.ttf"));
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        byte[] fontBytes = ms.ToArray();
        fontSystem = new FontSystem();
        fontSystem.AddFont(fontBytes);
    }

    protected override void Update(GameTime gameTime)
    {
        time += (float)gameTime.ElapsedGameTime.TotalSeconds;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 30, 46));

        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;

        spriteBatch.Begin();

        // Title
        var titleFont = fontSystem.GetFont(40);
        string title = "FontStashSharp";
        var titleSize = titleFont.MeasureString(title);
        spriteBatch.DrawString(titleFont, title,
            new Vector2((w - titleSize.X) / 2f, 20),
            Color.White);

        // Demonstrate multiple font sizes
        int y = 80;
        int[] sizes = { 12, 16, 20, 24, 32 };
        for (int i = 0; i < sizes.Length; i++)
        {
            var font = fontSystem.GetFont(sizes[i]);
            string text = $"{sizes[i]}px - The quick brown fox jumps over the lazy dog";
            spriteBatch.DrawString(font, text,
                new Vector2(20, y),
                Color.LightGray);
            y += sizes[i] + 12;
        }

        // Colored text
        y += 10;
        var colorFont = fontSystem.GetFont(28);
        Color[] rainbow = {
            Color.Red, Color.Orange, Color.Yellow,
            Color.Green, Color.Cyan, Color.Blue, Color.Violet
        };

        string colorText = "Rainbow";
        // Draw each character with a different color
        float x = 20;
        for (int i = 0; i < colorText.Length; i++)
        {
            string ch = colorText[i].ToString();
            Color color = rainbow[i % rainbow.Length];
            spriteBatch.DrawString(colorFont, ch,
                new Vector2(x, y), color);
            x += colorFont.MeasureString(ch).X;
        }

        // Animated text
        y += 50;
        var animFont = fontSystem.GetFont(24);
        string animText = "Dynamic text rendering!";
        float ax = 20;
        for (int i = 0; i < animText.Length; i++)
        {
            float offset = MathF.Sin(time * 3f + i * 0.4f) * 8f;
            float hue = ((time * 0.3f + i * 0.05f) % 1f);
            Color c = HsvToColor(hue, 0.7f, 1f);
            string ch = animText[i].ToString();
            spriteBatch.DrawString(animFont, ch,
                new Vector2(ax, y + offset), c);
            ax += animFont.MeasureString(ch).X;
        }

        spriteBatch.End();
        base.Draw(gameTime);
    }

    static Color HsvToColor(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1 - MathF.Abs((h * 6f) % 2f - 1));
        float m = v - c;
        float r, g, b;
        int sector = (int)(h * 6f) % 6;
        switch (sector)
        {
            case 0: r = c; g = x; b = 0; break;
            case 1: r = x; g = c; b = 0; break;
            case 2: r = 0; g = c; b = x; break;
            case 3: r = 0; g = x; b = c; break;
            case 4: r = x; g = 0; b = c; break;
            default: r = c; g = 0; b = x; break;
        }
        return new Color(r + m, g + m, b + m);
    }
}
