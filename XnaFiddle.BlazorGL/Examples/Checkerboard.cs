using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public class MyGame : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;
    Texture2D pixel;
    float time;

    public MyGame()
    {
        graphics = new GraphicsDeviceManager(this);
        graphics.GraphicsProfile = GraphicsProfile.HiDef;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        spriteBatch = new SpriteBatch(GraphicsDevice);
        pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });
    }

    protected override void Update(GameTime gameTime)
    {
        time += (float)gameTime.ElapsedGameTime.TotalSeconds;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
        spriteBatch.Begin();

        int tileSize = 40;
        int cols = GraphicsDevice.Viewport.Width / tileSize + 1;
        int rows = GraphicsDevice.Viewport.Height / tileSize + 1;

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                if ((x + y) % 2 == 0)
                {
                    float h = (x + y + time) * 0.1f;
                    float r = MathF.Abs(MathF.Sin(h * MathF.PI * 2f));
                    float g = MathF.Abs(MathF.Sin((h + 0.33f) * MathF.PI * 2f));
                    float b = MathF.Abs(MathF.Sin((h + 0.66f) * MathF.PI * 2f));
                    spriteBatch.Draw(pixel,
                        new Rectangle(x * tileSize, y * tileSize, tileSize, tileSize),
                        new Color(r, g, b));
                }
            }
        }

        spriteBatch.End();
        base.Draw(gameTime);
    }
}
