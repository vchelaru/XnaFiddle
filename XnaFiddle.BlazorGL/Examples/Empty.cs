using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// A minimal, do-nothing starting point: clears to cornflower blue and nothing else.
// Use this as a clean slate for your own project — add fields, content, update and draw
// logic where indicated below. The SpriteBatch is created but unused on purpose so it's
// ready the moment you start drawing.
public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;

    public Game1()
    {
        graphics = new GraphicsDeviceManager(this);
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        // Add your initialization logic here.
        base.Initialize();
    }

    protected override void LoadContent()
    {
        spriteBatch = new SpriteBatch(GraphicsDevice);
        // Load your textures, fonts, sounds, etc. here.
    }

    protected override void Update(GameTime gameTime)
    {
        // Add your update logic here.
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        // Add your drawing code here.
        base.Draw(gameTime);
    }
}
