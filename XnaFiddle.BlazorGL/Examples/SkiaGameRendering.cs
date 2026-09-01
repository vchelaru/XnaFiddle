using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaGameRendering;
using SkiaSharp;

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    SkiaRenderTarget2D canvas;

    // Columns 0/1/2 (Filled / Border / Fill+Border) use the same flat color on every shape in the
    // grid, so their paints are built once and reused. Only the gradient columns (3/4) need a
    // per-cell paint, since their shader depends on each shape's position — see DrawShape.
    SKPaint fillPaint;
    SKPaint borderPaint;
    SKPaint fillBorderFillPaint;

    const float Thickness = 4f;

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
        fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(80, 180, 220) };
        borderPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = Thickness,
            Color = new SKColor(220, 160, 60),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        fillBorderFillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(60, 180, 130) };
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 30, 30));

        // SkiaRenderer.IsReady flips true once the browser has finished creating the WebGL2 host
        // context (immediate on desktop, briefly async here) -- poll it every Draw and initialize
        // once, from the fiddle's own code. Game never touches SkiaGameWebGlHost or
        // SkiaWebGlBackend directly -- see SkiaGameRenderingPlugin.cs for the harness side.
        if (!SkiaRenderer.IsInitialized && SkiaRenderer.IsReady)
            SkiaRenderer.Initialize(GraphicsDevice);

        if (SkiaRenderer.IsInitialized)
        {
            int w = GraphicsDevice.Viewport.Width;
            int h = GraphicsDevice.Viewport.Height;
            canvas ??= new SkiaRenderTarget2D(GraphicsDevice, w, h);

            canvas.Begin();
            DrawGrid(canvas.Canvas, w, h);
            canvas.End();
        }

        base.Draw(gameTime);
    }

    // Mirrors Examples/AposShapes.cs's 6x5 showcase grid: rows = shape type, columns = fill mode.
    // Static (no animation) — recomputed from the viewport every frame like Apos's version, which
    // is cheap (arithmetic plus a couple of disposable gradient paints per cell), so it stays
    // correct if the canvas resizes.
    void DrawGrid(SKCanvas c, int w, int h)
    {
        const int cols = 5; // Filled, Border, Fill+Border, Linear Gradient, Radial Gradient
        const int rows = 6; // Circle, Rectangle, Rounded Rect, Line, Hexagon, Triangle

        float cellW = w / (float)cols;
        float cellH = h / (float)rows;
        float shapeSize = MathF.Min(cellW, cellH) * 0.42f;

        for (int row = 0; row < rows; row++)
        {
            float cy = row * cellH + cellH / 2f;

            for (int col = 0; col < cols; col++)
            {
                float cx = col * cellW + cellW / 2f;
                DrawShape(c, row, col, cx, cy, shapeSize);
            }
        }
    }

    void DrawShape(SKCanvas c, int row, int col, float cx, float cy, float size)
    {
        switch (row)
        {
            case 0: // Circle
                switch (col)
                {
                    case 0: c.DrawCircle(cx, cy, size, fillPaint); break;
                    case 1: c.DrawCircle(cx, cy, size, borderPaint); break;
                    case 2: c.DrawCircle(cx, cy, size, fillBorderFillPaint); c.DrawCircle(cx, cy, size, borderPaint); break;
                    case 3: using (var p = LinearGradientPaint(cx, cy, size)) c.DrawCircle(cx, cy, size, p); break;
                    case 4: using (var p = RadialGradientPaint(cx, cy, size)) c.DrawCircle(cx, cy, size, p); break;
                }
                break;

            case 1: // Rectangle
            {
                SKRect rect = RectAround(cx, cy, size);
                switch (col)
                {
                    case 0: c.DrawRect(rect, fillPaint); break;
                    case 1: c.DrawRect(rect, borderPaint); break;
                    case 2: c.DrawRect(rect, fillBorderFillPaint); c.DrawRect(rect, borderPaint); break;
                    case 3: using (var p = LinearGradientPaint(cx, cy, size)) c.DrawRect(rect, p); break;
                    case 4: using (var p = RadialGradientPaint(cx, cy, size)) c.DrawRect(rect, p); break;
                }
                break;
            }

            case 2: // Rounded rect
            {
                SKRect rect = RectAround(cx, cy, size);
                float rounded = size * 0.35f;
                switch (col)
                {
                    case 0: c.DrawRoundRect(rect, rounded, rounded, fillPaint); break;
                    case 1: c.DrawRoundRect(rect, rounded, rounded, borderPaint); break;
                    case 2: c.DrawRoundRect(rect, rounded, rounded, fillBorderFillPaint); c.DrawRoundRect(rect, rounded, rounded, borderPaint); break;
                    case 3: using (var p = LinearGradientPaint(cx, cy, size)) c.DrawRoundRect(rect, rounded, rounded, p); break;
                    case 4: using (var p = RadialGradientPaint(cx, cy, size)) c.DrawRoundRect(rect, rounded, rounded, p); break;
                }
                break;
            }

            case 3: // Line -- SkiaSharp's DrawLine always strokes zero-area geometry, so it can't
                    // express a *filled* line the way Apos.Shapes' ShapeBatch.FillLine does. A
                    // horizontal capsule (rounded rect, corner radius = half height) gives the same
                    // thickness+rounded-end look.
            {
                float radius = size * 0.15f;
                SKRect rect = SKRect.Create(cx - size - radius, cy - radius, size * 2 + radius * 2, radius * 2);
                switch (col)
                {
                    case 0: c.DrawRoundRect(rect, radius, radius, fillPaint); break;
                    case 1: c.DrawRoundRect(rect, radius, radius, borderPaint); break;
                    case 2: c.DrawRoundRect(rect, radius, radius, fillBorderFillPaint); c.DrawRoundRect(rect, radius, radius, borderPaint); break;
                    case 3: using (var p = LinearGradientPaint(cx, cy, size)) c.DrawRoundRect(rect, radius, radius, p); break;
                    case 4: using (var p = RadialGradientPaint(cx, cy, size)) c.DrawRoundRect(rect, radius, radius, p); break;
                }
                break;
            }

            case 4: // Hexagon
            {
                using SKPath path = PolygonPath(cx, cy, size, 6, -90f);
                switch (col)
                {
                    case 0: c.DrawPath(path, fillPaint); break;
                    case 1: c.DrawPath(path, borderPaint); break;
                    case 2: c.DrawPath(path, fillBorderFillPaint); c.DrawPath(path, borderPaint); break;
                    case 3: using (var p = LinearGradientPaint(cx, cy, size)) c.DrawPath(path, p); break;
                    case 4: using (var p = RadialGradientPaint(cx, cy, size)) c.DrawPath(path, p); break;
                }
                break;
            }

            case 5: // Triangle
            {
                using SKPath path = PolygonPath(cx, cy, size, 3, -90f);
                switch (col)
                {
                    case 0: c.DrawPath(path, fillPaint); break;
                    case 1: c.DrawPath(path, borderPaint); break;
                    case 2: c.DrawPath(path, fillBorderFillPaint); c.DrawPath(path, borderPaint); break;
                    case 3: using (var p = LinearGradientPaint(cx, cy, size)) c.DrawPath(path, p); break;
                    case 4: using (var p = RadialGradientPaint(cx, cy, size)) c.DrawPath(path, p); break;
                }
                break;
            }
        }
    }

    static SKRect RectAround(float cx, float cy, float size)
    {
        float rw = size * 2f;
        float rh = size * 1.3f;
        return SKRect.Create(cx - rw / 2f, cy - rh / 2f, rw, rh);
    }

    // A regular polygon inscribed in a circle of the given radius, centered on (cx, cy).
    // startDegrees rotates the first vertex (-90 puts it straight up, matching the "pointy top"
    // look of Apos.Shapes' hexagon/triangle).
    static SKPath PolygonPath(float cx, float cy, float radius, int sides, float startDegrees)
    {
        var path = new SKPath();
        for (int i = 0; i < sides; i++)
        {
            float angle = (startDegrees + i * 360f / sides) * (MathF.PI / 180f);
            float x = cx + radius * MathF.Cos(angle);
            float y = cy + radius * MathF.Sin(angle);
            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }
        path.Close();
        return path;
    }

    static SKPaint LinearGradientPaint(float cx, float cy, float size)
    {
        var a = new SKPoint(cx - size, cy);
        var b = new SKPoint(cx + size, cy);
        using SKShader shader = SKShader.CreateLinearGradient(
            a, b,
            new[] { new SKColor(220, 60, 80), new SKColor(60, 140, 220) },
            SKShaderTileMode.Clamp);
        return new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Shader = shader };
    }

    static SKPaint RadialGradientPaint(float cx, float cy, float size)
    {
        using SKShader shader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy), size,
            new[] { new SKColor(255, 220, 60), new SKColor(140, 40, 200) },
            SKShaderTileMode.Clamp);
        return new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Shader = shader };
    }
}
