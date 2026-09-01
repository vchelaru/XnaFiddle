using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using SkiaSharp;

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SkiaRenderTarget2D canvas;
    SKPaint paint;
    float hue;

    public Game1()
    {
        graphics = new GraphicsDeviceManager(this);
        // The WebGL backend needs a real WebGL2 context, so this example requires HiDef
        // (see SkiaGameRendering's docs/webgl/quickstart.md).
        graphics.GraphicsProfile = GraphicsProfile.HiDef;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        paint = new SKPaint { IsAntialias = true };
    }

    protected override void Update(GameTime gameTime)
    {
        hue = (hue + (float)gameTime.ElapsedGameTime.TotalSeconds * 60f) % 360f;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 30, 30));

        // SkiaRenderer.IsReady flips true once the browser has finished creating the WebGL2
        // host context (immediate on desktop, briefly async here) -- poll it every Draw and
        // initialize once, from the fiddle's own code. Game never touches SkiaGameWebGlHost or
        // SkiaWebGlBackend directly -- see SkiaGameRenderingPlugin.cs for the harness side.
        if (!SkiaRenderer.IsInitialized && SkiaRenderer.IsReady)
            SkiaRenderer.Initialize(GraphicsDevice);

        if (SkiaRenderer.IsInitialized)
        {
            int w = GraphicsDevice.Viewport.Width;
            int h = GraphicsDevice.Viewport.Height;
            canvas ??= new SkiaRenderTarget2D(GraphicsDevice, w, h);

            canvas.Begin();
            float radius = Math.Min(w, h) * 0.25f;
            paint.Color = SKColor.FromHsv(hue, 70, 90);
            canvas.Canvas.DrawCircle(w / 2f, h / 2f, radius, paint);
            canvas.End();
        }

        base.Draw(gameTime);
    }
}
