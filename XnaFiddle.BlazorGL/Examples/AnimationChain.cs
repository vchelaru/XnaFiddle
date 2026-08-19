using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FlatRedBall.AnimationChain;
using FlatRedBall2.AnimationEditorCommon;
using FontStashSharp;

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;
    FontSystem fontSystem;

    AnimationPlayer<AnimationFrame> player;
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
        AnimationChainList<AnimationFrame> animations = Content.Load<AnimationChainList<AnimationFrame>>("PlatformerAnimations");

        // AnimationPlayer plays one named chain at a time and advances it over time.
        player = new AnimationPlayer<AnimationFrame>(animations) { IsLooping = true };
        Play("CharacterWalkRight");

        using var stream = TitleContainer.OpenStream(
            Path.Combine(Content.RootDirectory, "std/DroidSans.ttf"));
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

        if (kb.IsKeyDown(Keys.Right))
            Play("CharacterRunRight");
        else if (kb.IsKeyDown(Keys.Left))
            Play("CharacterRunLeft");
        else if (kb.IsKeyDown(Keys.Up))
            Play("CharacterJumpRight");
        else
            Play("CharacterWalkRight");

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
        Vector2 screenCenter = new Vector2(
            GraphicsDevice.Viewport.Width / 2f,
            GraphicsDevice.Viewport.Height / 2f);

        // Center on the current frame's size so the sprite doesn't jump around as its
        // source rectangle changes between animations.
        PixelRectangle? source = player.CurrentFrame?.SourceRectangle;
        Vector2 origin = source.HasValue
            ? new Vector2(source.Value.Width / 2f, source.Value.Height / 2f)
            : Vector2.Zero;

        // DrawAnimation is FlatRedBall.AnimationChain's own SpriteBatch extension: it reads
        // the player's current frame and applies flip, per-frame offset, and per-frame color
        // for you -- no manual Rectangle/SpriteEffects bookkeeping needed.
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        spriteBatch.DrawAnimation(player, screenCenter, Color.White, origin, scale: 6f);
        spriteBatch.End();
    }

    void DrawLabel()
    {
        var font = fontSystem.GetFont(22);

        spriteBatch.Begin();
        spriteBatch.DrawString(font, "Click here, then hold Left/Right to run, Up to jump",
            new Vector2(16, 14), Color.White);
        spriteBatch.DrawString(font, $"Now playing: {currentChain}",
            new Vector2(16, 42), new Color(150, 150, 170));
        spriteBatch.End();
    }
}
