using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Apos.Shapes;

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    ShapeBatch shapeBatch;
    SpriteBatch spriteBatch;
    SpriteFont font;

    public Game1()
    {
        graphics = new GraphicsDeviceManager(this);
        if (GraphicsAdapter.DefaultAdapter.IsProfileSupported(GraphicsProfile.HiDef))
            graphics.GraphicsProfile = GraphicsProfile.HiDef;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        shapeBatch = new ShapeBatch(GraphicsDevice, Content);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 30, 30));

        int w = GraphicsDevice.Viewport.Width;
        int h = GraphicsDevice.Viewport.Height;

        int cols = 5;  // Filled, Border, Fill+Border, Linear Gradient, Radial Gradient
        int rows = 6;  // Circle, Rectangle, Rounded Rect, Line, Hexagon, Triangle

        float cellW = w / (float)cols;
        float cellH = h / (float)rows;
        float shapeSize = MathF.Min(cellW, cellH) * 0.42f;

        shapeBatch.Begin();

        // Draw each cell
        for (int row = 0; row < rows; row++)
        {
            float cy = row * cellH + cellH / 2f;

            for (int col = 0; col < cols; col++)
            {
                float cx = col * cellW + cellW / 2f;
                Vector2 center = new Vector2(cx, cy);

                Color fillColor = new Color(80, 180, 220);
                Color borderColor = new Color(220, 160, 60);
                Color fillBorderFill = new Color(60, 180, 130);  // Distinct green for Fill+Border column

                // Build gradient for gradient columns
                Gradient linearGrad = new Gradient(
                    new Vector2(cx - shapeSize, cy), new Color(220, 60, 80),
                    new Vector2(cx + shapeSize, cy), new Color(60, 140, 220),
                    Gradient.Shape.Linear);
                Gradient radialGrad = new Gradient(
                    center, new Color(255, 220, 60),
                    center + new Vector2(shapeSize, 0), new Color(140, 40, 200),
                    Gradient.Shape.Radial);

                DrawShape(row, col, center, shapeSize, fillColor, borderColor, fillBorderFill, linearGrad, radialGrad);
            }
        }

        shapeBatch.End();
        base.Draw(gameTime);
    }

    void DrawShape(int row, int col, Vector2 center, float size,
        Color fill, Color border, Color fillBorder, Gradient linearGrad, Gradient radialGrad)
    {
        float thickness = 2f;

        switch (row)
        {
            case 0: // Circle
                switch (col)
                {
                    case 0: shapeBatch.FillCircle(center, size, fill); break;
                    case 1: shapeBatch.BorderCircle(center, size, border, thickness); break;
                    case 2: shapeBatch.DrawCircle(center, size, fillBorder, border, thickness); break;
                    case 3: shapeBatch.FillCircle(center, size, linearGrad); break;
                    case 4: shapeBatch.FillCircle(center, size, radialGrad); break;
                }
                break;

            case 1: // Rectangle
            {
                Vector2 rSize = new Vector2(size * 2f, size * 1.3f);
                Vector2 xy = center - rSize / 2f;
                switch (col)
                {
                    case 0: shapeBatch.FillRectangle(xy, rSize, fill); break;
                    case 1: shapeBatch.BorderRectangle(xy, rSize, border, thickness); break;
                    case 2: shapeBatch.DrawRectangle(xy, rSize, fillBorder, border, thickness, 0f); break;
                    case 3: shapeBatch.FillRectangle(xy, rSize, linearGrad); break;
                    case 4: shapeBatch.FillRectangle(xy, rSize, radialGrad); break;
                }
                break;
            }

            case 2: // Rounded Rect
            {
                Vector2 rSize = new Vector2(size * 2f, size * 1.3f);
                Vector2 xy = center - rSize / 2f;
                float rounded = size * 0.35f;
                switch (col)
                {
                    case 0: shapeBatch.FillRectangle(xy, rSize, fill, rounded); break;
                    case 1: shapeBatch.BorderRectangle(xy, rSize, border, thickness, rounded); break;
                    case 2: shapeBatch.DrawRectangle(xy, rSize, fillBorder, border, thickness, rounded); break;
                    case 3: shapeBatch.FillRectangle(xy, rSize, linearGrad, rounded); break;
                    case 4: shapeBatch.FillRectangle(xy, rSize, radialGrad, rounded); break;
                }
                break;
            }

            case 3: // Line
            {
                Vector2 a = center - new Vector2(size, 0);
                Vector2 b = center + new Vector2(size, 0);
                float radius = size * 0.15f;
                switch (col)
                {
                    case 0: shapeBatch.FillLine(a, b, radius, fill); break;
                    case 1: shapeBatch.BorderLine(a, b, radius, border, thickness); break;
                    case 2: shapeBatch.DrawLine(a, b, radius, fillBorder, border, thickness); break;
                    case 3: shapeBatch.FillLine(a, b, radius, linearGrad); break;
                    case 4: shapeBatch.FillLine(a, b, radius, radialGrad); break;
                }
                break;
            }

            case 4: // Hexagon
                switch (col)
                {
                    case 0: shapeBatch.FillHexagon(center, size, fill); break;
                    case 1: shapeBatch.BorderHexagon(center, size, border, thickness); break;
                    case 2: shapeBatch.DrawHexagon(center, size, fillBorder, border, thickness); break;
                    case 3: shapeBatch.FillHexagon(center, size, linearGrad); break;
                    case 4: shapeBatch.FillHexagon(center, size, radialGrad); break;
                }
                break;

            case 5: // Triangle
                switch (col)
                {
                    case 0: shapeBatch.FillEquilateralTriangle(center, size * 0.55f, fill); break;
                    case 1: shapeBatch.BorderEquilateralTriangle(center, size * 0.55f, border, thickness); break;
                    case 2: shapeBatch.DrawEquilateralTriangle(center, size * 0.55f, fillBorder, border, thickness); break;
                    case 3: shapeBatch.FillEquilateralTriangle(center, size * 0.55f, linearGrad); break;
                    case 4: shapeBatch.FillEquilateralTriangle(center, size * 0.55f, radialGrad); break;
                }
                break;
        }
    }
}
