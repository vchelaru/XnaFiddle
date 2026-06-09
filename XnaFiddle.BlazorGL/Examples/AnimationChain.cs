using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FlatRedBall.AnimationChain;
using FontStashSharp;

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;
    FontSystem fontSystem;

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
        Play("CharacterWalkRight");

        // A TTF loaded with FontStashSharp, used only to draw the on-screen controls.
        using var stream = TitleContainer.OpenStream(
            Path.Combine(Content.RootDirectory, "DroidSans.ttf"));
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        fontSystem = new FontSystem();
        fontSystem.AddFont(ms.ToArray());
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

        // Default is a calm walk; hold an arrow key to run or jump.
        if (kb.IsKeyDown(Keys.Right))
            Play("CharacterRunRight");
        else if (kb.IsKeyDown(Keys.Left))
            Play("CharacterRunLeft");
        else if (kb.IsKeyDown(Keys.Up))
            Play("CharacterJumpRight");
        else
            Play("CharacterWalkRight");

        // Advance the current animation by the elapsed frame time.
        player.Update(gameTime.ElapsedGameTime);

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 30, 46));

        DrawSprite();
        DrawLabel();

        base.Draw(gameTime);
    }

    void DrawSprite()
    {
        AnimationFrame frame = player.CurrentFrame;
        if (frame == null || frame.Texture == null)
            return;

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

    void DrawLabel()
    {
        var font = fontSystem.GetFont(22);

        // Keyboard input only reaches the game once the canvas has focus, so tell
        // the user to click first. "Now playing" shows the live chain name, which
        // updates as you press keys — the point of named animation chains.
        spriteBatch.Begin();
        spriteBatch.DrawString(font, "Click here, then hold Left/Right to run, Up to jump",
            new Vector2(16, 14), Color.White);
        spriteBatch.DrawString(font, $"Now playing: {currentChain}",
            new Vector2(16, 42), new Color(150, 150, 170));
        spriteBatch.End();
    }
}
