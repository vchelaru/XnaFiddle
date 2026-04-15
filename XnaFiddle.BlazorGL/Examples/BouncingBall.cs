using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MonoGameGum;
using MonoGameGum.GueDeriving;
using Gum.Wireframe;
using Gum.Forms;
using Gum.Mvvm;
using Gum.Forms.Controls;

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;
    Texture2D circle;

    Vector2 position;

    const int Radius = 30;
    Vector2 velocity = new Vector2(300f, 200f);
    Color ballColor = Color.Red;

    public Game1()
    {
        graphics = new GraphicsDeviceManager(this);
        if (GraphicsAdapter.DefaultAdapter.IsProfileSupported(GraphicsProfile.HiDef))
            graphics.GraphicsProfile = GraphicsProfile.HiDef;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        base.Initialize();

        position = new Vector2(
            GraphicsDevice.Viewport.Width / 2f,
            GraphicsDevice.Viewport.Height / 2f);
    }

    protected override void LoadContent()
    {
        spriteBatch = new SpriteBatch(GraphicsDevice);

        // Create a circle texture procedurally
        int size = Radius * 2;
        circle = new Texture2D(GraphicsDevice, size, size);
        Color[] data = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - Radius + 0.5f;
                float dy = y - Radius + 0.5f;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                data[y * size + x] = dist <= Radius ? ballColor : Color.Transparent;
            }
        }
        circle.SetData(data);
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        position += velocity * dt;

        // Bounce off walls
        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;

        if (position.X - Radius < 0) { position.X = Radius; velocity.X = MathF.Abs(velocity.X); }
        if (position.X + Radius > w) { position.X = w - Radius; velocity.X = -MathF.Abs(velocity.X); }
        if (position.Y - Radius < 0) { position.Y = Radius; velocity.Y = MathF.Abs(velocity.Y); }
        if (position.Y + Radius > h) { position.Y = h - Radius; velocity.Y = -MathF.Abs(velocity.Y); }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        spriteBatch.Begin();
        spriteBatch.Draw(circle, position - new Vector2(Radius), Color.White);
        spriteBatch.End();
        base.Draw(gameTime);
    }
}
