using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

public class Game1 : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private OceanScene _scene = null!;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            GraphicsProfile = GraphicsProfile.HiDef,
            PreferredBackBufferWidth = 1600,
            PreferredBackBufferHeight = 900,
            PreferMultiSampling = false,
            SynchronizeWithVerticalRetrace = true,
        };

        Content.RootDirectory = "Content";
        IsFixedTimeStep = false;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        _scene = new OceanScene(GraphicsDevice, Content);
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keyboardState = Keyboard.GetState();

        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
            keyboardState.IsKeyDown(Keys.Escape))
        {
            Exit();
        }

        _scene.Update(gameTime);

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);

        _scene.Draw();

        base.Draw(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scene?.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class OceanScene : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Effect _effect;
    private readonly VertexBuffer _vertexBuffer;
    private readonly IndexBuffer _indexBuffer;

    private float _time;
    private Vector2 _previousMousePosition;
    private Vector3 _cameraPosition;
    private float _yaw;
    private float _pitch;
    private bool _dragging;

    public OceanScene(GraphicsDevice graphicsDevice, ContentManager content)
    {
        _graphicsDevice = graphicsDevice;
        _effect = content.Load<Effect>("Ocean");

        VertexPositionTexture[] vertices = new VertexPositionTexture[]
        {
            new VertexPositionTexture(new Vector3(-1f,  1f, 0f), new Vector2(0f, 0f)),
            new VertexPositionTexture(new Vector3( 1f,  1f, 0f), new Vector2(1f, 0f)),
            new VertexPositionTexture(new Vector3(-1f, -1f, 0f), new Vector2(0f, 1f)),
            new VertexPositionTexture(new Vector3( 1f, -1f, 0f), new Vector2(1f, 1f)),
        };

        short[] indices = new short[]
        {
            0, 1, 2,
            1, 3, 2,
        };

        _vertexBuffer = new VertexBuffer(
            _graphicsDevice,
            typeof(VertexPositionTexture),
            vertices.Length,
            BufferUsage.WriteOnly);

        _vertexBuffer.SetData(vertices);

        _indexBuffer = new IndexBuffer(
            _graphicsDevice,
            IndexElementSize.SixteenBits,
            indices.Length,
            BufferUsage.WriteOnly);

        _indexBuffer.SetData(indices);

        _cameraPosition = new Vector3(0f, 1.5f, 1f);
        _yaw = 0.35f;
        _pitch = 0.18f;
        _previousMousePosition = Vector2.Zero;
        _dragging = false;
    }

    public void Update(GameTime gameTime)
    {
        _time = (float)gameTime.TotalGameTime.TotalSeconds;
        MouseState mouseState = Mouse.GetState();
        Vector2 currentMousePosition = Vector2.Clamp(
            new Vector2(mouseState.X, mouseState.Y),
            Vector2.Zero,
            new Vector2(_graphicsDevice.Viewport.Width, _graphicsDevice.Viewport.Height));

        if (mouseState.LeftButton == ButtonState.Pressed)
        {
            if (_dragging)
            {
                Vector2 delta = currentMousePosition - _previousMousePosition;
                _yaw -= delta.X * 0.0065f;
                _pitch += delta.Y * 0.0045f;
                _pitch = MathHelper.Clamp(_pitch, -1.10f, 1.10f);
            }

            _previousMousePosition = currentMousePosition;
            _dragging = true;
        }
        else
        {
            _dragging = false;
        }
    }

    public void Draw()
    {
        Viewport viewport = _graphicsDevice.Viewport;

        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.DepthStencilState = DepthStencilState.None;

        _effect.Parameters["Resolution"]?.SetValue(new Vector2(viewport.Width, viewport.Height));
        _effect.Parameters["CameraPosition"]?.SetValue(_cameraPosition);
        _effect.Parameters["CameraAngles"]?.SetValue(new Vector2(_yaw, _pitch));
        _effect.Parameters["Time"]?.SetValue(_time);

        _graphicsDevice.SetVertexBuffer(_vertexBuffer);
        _graphicsDevice.Indices = _indexBuffer;

        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();

            _graphicsDevice.DrawIndexedPrimitives(
                PrimitiveType.TriangleList,
                0,
                0,
                2);
        }
    }

    public void Dispose()
    {
        _effect.Dispose();
        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();
    }
}
