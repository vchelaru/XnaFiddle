using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Gum;
using Gum.GueDeriving;
using Gum.Wireframe;
using Gum.Forms;
using Gum.Forms.Controls;

// Theme packages (Gum.Themes.*.Kni) — each ships an Apply(GraphicsDevice) entry point plus a
// "<Name>Styling.ActiveStyle" object used below to pick a matching backdrop color.
using Gum.Themes.Bubblegum;
using Gum.Themes.DarkPro;
using Gum.Themes.Neon;
using Gum.Themes.ForestGlade;
using Gum.Themes.Retro95;

public class Game1 : Game
{
    GraphicsDeviceManager graphics;
    GumService GumUI => GumService.Default;

    // A selectable theme: display name, its Apply entry point, and the backdrop color to
    // clear to while it's active. "Default" reverts to stock, un-themed V3 visuals.
    class ThemeOption
    {
        public string Name;
        public Action<GraphicsDevice> Apply;
        public Func<Color> GetClearColor;
    }

    ThemeOption[] themes;
    int themeIndex;
    Color clearColor;

    // The theme picker is a row of ToggleButton — a Forms control type that is NOT part of the
    // showcase below (which covers Button, TextBox, CheckBox, Slider, ComboBox, ListBox,
    // RadioButtons), so it reads as chrome rather than another sample of an already-demonstrated
    // control. ToggleButton has no built-in mutual exclusion (unlike RadioButton's grouping), so
    // it's enforced by hand in the Checked handler below.
    List<ToggleButton> themeToggles = new List<ToggleButton>();

    // The Forms control showcase — destroyed and rebuilt on every theme change. Forms
    // controls resolve their visual from FrameworkElement.DefaultFormsTemplates at
    // construction time, so an already-built control does not re-skin in place; only a
    // freshly-constructed one picks up a newly-applied theme's templates.
    StackPanel controlsPanel;

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

        themes = new ThemeOption[]
        {
            new ThemeOption { Name = "Default",     Apply = ResetToDefaultTheme,   GetClearColor = () => Color.CornflowerBlue },
            new ThemeOption { Name = "Bubblegum",   Apply = BubblegumTheme.Apply,  GetClearColor = () => BubblegumStyling.ActiveStyle.Colors.Background },
            new ThemeOption { Name = "Dark Pro",    Apply = DarkProTheme.Apply,    GetClearColor = () => DarkProStyling.ActiveStyle.Colors.Background },
            new ThemeOption { Name = "Neon",        Apply = NeonTheme.Apply,       GetClearColor = () => NeonStyling.ActiveStyle.Colors.Background },
            new ThemeOption { Name = "Forest Glade",Apply = ForestGladeTheme.Apply,GetClearColor = () => ForestGladeStyling.ActiveStyle.Colors.CanopyDeep },
            new ThemeOption { Name = "Retro 95",    Apply = Retro95Theme.Apply,    GetClearColor = () => Retro95Styling.ActiveStyle.Colors.Surface },
        };

