using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FlatRedBall.AnimationChain;

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;

    AnimationPlayer player;
    string currentChain = "";

    public Game1()
    {
        graphics = new GraphicsDeviceManager(this);
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        spriteBatch = new SpriteBatch(GraphicsDevice);

        // Load a FlatRedBall AnimationChainList from an .achx file authored in the
        // FlatRedBall Animation Editor. The .achx slices frames out of
        // AnimatedSpritesheet.png; both files are bundled with this example.
        AnimationChainList animations = Content.Load<AnimationChainList>("PlatformerAnimations");

        // AnimationPlayer plays one named chain at a time and advances it over time.
        player = new AnimationPlayer(animations) { IsLooping = true };
        Play("CharacterIdleRight");
    }

    // Switch to a different animation chain only when it actually changes, so we
    // don't restart the animation every frame a key is held.
    void Play(string chainName)
    {
        if (currentChain == chainName)
            return;
        currentChain = chainName;
        player.Play(chainName);
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState kb = Keyboard.GetState();

        // Click the canvas first so it has focus, then use the arrow keys.
        if (kb.IsKeyDown(Keys.Right))
            Play("CharacterRunRight");
        else if (kb.IsKeyDown(Keys.Left))
            Play("CharacterRunLeft");
        else if (kb.IsKeyDown(Keys.Up))
            Play("CharacterJumpRight");
        else
            Play("CharacterIdleRight");

        // Advance the current animation by the elapsed frame time.
        player.Update(gameTime.ElapsedGameTime);

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 30, 46));

        AnimationFrame frame = player.CurrentFrame;
        if (frame != null && frame.Texture != null)
        {
            // SourceRectangle is the sub-region of the spritesheet for this frame.
            Rectangle? source = frame.SourceRectangle;
            int frameWidth = source?.Width ?? frame.Texture.Width;
            int frameHeight = source?.Height ?? frame.Texture.Height;

            Vector2 screenCenter = new Vector2(
                GraphicsDevice.Viewport.Width / 2f,
                GraphicsDevice.Viewport.Height / 2f);
            Vector2 origin = new Vector2(frameWidth / 2f, frameHeight / 2f);

            // A frame can request horizontal/vertical mirroring.
            SpriteEffects effects = SpriteEffects.None;
            if (frame.FlipHorizontal)
                effects |= SpriteEffects.FlipHorizontally;
            if (frame.FlipVertical)
                effects |= SpriteEffects.FlipVertically;

            // PointClamp keeps the pixel art crisp when scaled up.
            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            spriteBatch.Draw(frame.Texture, screenCenter, source, Color.White,
                0f, origin, 6f, effects, 0f);
            spriteBatch.End();
        }

        base.Draw(gameTime);
    }
}
