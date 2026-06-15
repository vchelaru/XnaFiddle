using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Gum;
using Gum.GueDeriving;
using Gum.Wireframe;
using Gum.Forms;
using Gum.Converters;
using RenderingLibrary.Graphics;
using MonoGameAndGum.Renderables;

// Gum shape runtimes (CircleRuntime / RectangleRuntime) showcase, mirroring the
// Apos.Shapes grid. Gum draws shapes in retained mode: you create runtime objects
// once, add them to the Gum root, and GumService draws them every frame — there is
// no per-frame ShapeBatch.Begin()/End() like Apos.Shapes.
//
// Rows are shape kinds; columns are rendering features. A single CircleRuntime /
// RectangleRuntime can combine fill, outline (stroke), rounded corners, gradients,
// dashes, and drop shadows — set via the properties below.
public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    GumService GumUI => GumService.Default;

    // One column per rendering feature, left to right.
    enum Feature { Filled, Outline, FillAndOutline, Dashed, Gradient, DropShadow }
    static readonly Feature[] Columns =
    {
        Feature.Filled, Feature.Outline, Feature.FillAndOutline,
        Feature.Dashed, Feature.Gradient, Feature.DropShadow,
    };

    static readonly Color Fill   = new Color(70, 160, 210);
    static readonly Color Stroke = new Color(245, 205, 95);
    static readonly Color GradTo = new Color(225, 70, 120);
    static readonly Color Shadow = new Color(0, 0, 0, 150);

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

        // Gum.Shapes (the Gum.Shapes.KNI package) provides the Apos.Shapes-backed
        // renderer for filled shapes, gradients, and drop shadows. Without this call
        // the runtimes still draw, but only as plain outlines.
        ShapeRenderer.Self.Initialize();

        BuildGrid();
    }

    void BuildGrid()
    {
        float canvasW = GraphicalUiElement.CanvasWidth;
        float canvasH = GraphicalUiElement.CanvasHeight;

        int rows = 3;                  // Circle, Rectangle, Rounded Rectangle
        int cols = Columns.Length;

        float cellW = canvasW / cols;
        float cellH = canvasH / rows;
        float size = MathF.Min(cellW, cellH) * 0.55f;

        for (int row = 0; row < rows; row++)
        {
            float cy = row * cellH + cellH / 2f;
            for (int col = 0; col < cols; col++)
            {
                float cx = col * cellW + cellW / 2f;
                GraphicalUiElement shape = MakeShape(row, col, size);

                // Position by the default top-left origin so the shape sits centered in its cell.
                shape.X = cx - shape.Width / 2f;
                shape.Y = cy - shape.Height / 2f;
                shape.AddToRoot();
            }
        }
    }

    // CircleRuntime and RectangleRuntime expose the same fill/stroke/effect properties
    // but share no shape-specific base type, so each kind is configured in its own branch.
    GraphicalUiElement MakeShape(int row, int col, float size)
    {
        Feature feature = Columns[col];

        if (row == 0)
        {
            var circle = new CircleRuntime();
            circle.Width = size;
            circle.Height = size;   // Circles are sized by Width/Height, not a radius.
            circle.IsAntialiased = true;

            circle.IsFilled = IsFilled(feature);
            circle.FillColor = Fill;
            circle.StrokeColor = Stroke;
            circle.StrokeWidth = StrokeWidth(feature);
            circle.StrokeDashLength = DashLength(feature);
            circle.StrokeGapLength = GapLength(feature);

            if (feature == Feature.Gradient) ApplyGradient(circle);
            if (feature == Feature.DropShadow) ApplyShadow(circle);
            return circle;
        }

        var rect = new RectangleRuntime();
        rect.Width = size * 1.4f;
        rect.Height = size;
        rect.IsAntialiased = true;
        rect.CornerRadius = row == 2 ? size * 0.28f : 0f;  // row 2 = rounded rectangle

        rect.IsFilled = IsFilled(feature);
        rect.FillColor = Fill;
        rect.StrokeColor = Stroke;
        rect.StrokeWidth = StrokeWidth(feature);
        rect.StrokeDashLength = DashLength(feature);
        rect.StrokeGapLength = GapLength(feature);

        if (feature == Feature.Gradient) ApplyGradient(rect);
        if (feature == Feature.DropShadow) ApplyShadow(rect);
        return rect;
    }

    static bool IsFilled(Feature f) =>
        f is Feature.Filled or Feature.FillAndOutline or Feature.Gradient or Feature.DropShadow;

    static float StrokeWidth(Feature f) =>
        f is Feature.Outline or Feature.FillAndOutline or Feature.Dashed ? 3f : 0f;

    static float DashLength(Feature f) => f == Feature.Dashed ? 10f : 0f;
    static float GapLength(Feature f) => f == Feature.Dashed ? 6f : 0f;

    // Linear gradient running corner-to-corner across the shape. Gradient coordinates
    // are expressed as a percentage of the shape so they scale with its size.
    static void ApplyGradient(CircleRuntime s)
    {
        s.UseGradient = true;
        s.GradientType = GradientType.Linear;
        s.Color2 = GradTo;
        SetGradientCorners(
            (v, u) => { s.GradientX1 = v; s.GradientX1Units = u; },
            (v, u) => { s.GradientY1 = v; s.GradientY1Units = u; },
            (v, u) => { s.GradientX2 = v; s.GradientX2Units = u; },
            (v, u) => { s.GradientY2 = v; s.GradientY2Units = u; });
    }

    static void ApplyGradient(RectangleRuntime s)
    {
        s.UseGradient = true;
        s.GradientType = GradientType.Linear;
        s.Color2 = GradTo;
        SetGradientCorners(
            (v, u) => { s.GradientX1 = v; s.GradientX1Units = u; },
            (v, u) => { s.GradientY1 = v; s.GradientY1Units = u; },
            (v, u) => { s.GradientX2 = v; s.GradientX2Units = u; },
            (v, u) => { s.GradientY2 = v; s.GradientY2Units = u; });
    }

    static void SetGradientCorners(
        Action<float, GeneralUnitType> x1, Action<float, GeneralUnitType> y1,
        Action<float, GeneralUnitType> x2, Action<float, GeneralUnitType> y2)
    {
        x1(0f, GeneralUnitType.Percentage);   y1(0f, GeneralUnitType.Percentage);
        x2(100f, GeneralUnitType.Percentage); y2(100f, GeneralUnitType.Percentage);
    }

    static void ApplyShadow(CircleRuntime s)
    {
        s.HasDropshadow = true;
        s.DropshadowColor = Shadow;
        s.DropshadowOffsetX = 5f;
        s.DropshadowOffsetY = 5f;
        s.DropshadowBlur = 6f;
    }

    static void ApplyShadow(RectangleRuntime s)
    {
        s.HasDropshadow = true;
        s.DropshadowColor = Shadow;
        s.DropshadowOffsetX = 5f;
        s.DropshadowOffsetY = 5f;
        s.DropshadowBlur = 6f;
    }

    protected override void Update(GameTime gameTime)
    {
        GumUI.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 30, 30));
        GumUI.Draw();
        base.Draw(gameTime);
    }
}