        BuildThemePicker();
        ApplyTheme(0);
        RebuildControlsPanel();
    }

    // Reverts to stock, unstyled V3 defaults — as if no theme had ever been applied. A theme's
    // Apply() only ever ADDS entries to DefaultFormsTemplates (InitializeDefaults uses
    // TryAdd), so clearing the dictionary first is what lets InitializeDefaults' TryAdd calls
    // take effect again and restore every control's stock V3 visual.
    static void ResetToDefaultTheme(GraphicsDevice graphicsDevice)
    {
        FrameworkElement.DefaultFormsTemplates.Clear();
        FormsUtilities.InitializeDefaults();
    }

    void BuildThemePicker()
    {
        var caption = new Label();
        caption.Text = "Theme:";
        caption.X = 16;
        caption.Y = 12;
        caption.AddToRoot();

        var pickerRow = new StackPanel();
        pickerRow.Orientation = Orientation.Horizontal;
        pickerRow.Spacing = 8;
        pickerRow.Visual.X = 16;
        pickerRow.Visual.Y = 40;
        pickerRow.AddToRoot();

        for (int i = 0; i < themes.Length; i++)
        {
            int capturedIndex = i; // must be a per-iteration local, or every toggle's Checked closes over the same shared loop variable

            var toggle = new ToggleButton();
            toggle.Text = themes[i].Name;
            toggle.Width = 108;
            toggle.IsChecked = i == 0; // set before subscribing so this doesn't itself raise Checked
            toggle.Checked += (_, _) =>
            {
                for (int j = 0; j < themeToggles.Count; j++)
                {
                    if (j != capturedIndex)
                        themeToggles[j].IsChecked = false;
                }

                if (capturedIndex != themeIndex)
                {
                    ApplyTheme(capturedIndex);
                    RebuildControlsPanel();
                }
            };
            pickerRow.AddChild(toggle);
            themeToggles.Add(toggle);
        }
    }

    void ApplyTheme(int index)
    {
        themeIndex = index;
        ThemeOption theme = themes[index];
        theme.Apply(GraphicsDevice);
        clearColor = theme.GetClearColor();

        Window.Title = $"Gum Theme Picker — {theme.Name}";
    }

    void RebuildControlsPanel()
    {
        controlsPanel?.RemoveFromRoot();

        var panel = new StackPanel();
        panel.Spacing = 16;
        panel.Visual.X = 16;
        panel.Visual.Y = 90;
        panel.AddToRoot();
        controlsPanel = panel;

        // Status label — updated by controls below
        var label = new Label();
        label.Text = "Interact with any control:";
        panel.AddChild(label);

        // Button
        var button = new Button();
        button.Text = "Click Me";
        button.Width = 200;
        button.Click += (_, _) =>
            label.Text = $"Button clicked @ {DateTime.Now:HH:mm:ss}";
        panel.AddChild(button);

        // TextBox
        var textBox = new TextBox();
        textBox.Placeholder = "Enter text here...";
        textBox.Width = 200;
        textBox.TextChanged += (_, _) =>
            label.Text = $"TextBox: {textBox.Text}";
        panel.AddChild(textBox);

        // CheckBox
        var checkBox = new CheckBox();
        checkBox.Text = "Check me";
        checkBox.Checked += (_, _) => label.Text = "CheckBox checked";
        checkBox.Unchecked += (_, _) => label.Text = "CheckBox unchecked";
        panel.AddChild(checkBox);

        // Slider
        var slider = new Slider();
        slider.Width = 200;
        slider.Minimum = 0;
        slider.Maximum = 100;
        slider.ValueChanged += (_, _) =>
            label.Text = $"Slider: {slider.Value:0.0}";
        panel.AddChild(slider);

        // ComboBox
        var comboBox = new ComboBox();
        for (int i = 0; i < 10; i++)
            comboBox.Items.Add($"Option {i}");
        comboBox.SelectionChanged += (_, _) =>
            label.Text = "ComboBox: " + comboBox.SelectedObject;
        panel.AddChild(comboBox);

        // ListBox
        var listBox = new ListBox();
        listBox.Visual.Width = 200;
        listBox.Visual.Height = 120;
        for (int i = 0; i < 10; i++)
            listBox.Items.Add($"Item {i}");
        listBox.SelectionChanged += (_, _) =>
            label.Text = $"ListBox: {listBox.SelectedObject} (index {listBox.SelectedIndex})";
        panel.AddChild(listBox);

        // Radio buttons
        var radioGroup = new StackPanel();
        panel.AddChild(radioGroup);

        var radioA = new RadioButton();
        radioA.Text = "Option A";
        radioA.Checked += (_, _) => label.Text = "Radio: Option A";
        radioGroup.AddChild(radioA);

        var radioB = new RadioButton();
        radioB.Text = "Option B";
        radioB.Checked += (_, _) => label.Text = "Radio: Option B";
        radioGroup.AddChild(radioB);

        var radioC = new RadioButton();
        radioC.Text = "Option C";
        radioC.Checked += (_, _) => label.Text = "Radio: Option C";
        radioGroup.AddChild(radioC);
    }

    protected override void Update(GameTime gameTime)
    {
        GumUI.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(clearColor);
        GumUI.Draw();
        base.Draw(gameTime);
    }
}
