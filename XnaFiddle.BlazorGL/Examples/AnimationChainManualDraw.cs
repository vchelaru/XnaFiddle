using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using FlatRedBall2.AnimationEditorCommon;
using FontStashSharp;

// Closes AnimationFrameBase<Texture2D> over the renderer-agnostic FlatRedBall.AnimationChain.Common
// data model. No reference to FlatRedBall.AnimationChain.KNI/.MonoGame -- useful if you batch
// sprites yourself and only want the .achx/.achj parsing plus AnimationPlayer's playback/timing.
public class SimpleFrame : AnimationFrameBase<Texture2D>
{
}

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;
    FontSystem fontSystem;

    AnimationPlayer<SimpleFrame> player;
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

        // AnimationChainListSave (FlatRedBall.AnimationChain.Common) parses .achx/.achj with no
        // MonoGame/KNI dependency -- the dialect is picked from the path's extension.
        string achxPath = Path.Combine(Content.RootDirectory, "PlatformerAnimations.achx");
        AnimationChainListSave save = AnimationChainListSave.FromFile(achxPath, TitleContainer.OpenStream);

        player = new AnimationPlayer<SimpleFrame>(BuildAnimations(save)) { IsLooping = true };
        Play("CharacterWalkRight");

        using var stream = TitleContainer.OpenStream(
            Path.Combine(Content.RootDirectory, "std/DroidSans.ttf"));
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        fontSystem = new FontSystem();
        fontSystem.AddFont(ms.ToArray());
    }

    // Converts the portable save data into our own frame type -- resolving each frame's texture
    // and source rectangle by hand is the bridge AchxLoader/AnimationChainListSaveExtensions
    // normally do for you in the KNI/MonoGame package.
    AnimationChainList<SimpleFrame> BuildAnimations(AnimationChainListSave save)
    {
        var list = new AnimationChainList<SimpleFrame>();
        bool isPixelCoords = save.CoordinateType == TextureCoordinateType.Pixel;

        foreach (AnimationChainSave chainSave in save.AnimationChains)
        {
            var chain = new AnimationChain<SimpleFrame> { Name = chainSave.Name };

            foreach (AnimationFrameSave frameSave in chainSave.Frames)
            {
                // This example's texture names have no subdirectory, so we can pass them
                // straight to Content.Load; a real .achx with FileRelativeTextures would need
                // its texture paths resolved against the .achx's own directory first.
                Texture2D texture = Content.Load<Texture2D>(frameSave.TextureName);

                float scaleX = isPixelCoords ? 1f : texture.Width;
                float scaleY = isPixelCoords ? 1f : texture.Height;
                int left = (int)(frameSave.LeftCoordinate * scaleX);
                int top = (int)(frameSave.TopCoordinate * scaleY);
                int right = (int)(frameSave.RightCoordinate * scaleX);
                int bottom = (int)(frameSave.BottomCoordinate * scaleY);

                chain.Add(new SimpleFrame
                {
                    Texture = texture,
                    FrameLength = TimeSpan.FromSeconds(frameSave.FrameLength),
                    FlipHorizontal = frameSave.FlipHorizontal,
                    FlipVertical = frameSave.FlipVertical,
                    SourceRectangle = new PixelRectangle(left, top, right - left, bottom - top),
                });
            }

            list.Add(chain);
        }

        return list;
    }

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

    // Plain SpriteBatch.Draw -- no DrawAnimation helper. The tradeoff for going manual: this
    // example does not apply per-frame RelativeX/RelativeY offset, alpha, or color operations,
    // even though SimpleFrame carries that data -- add it yourself if your game needs it.
    void DrawSprite()
    {
        SimpleFrame frame = player.CurrentFrame;
        if (frame?.Texture == null || !frame.SourceRectangle.HasValue)
            return;

        PixelRectangle pr = frame.SourceRectangle.Value;
        Rectangle source = new Rectangle(pr.X, pr.Y, pr.Width, pr.Height);

        SpriteEffects effects = SpriteEffects.None;
        if (frame.FlipHorizontal)
            effects |= SpriteEffects.FlipHorizontally;
        if (frame.FlipVertical)
            effects |= SpriteEffects.FlipVertically;

        Vector2 screenCenter = new Vector2(
            GraphicsDevice.Viewport.Width / 2f,
            GraphicsDevice.Viewport.Height / 2f);
        Vector2 origin = new Vector2(source.Width / 2f, source.Height / 2f);

        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        spriteBatch.Draw(frame.Texture, screenCenter, source, Color.White,
            0f, origin, 6f, effects, 0f);
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
