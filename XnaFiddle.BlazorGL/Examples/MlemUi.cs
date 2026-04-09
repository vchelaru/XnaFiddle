using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MLEM.Font;
using MLEM.Formatting;
using MLEM.Extended.Font;
using FontStashSharp;
using MLEM.Textures;
using MLEM.Misc;
using MLEM.Graphics;
using MLEM.Maths;
using MLEM.Animations;
using MLEM.Ui;
using MLEM.Ui.Elements;
using MLEM.Ui.Style;

public class Game1 : Game
{

    GraphicsDeviceManager graphics;
    SpriteBatch spriteBatch;
    UiSystem uiSystem;

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
        spriteBatch = new SpriteBatch(GraphicsDevice);

        // initialize the ui system and set up styling
        var texture = Content.Load<Texture2D>("Ui");
        uiSystem = new UiSystem(this, new UntexturedStyle(spriteBatch)
        {
            // when using a SpriteFont, use GenericSpriteFont. When using a MonoGame.Extended BitmapFont, use GenericBitmapFont.
            // Wrapping fonts like this allows for both types to be usable within MLEM.Ui easily
            // Supplying a bold and an italic version is optional
            Font = new GenericStashFont(LoadFont("RobotoRegular")),
            PanelTexture = new NinePatch(new TextureRegion(texture, 0, 8, 24, 24), 8),
            ButtonTexture = new NinePatch(new TextureRegion(texture, 24, 8, 16, 16), 4),
            TextFieldTexture = new NinePatch(new TextureRegion(texture, 24, 8, 16, 16), 4),
            ScrollBarBackground = new NinePatch(new TextureRegion(texture, 12, 0, 4, 8), 1, 1, 2, 2),
            ScrollBarScrollerTexture = new NinePatch(new TextureRegion(texture, 8, 0, 4, 8), 1, 1, 2, 2),
            CheckboxTexture = new NinePatch(new TextureRegion(texture, 24, 8, 16, 16), 4),
            CheckboxCheckmark = new TextureRegion(texture, 24, 0, 8, 8),
            RadioTexture = new NinePatch(new TextureRegion(texture, 16, 0, 8, 8), 3),
            RadioCheckmark = new TextureRegion(texture, 32, 0, 8, 8),
            DropdownClosedArrowTexture = new TextureRegion(texture, 40, 0, 8, 8),
            DropdownOpenedArrowTexture = new TextureRegion(texture, 48, 0, 8, 8),
            DropdownArrowPadding = new Padding(0, 4, 0, 0),
            TextScale = 0.08F
        })
        {
            GlobalScale = 8,
            AutoScaleWithScreen = true,
            AutoScaleReferenceSize = new Point(1280, 720)
        };

        // add our panel and demo contents
        // create the root panel that all the other components sit on and add it to the ui system
        var root = new Panel(Anchor.Center, new Vector2(80, 100), Vector2.Zero, false, true);
        uiSystem.Add("Content", root);

        root.AddChild(new Paragraph(Anchor.AutoLeft, 1, "This is a small demo for MLEM.Ui, a user interface library that is part of the MLEM Library for Extending MonoGame and FNA."));
        var image = root.AddChild(new Image(Anchor.AutoCenter, new Vector2(50, 50), new TextureRegion(texture, 0, 0, 8, 8)) {IsHidden = true, Padding = new Padding(3)});

        // Setting the x or y coordinate of the size to 1 or a lower number causes the width or height to be a percentage of the parent's width or height
        // (for example, setting the size's x to 0.75 would make the element's width be 0.75*parentWidth)
        root.AddChild(new Button(Anchor.AutoCenter, new Vector2(1, 10), "Toggle Grass Image", "This button shows a grass tile above it to show the automatic anchoring of objects.") {
            OnPressed = element => image.IsHidden = !image.IsHidden
        });

        root.AddChild(new VerticalSpace(3));
        root.AddChild(new Paragraph(Anchor.AutoLeft, 1, "Paragraphs can also contain <c Blue>formatting codes</c>, including colors and <i>text styles</i>. For more info, check out the <b>text formatting example</b>!"));

