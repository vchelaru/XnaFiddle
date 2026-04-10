using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MonoGameGum;
using MonoGameGum.GueDeriving;
using Gum.Wireframe;
using Gum.Forms;
using Gum.Forms.Controls;
using Gum.DataTypes;
using Gum.Managers;
using GumRuntime;
using KernSmith;
using KernSmith.Gum;
using RenderingLibrary;

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    GumService GumUI => GumService.Default;

    TextRuntime previewText;
    Label sizeValueLabel;
    int fontSize = 36;

    static readonly string[] FontFamilies = ["Droid Sans"];

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

        // Register bundled fonts so KernSmith can resolve them by name
        using (var stream = TitleContainer.OpenStream(
            Path.Combine(Content.RootDirectory, "DroidSans.ttf")))
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] fontBytes = ms.ToArray();
            Console.WriteLine($"[DynamicFonts] Registering Droid Sans ({fontBytes.Length} bytes)");
            BmFont.RegisterFont("Droid Sans", fontBytes);
        }

        // Test: can KernSmith actually generate a font in WASM?
        try
        {
            var testResult = BmFont.GenerateFromSystem("Droid Sans", new FontGeneratorOptions
            {
                Size = 24,
                Characters = CharacterSet.FromRanges((32, 126)),
                Backend = RasterizerBackend.StbTrueType
            });
            Console.WriteLine($"[DynamicFonts] Direct generation OK: {testResult.Pages.Count} pages, {testResult.Model.Characters.Count} chars");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DynamicFonts] Direct generation FAILED: {ex}");
        }

        // Wire up KernSmith for in-memory font generation.
        // StbTrueType backend is required for Blazor WASM (no native FreeType binary).
        CustomSetPropertyOnRenderable.InMemoryFontCreator =
            new KernSmithFontCreator(GraphicsDevice, RasterizerBackend.StbTrueType);
        Console.WriteLine("[DynamicFonts] KernSmithFontCreator initialized");

        BuildUI();
    }

    void BuildUI()
    {
        // ── Root: vertical stack ────────────────────────────────
        var root = new StackPanel();
        root.Visual.WidthUnits = DimensionUnitType.RelativeToParent;
        root.Visual.Width = 0;
        root.Visual.HeightUnits = DimensionUnitType.RelativeToParent;
        root.Visual.Height = 0;
        root.Spacing = 16;
        root.AddToRoot();

        // ── Controls row ────────────────────────────────────────
        var controlRow = new StackPanel();
        controlRow.Orientation = Orientation.Horizontal;
        controlRow.Spacing = 16;
        controlRow.Visual.Height = 40;
        controlRow.Visual.HeightUnits = DimensionUnitType.Absolute;
        root.AddChild(controlRow);

        // Font family picker
        var fontLabel = new Label();
        fontLabel.Text = "Font:";
        fontLabel.Width = 50;
        fontLabel.Height = 40;
        controlRow.AddChild(fontLabel);

        var fontCombo = new ComboBox();
        fontCombo.Width = 180;
        fontCombo.Height = 40;
        foreach (string family in FontFamilies)
            fontCombo.Items.Add(family);
        fontCombo.SelectedIndex = 0;
        fontCombo.SelectionChanged += (_, _) =>
        {
            if (fontCombo.SelectedObject is string font)
                previewText.Font = font;
        };
        controlRow.AddChild(fontCombo);

        // Size controls
        var sizeLabel = new Label();
        sizeLabel.Text = "Size:";
        sizeLabel.Width = 50;
        sizeLabel.Height = 40;
        controlRow.AddChild(sizeLabel);

        var minusBtn = new Button();
        minusBtn.Text = " - ";
        minusBtn.Width = 44;
        minusBtn.Height = 40;
        minusBtn.Click += (_, _) =>
        {
            fontSize = Math.Max(8, fontSize - 2);
            UpdatePreview();
        };
        controlRow.AddChild(minusBtn);

        sizeValueLabel = new Label();
        sizeValueLabel.Text = fontSize.ToString();
        sizeValueLabel.Width = 40;
        sizeValueLabel.Height = 40;
        controlRow.AddChild(sizeValueLabel);

        var plusBtn = new Button();
        plusBtn.Text = " + ";
        plusBtn.Width = 44;
        plusBtn.Height = 40;
        plusBtn.Click += (_, _) =>
        {
            fontSize = Math.Min(120, fontSize + 2);
            UpdatePreview();
        };
        controlRow.AddChild(plusBtn);

        // Style toggles
        var boldCheck = new CheckBox();
        boldCheck.Text = "Bold";
        boldCheck.Checked += (_, _) => { previewText.IsBold = true; };
        boldCheck.Unchecked += (_, _) => { previewText.IsBold = false; };
        controlRow.AddChild(boldCheck);

        var italicCheck = new CheckBox();
        italicCheck.Text = "Italic";
        italicCheck.Checked += (_, _) => { previewText.IsItalic = true; };
        italicCheck.Unchecked += (_, _) => { previewText.IsItalic = false; };
        controlRow.AddChild(italicCheck);

        var outlineCheck = new CheckBox();
        outlineCheck.Text = "Outline";
        outlineCheck.Checked += (_, _) => { previewText.OutlineThickness = 2; };
        outlineCheck.Unchecked += (_, _) => { previewText.OutlineThickness = 0; };
        controlRow.AddChild(outlineCheck);

        // ── Preview text ────────────────────────────────────────
        previewText = new TextRuntime();
        previewText.Text = "The quick brown fox jumps over the lazy dog.\n\nABCDEFGHIJKLMNOPQRSTUVWXYZ\nabcdefghijklmnopqrstuvwxyz\n0123456789 !@#$%^&*()";
        previewText.Font = FontFamilies[0];
        previewText.FontSize = fontSize;
        previewText.WidthUnits = DimensionUnitType.RelativeToParent;
        previewText.Width = -32;
        previewText.HeightUnits = DimensionUnitType.RelativeToChildren;
        previewText.Height = 0;
        previewText.X = 16;
        previewText.Color = Color.White;
        root.Visual.Children.Add(previewText);
    }

    void UpdatePreview()
    {
        sizeValueLabel.Text = fontSize.ToString();
        previewText.FontSize = fontSize;
    }

    protected override void Update(GameTime gameTime)
    {
        GumUI.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(45, 45, 58));
        GumUI.Draw();
        base.Draw(gameTime);
    }
}
