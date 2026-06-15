using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using Gum;
using Gum.Forms;
using Gum.Forms.Controls;

using nkast.Aether.Physics2D.Dynamics;

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    GumService GumUI => GumService.Default;

    SpriteBatch spriteBatch;
    BasicEffect spriteBatchEffect;

    Texture2D playerTexture;
    Texture2D groundTexture;
    Vector2 playerTextureSize;
    Vector2 groundTextureSize;
    Vector2 playerTextureOrigin;
    Vector2 groundTextureOrigin;

    KeyboardState oldKeyState;

    // Camera
    Vector3 cameraPosition = new Vector3(0, 1.70f, 0);
    float cameraViewWidth = 12.5f;

    // Physics
    World world;
    Body playerBody;
    Body groundBody;
    float playerBodyRadius = 1.5f / 2f;
    Vector2 groundBodySize = new Vector2(8f, 1f);

    // Gum UI
    Label instructionLabel;

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

        // Physics world
        world = new World();

        // Player (circle)
        Vector2 playerPosition = new Vector2(0, playerBodyRadius);
        playerBody = world.CreateBody(playerPosition, 0, BodyType.Dynamic);
        Fixture playerFixture = playerBody.CreateCircle(playerBodyRadius, 1f);
        playerFixture.Restitution = 0.3f;
        playerFixture.Friction = 0.5f;

        // Ground (rectangle)
        Vector2 groundPosition = new Vector2(0, -(groundBodySize.Y / 2f));
        groundBody = world.CreateBody(groundPosition, 0, BodyType.Static);
        Fixture groundFixture = groundBody.CreateRectangle(groundBodySize.X, groundBodySize.Y, 1f, Vector2.Zero);
        groundFixture.Restitution = 0.3f;
        groundFixture.Friction = 0.5f;

        // Gum UI for instructions
        GumUI.Initialize(this, DefaultVisualsVersion.V3);

        instructionLabel = new Label();
        instructionLabel.Text = "A / D = rotate    Space = jump    Arrow keys = camera";
        instructionLabel.X = 12;
        instructionLabel.Y = 12;
        instructionLabel.AddToRoot();
    }

    protected override void LoadContent()
    {
        spriteBatch = new SpriteBatch(GraphicsDevice);

        spriteBatchEffect = new BasicEffect(GraphicsDevice);
        spriteBatchEffect.TextureEnabled = true;

        playerTexture = Content.Load<Texture2D>("CircleSprite");
        groundTexture = Content.Load<Texture2D>("GroundSprite");

        playerTextureSize = new Vector2(playerTexture.Width, playerTexture.Height);
        groundTextureSize = new Vector2(groundTexture.Width, groundTexture.Height);
        playerTextureOrigin = playerTextureSize / 2f;
        groundTextureOrigin = groundTextureSize / 2f;
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        KeyboardState state = Keyboard.GetState();

        // Camera movement
        if (state.IsKeyDown(Keys.Left))
            cameraPosition.X -= dt * cameraViewWidth;
        if (state.IsKeyDown(Keys.Right))
            cameraPosition.X += dt * cameraViewWidth;
        if (state.IsKeyDown(Keys.Up))
            cameraPosition.Y += dt * cameraViewWidth;
        if (state.IsKeyDown(Keys.Down))
            cameraPosition.Y -= dt * cameraViewWidth;

        // Player controls
        if (state.IsKeyDown(Keys.A))
            playerBody.ApplyTorque(10);
        if (state.IsKeyDown(Keys.D))
            playerBody.ApplyTorque(-10);
        if (state.IsKeyDown(Keys.Space) && oldKeyState.IsKeyUp(Keys.Space))
            playerBody.ApplyLinearImpulse(new Vector2(0, 10));

        oldKeyState = state;

        // Step physics
        world.Step(dt);

        GumUI.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);

        // Set up world-space camera
        var vp = GraphicsDevice.Viewport;
        spriteBatchEffect.View = Matrix.CreateLookAt(cameraPosition, cameraPosition + Vector3.Forward, Vector3.Up);
        spriteBatchEffect.Projection = Matrix.CreateOrthographic(cameraViewWidth, cameraViewWidth / vp.AspectRatio, 0f, -1f);

        // Draw physics bodies (CullClockwise + FlipVertically for Y-up coordinate system)
        spriteBatch.Begin(rasterizerState: RasterizerState.CullClockwise, effect: spriteBatchEffect);
        spriteBatch.Draw(playerTexture, playerBody.Position, null, Color.White, playerBody.Rotation,
            playerTextureOrigin, new Vector2(playerBodyRadius * 2f) / playerTextureSize, SpriteEffects.FlipVertically, 0f);
        spriteBatch.Draw(groundTexture, groundBody.Position, null, Color.White, groundBody.Rotation,
            groundTextureOrigin, groundBodySize / groundTextureSize, SpriteEffects.FlipVertically, 0f);
        spriteBatch.End();

        // Gum UI overlay
        GumUI.Draw();

        base.Draw(gameTime);
    }
}