        root.AddChild(new VerticalSpace(3));
        root.AddChild(new Paragraph(Anchor.AutoLeft, 1, "Zoom in or out:"));
        root.AddChild(new Button(Anchor.AutoLeft, new Vector2(10), "+") {
            OnPressed = element => {
                if (element.Root.Scale < 2)
                    element.Root.Scale += 0.1F;
            }
        });
        root.AddChild(new Button(Anchor.AutoInline, new Vector2(10), "-") {
            OnPressed = element => {
                if (element.Root.Scale > 0.5F)
                    element.Root.Scale -= 0.1F;
            },
            PositionOffset = new Vector2(1, 0)
        });

        root.AddChild(new VerticalSpace(3));
        root.AddChild(new Checkbox(Anchor.AutoLeft, new Vector2(1, 10), "Checkbox 1!"));
        root.AddChild(new Checkbox(Anchor.AutoLeft, new Vector2(1, 10), "Checkbox 2!") {PositionOffset = new Vector2(0, 1)});

        root.AddChild(new VerticalSpace(3));
        root.AddChild(new RadioButton(Anchor.AutoLeft, new Vector2(1, 10), "Radio button 1!"));
        root.AddChild(new RadioButton(Anchor.AutoLeft, new Vector2(1, 10), "Radio button 2!") {PositionOffset = new Vector2(0, 1)});

        var slider = new Slider(Anchor.AutoLeft, new Vector2(1, 10), 5, 1) {
            StepPerScroll = 0.01F
        };
        root.AddChild(new Paragraph(Anchor.AutoLeft, 1, paragraph => "Slider is at " + (int) (slider.CurrentValue * 100) + "%") {PositionOffset = new Vector2(0, 1)});
        root.AddChild(slider);

        root.AddChild(new Button(Anchor.AutoCenter, new Vector2(0.5F, 10), "Fancy Hover") {
            PositionOffset = new Vector2(0, 1),
            MouseEnterAnimation = new UiAnimation(0.15, (a, s, e, p) => e.ScaleTransform(1 + Easings.OutSine(p) * 0.05F)),
            MouseExitAnimation = new UiAnimation(new UiAnimation.Step(TimeSpan.FromSeconds(0.15), (a, s, e, p) => e.ScaleTransform(1 + Easings.OutSine.ReverseOutput()(p) * 0.05F)) {
                Finished = (a, s, e) => e.Transform = Matrix.Identity
            })
        });

        root.AddChild(new Button(Anchor.AutoCenter, new Vector2(0.5F, 10), "Transform Ui", "This button causes the entire ui to be transformed (both in positioning, rotation and scale)") {
            OnPressed = element => {
                if (element.Root.Transform == Matrix.Identity) {
                    element.Root.Transform = Matrix.CreateScale(0.75F) * Matrix.CreateRotationZ(0.25F) * Matrix.CreateTranslation(50, -10, 0);
                } else {
                    element.Root.Transform = Matrix.Identity;
                }
            },
            PositionOffset = new Vector2(0, 1)
        });

        root.AddChild(new VerticalSpace(3));
        var dropdown = root.AddChild(new Dropdown(Anchor.AutoLeft, new Vector2(1, 10), "Dropdown Menu"));
        dropdown.AddElement("First Option");
        dropdown.AddElement("Second Option");
        dropdown.AddElement("Third Option");
        dropdown.AddElement(new Paragraph(Anchor.AutoLeft, 1, "Dropdowns are basically just prioritized panels, so they can contain all controls, including paragraphs and"));
        dropdown.AddElement(new Button(Anchor.AutoLeft, new Vector2(1, 10), "Buttons"));

        root.AddChild(new VerticalSpace(3));
        var alignPar = root.AddChild(new Paragraph(Anchor.AutoLeft, 1, "Paragraphs can have <l Left>left</l> aligned text, <l Right>right</l> aligned text and <l Center>center</l> aligned text."));
        alignPar.LinkAction = (l, c) => {
            if (Enum.TryParse<TextAlignment>(c.Match.Groups[1].Value, out var alignment))
                alignPar.Alignment = alignment;
        };

        // select the first element for auto-navigation
        root.Root.SelectElement(root.Children.First(c => c.CanBeSelected));
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        uiSystem.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);
        GraphicsDevice.Clear(Color.CornflowerBlue);
        uiSystem.Draw(gameTime, spriteBatch);
    }

    private SpriteFontBase LoadFont(string name)
    {
        // we use FontStashSharp fonts for this example; see the FontStashSharp example for more information
        var files = XnaFiddle.InMemoryContentManager.Files;
        var fontSystem = new FontSystem();
        fontSystem.AddFont(files[name]);
        return fontSystem.GetFont(64);
    }
}
