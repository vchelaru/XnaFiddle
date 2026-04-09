using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using MonoGameGum;
using Gum.Forms;
using Gum.Forms.Controls;

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    GumService GumUI => GumService.Default;
    SoundEffect sound;
    SoundEffectInstance loopInstance;
    Label statusLabel;
    bool wasSpaceDown;
    bool wasEnterDown;
    bool isLooping;

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
        GumUI.Initialize(this, DefaultVisualsVersion.V3);

        var panel = new StackPanel();
        panel.Spacing = 12;
        panel.AddToRoot();

        var title = new Label();
        title.Text = "Sound Playback Demo";
        panel.AddChild(title);

        var instructions = new Label();
        instructions.Text = "SPACE = play once\nENTER = toggle loop";
        panel.AddChild(instructions);

        statusLabel = new Label();
        statusLabel.Text = "Ready";
        panel.AddChild(statusLabel);
    }

    protected override void LoadContent()
    {
        // Load a WAV sound effect from a content file.
        // Assets can be bundled with examples or drag-and-dropped onto the canvas.
        sound = Content.Load<SoundEffect>("powerup");

        // Create an instance for looped playback
        loopInstance = sound.CreateInstance();
        loopInstance.IsLooped = true;
    }

    protected override void Update(GameTime gameTime)
    {
        GumUI.Update(gameTime);
        KeyboardState kb = Keyboard.GetState();

        // Space = play once (fire-and-forget)
        bool spaceDown = kb.IsKeyDown(Keys.Space);
        if (spaceDown && !wasSpaceDown)
        {
            sound.Play();
            statusLabel.Text = "Played once";
        }
        wasSpaceDown = spaceDown;

        // Enter = toggle looped playback
        bool enterDown = kb.IsKeyDown(Keys.Enter);
        if (enterDown && !wasEnterDown)
        {
            if (isLooping)
            {
                loopInstance.Stop();
                statusLabel.Text = "Loop stopped";
            }
            else
            {
                loopInstance.Play();
                statusLabel.Text = "Looping...";
            }
            isLooping = !isLooping;
        }
        wasEnterDown = enterDown;

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 30, 46));
        GumUI.Draw();
        base.Draw(gameTime);
    }
}
