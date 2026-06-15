using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Gum;
using Gum.GueDeriving;
using Gum.Wireframe;
using Gum.Forms;
using Gum.Forms.Controls;
using Gum.DataTypes;

public class Game1 : Game
{
    // ── Character data model ────────────────────────────────────────────────

    class CharacterData
    {
        public string Name;
        public int PinLength;           // proxy: only store how long the PIN was
        public string Class;            // "Warrior", "Mage", "Rogue"
        public string Difficulty;
        public float Strength;
        public float Agility;
        public bool Permadeath;

        public static string[] AchievementsFor(string cls) => cls switch
        {
            "Warrior" => new[] { "First Blood", "Iron Will", "100 Battles Won" },
            "Mage"    => new[] { "Arcane Scholar", "Spellbinder", "Ley Line Walker" },
            "Rogue"   => new[] { "Shadow Step", "Pickpocket Pro", "Unseen Blade" },
            _         => new[] { "First Steps" }
        };
    }

    // ── Gum service ─────────────────────────────────────────────────────────

    GraphicsDeviceManager _graphics;
    GumService GumUI => GumService.Default;

    // ── State ────────────────────────────────────────────────────────────────

    List<CharacterData> _characters = new();
    CharacterData _selected;            // currently selected character
    bool _updatingDetail;               // guard re-entrant updates

    // ── Persisted controls ───────────────────────────────────────────────────

    ListBox _characterList;
    Button _deleteButton;
    Button _playButton;
    Label _statusLabel;

    // Detail panel controls (rebuilt on selection)
    Label _detailNameLabel;
    RadioButton _detailWarrior;
    RadioButton _detailMage;
    RadioButton _detailRogue;
    ComboBox _detailDifficulty;
    Slider _detailStrength;
    Slider _detailAgility;
    CheckBox _detailPermadeath;
    ItemsControl _detailAchievements;

    // Creation window controls
    Window _createWindow;
    TextBox _createName;
    PasswordBox _createPin;
    RadioButton _createWarrior;
    RadioButton _createMage;
    RadioButton _createRogue;

    // ── Constructor ──────────────────────────────────────────────────────────

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        if (GraphicsAdapter.DefaultAdapter.IsProfileSupported(GraphicsProfile.HiDef))
            _graphics.GraphicsProfile = GraphicsProfile.HiDef;
        _graphics.PreferredBackBufferWidth  = 900;
        _graphics.PreferredBackBufferHeight = 600;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    // ── Initialize ───────────────────────────────────────────────────────────

    protected override void Initialize()
    {
        base.Initialize();
        GumUI.Initialize(this, DefaultVisualsVersion.V3);

        BuildMenuBar();
        BuildMainLayout();
        BuildCreationWindow();

        UpdateButtonStates();
    }

    // ── Menu bar ─────────────────────────────────────────────────────────────

    void BuildMenuBar()
    {
        var menu = new Menu();
        menu.AddToRoot();

        // File menu
        var fileItem = new MenuItem();
        fileItem.Header = "File";
        menu.Items.Add(fileItem);

        var exitItem = new MenuItem();
        exitItem.Header = "Exit";
        exitItem.Selected += (_, _) => _statusLabel.Text = "Goodbye!";
        fileItem.Items.Add(exitItem);

        // Help menu
        var helpItem = new MenuItem();
        helpItem.Header = "Help";
        menu.Items.Add(helpItem);

        var aboutItem = new MenuItem();
        aboutItem.Header = "About";
        aboutItem.Selected += (_, _) => _statusLabel.Text = "Gum UI Demo v1.0";
        helpItem.Items.Add(aboutItem);
    }

    // ── Main three-column layout ─────────────────────────────────────────────

    void BuildMainLayout()
    {
        const int menuHeight    = 32;
        const int statusHeight  = 28;
        const int leftWidth     = 220;
        const int splitterWidth = 6;

        // ── Left panel ──────────────────────────────────────────────────────
        var leftPanel = new StackPanel();
        leftPanel.X = 0;
        leftPanel.Y = menuHeight;
        leftPanel.Width  = leftWidth;
        // Fill vertically, leaving room for status bar at bottom
        leftPanel.Visual.Height = -(menuHeight + statusHeight);
        leftPanel.Visual.HeightUnits = DimensionUnitType.RelativeToParent;
        leftPanel.Spacing = 4;
        leftPanel.AddToRoot();

        var listHeader = new Label();
        listHeader.Text = "Characters";
        leftPanel.AddChild(listHeader);

        _characterList = new ListBox();
        _characterList.Visual.Width  = leftWidth - 4;
        _characterList.Visual.Height = -140;    // leave room for buttons
        _characterList.Visual.HeightUnits = DimensionUnitType.RelativeToParent;
        _characterList.SelectionChanged += OnCharacterSelected;
        leftPanel.AddChild(_characterList);

        var newCharBtn = new Button();
        newCharBtn.Text   = "New Character";
        newCharBtn.Width  = leftWidth - 4;
        newCharBtn.Height = 34;
        newCharBtn.Click += (_, _) => OpenCreateWindow();
        leftPanel.AddChild(newCharBtn);

        _deleteButton = new Button();
        _deleteButton.Text   = "Delete";
        _deleteButton.Width  = leftWidth - 4;
        _deleteButton.Height = 34;
        _deleteButton.Click += (_, _) => DeleteSelected();
        leftPanel.AddChild(_deleteButton);

        // ── Splitter ────────────────────────────────────────────────────────
        var splitter = new Splitter();
        splitter.X     = leftWidth;
        splitter.Y     = menuHeight;
        splitter.Width = splitterWidth;
        splitter.Visual.Height = -(menuHeight + statusHeight);
        splitter.Visual.HeightUnits = DimensionUnitType.RelativeToParent;
        splitter.Dock(Dock.FillVertically);
        splitter.AddToRoot();

        // ── Right detail ScrollViewer ────────────────────────────────────────
        var rightPanel = new ScrollViewer();
        rightPanel.X = leftWidth + splitterWidth;
        rightPanel.Y = menuHeight;
        rightPanel.Width  = -(leftWidth + splitterWidth);
        rightPanel.Visual.WidthUnits  = DimensionUnitType.RelativeToParent;
        rightPanel.Height = -(menuHeight + statusHeight);
        rightPanel.Visual.HeightUnits = DimensionUnitType.RelativeToParent;
        rightPanel.InnerPanel.StackSpacing = 6;
        rightPanel.AddToRoot();

        BuildDetailPanel(rightPanel);

        // ── Status label ─────────────────────────────────────────────────────
        _statusLabel = new Label();
        _statusLabel.Text = "Select or create a character to begin.";
        _statusLabel.Dock(Dock.Bottom);
        _statusLabel.Height = statusHeight;
        _statusLabel.AddToRoot();

        // ── Play button ──────────────────────────────────────────────────────
        _playButton = new Button();
        _playButton.Text   = "Play Selected";
        _playButton.Width  = 160;
        _playButton.Height = 34;
        _playButton.Dock(Dock.Bottom);
        _playButton.X = 4;
        _playButton.Click += (_, _) =>
        {
            if (_selected != null)
                _statusLabel.Text = $"Playing as {_selected.Name}...";
        };
        _playButton.AddToRoot();
    }

    // ── Detail panel (right side) ────────────────────────────────────────────

    void BuildDetailPanel(ScrollViewer parent)
    {
        // Name row
        var nameHeader = new Label();
        nameHeader.Text = "Name:";
        parent.AddChild(nameHeader);

        _detailNameLabel = new Label();
        _detailNameLabel.Text = "(no character selected)";
        parent.AddChild(_detailNameLabel);

        // Class row
        var classHeader = new Label();
        classHeader.Text = "Class:";
        parent.AddChild(classHeader);

        // RadioButtons inside their own StackPanel so they form one group
        var classGroup = new StackPanel();
        classGroup.Visual.Width      = 200;
        classGroup.Spacing           = 2;
        parent.AddChild(classGroup);

        _detailWarrior = new RadioButton();
        _detailWarrior.Text    = "Warrior";
        _detailWarrior.Checked += (_, _) => OnDetailClassChanged("Warrior");
        classGroup.AddChild(_detailWarrior);

        _detailMage = new RadioButton();
        _detailMage.Text    = "Mage";
        _detailMage.Checked += (_, _) => OnDetailClassChanged("Mage");
        classGroup.AddChild(_detailMage);

        _detailRogue = new RadioButton();
        _detailRogue.Text    = "Rogue";
        _detailRogue.Checked += (_, _) => OnDetailClassChanged("Rogue");
        classGroup.AddChild(_detailRogue);

        // Difficulty row
        var diffHeader = new Label();
        diffHeader.Text = "Difficulty:";
        parent.AddChild(diffHeader);

        _detailDifficulty = new ComboBox();
        _detailDifficulty.Items.Add("Easy");
        _detailDifficulty.Items.Add("Normal");
        _detailDifficulty.Items.Add("Hard");
        _detailDifficulty.Width = 160;
        _detailDifficulty.SelectionChanged += (_, _) => OnDetailDifficultyChanged();
        parent.AddChild(_detailDifficulty);

        // Strength slider
        var strHeader = new Label();
        strHeader.Text = "Strength:";
        parent.AddChild(strHeader);

        _detailStrength = new Slider();
        _detailStrength.Width   = 260;
        _detailStrength.Minimum = 0;
        _detailStrength.Maximum = 100;
        _detailStrength.Value   = 50;
        _detailStrength.ValueChanged += (_, _) => OnDetailStrengthChanged();
        parent.AddChild(_detailStrength);

        // Agility slider
        var agiHeader = new Label();
        agiHeader.Text = "Agility:";
        parent.AddChild(agiHeader);

        _detailAgility = new Slider();
        _detailAgility.Width   = 260;
        _detailAgility.Minimum = 0;
        _detailAgility.Maximum = 100;
        _detailAgility.Value   = 50;
        _detailAgility.ValueChanged += (_, _) => OnDetailAgilityChanged();
        parent.AddChild(_detailAgility);

        // Permadeath checkbox
        _detailPermadeath = new CheckBox();
        _detailPermadeath.Text      = "Permadeath";
        _detailPermadeath.Checked   += (_, _) => OnDetailPermadeathChanged(true);
        _detailPermadeath.Unchecked += (_, _) => OnDetailPermadeathChanged(false);
        parent.AddChild(_detailPermadeath);

        // Achievements
        var achHeader = new Label();
        achHeader.Text = "Achievements:";
        parent.AddChild(achHeader);

        _detailAchievements = new ItemsControl();
        _detailAchievements.Visual.Width  = 300;
        _detailAchievements.Visual.Height = 120;
        parent.AddChild(_detailAchievements);
    }

    // ── Creation window ──────────────────────────────────────────────────────

    void BuildCreationWindow()
    {
        _createWindow = new Window();
        _createWindow.Width  = 340;
        _createWindow.Height = 300;
        _createWindow.ResizeMode = ResizeMode.NoResize;
        // Window title text is set via its TitleBar label when available;
        // use a Label child as a header instead
        var titleLabel = new Label();
        titleLabel.Text = "Create New Character";
        titleLabel.Dock(Dock.Top);
        titleLabel.Y = 28;
        _createWindow.AddChild(titleLabel);

        // Name TextBox
        var nameLabel = new Label();
        nameLabel.Text = "Name:";
        nameLabel.X    = 12;
        nameLabel.Y    = 62;
        _createWindow.AddChild(nameLabel);

        _createName = new TextBox();
        _createName.Placeholder = "Character name...";
        _createName.X     = 12;
        _createName.Y     = 80;
        _createName.Width = 300;
        _createWindow.AddChild(_createName);

        // PIN PasswordBox
        var pinLabel = new Label();
        pinLabel.Text = "PIN:";
        pinLabel.X    = 12;
        pinLabel.Y    = 116;
        _createWindow.AddChild(pinLabel);

        _createPin = new PasswordBox();
        _createPin.Placeholder = "Enter PIN...";
        _createPin.X     = 12;
        _createPin.Y     = 134;
        _createPin.Width = 300;
        _createWindow.AddChild(_createPin);

        // Class RadioButtons — grouped in a StackPanel
        var classLabel = new Label();
        classLabel.Text = "Class:";
        classLabel.X    = 12;
        classLabel.Y    = 170;
        _createWindow.AddChild(classLabel);

        var classGroup = new StackPanel();
        classGroup.X       = 12;
        classGroup.Y       = 188;
        classGroup.Width   = 200;
        classGroup.Spacing = 2;
        _createWindow.AddChild(classGroup);

        _createWarrior = new RadioButton();
        _createWarrior.Text      = "Warrior";
        _createWarrior.IsChecked = true;
        classGroup.AddChild(_createWarrior);

        _createMage = new RadioButton();
        _createMage.Text = "Mage";
        classGroup.AddChild(_createMage);

        _createRogue = new RadioButton();
        _createRogue.Text = "Rogue";
        classGroup.AddChild(_createRogue);

        // Buttons
        var createBtn = new Button();
        createBtn.Text   = "Create";
        createBtn.Width  = 110;
        createBtn.Height = 34;
        createBtn.X      = 12;
        createBtn.Y      = 252;
        createBtn.Click += (_, _) => OnCreateCharacter();
        _createWindow.AddChild(createBtn);

        var cancelBtn = new Button();
        cancelBtn.Text   = "Cancel";
        cancelBtn.Width  = 110;
        cancelBtn.Height = 34;
        cancelBtn.X      = 132;
        cancelBtn.Y      = 252;
        cancelBtn.Click += (_, _) => CloseCreateWindow();
        _createWindow.AddChild(cancelBtn);
        // Window is NOT added to root here — we show it on demand
    }

    // ── Window open / close ──────────────────────────────────────────────────

    void OpenCreateWindow()
    {
        _createName.Text     = string.Empty;
        _createWarrior.IsChecked = true;
        _createWindow.Anchor(Gum.Wireframe.Anchor.Center);
        _createWindow.AddToRoot();
    }

    void CloseCreateWindow()
    {
        _createWindow.RemoveFromRoot();
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    void OnCreateCharacter()
    {
        string name = _createName.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            _statusLabel.Text = "Name cannot be empty.";
            return;
        }

        string cls = _createMage.IsChecked  ? "Mage"
                   : _createRogue.IsChecked ? "Rogue"
                   : "Warrior";

        var data = new CharacterData
        {
            Name       = name,
            PinLength  = _createPin.Password?.Length ?? 0,
            Class      = cls,
            Difficulty = "Normal",
            Strength   = 50f,
            Agility    = 50f,
            Permadeath = false
        };

        _characters.Add(data);
        _characterList.Items.Add(data.Name);
        _characterList.SelectedIndex = _characterList.Items.Count - 1;

        CloseCreateWindow();
        _statusLabel.Text = $"Created character: {name}";
    }

    void OnCharacterSelected(object sender, EventArgs e)
    {
        int idx = _characterList.SelectedIndex;
        if (idx < 0 || idx >= _characters.Count)
        {
            _selected = null;
            ClearDetailPanel();
        }
        else
        {
            _selected = _characters[idx];
            PopulateDetailPanel(_selected);
        }
        UpdateButtonStates();
    }

    void DeleteSelected()
    {
        int idx = _characterList.SelectedIndex;
        if (idx < 0 || idx >= _characters.Count)
            return;

        string name = _selected?.Name ?? string.Empty;
        _characters.RemoveAt(idx);
        _characterList.Items.RemoveAt(idx);
        _selected = null;
        ClearDetailPanel();
        UpdateButtonStates();
        _statusLabel.Text = $"Deleted character: {name}";
    }

    // Detail panel live-update handlers — guarded to avoid feedback loops
    void OnDetailClassChanged(string cls)
    {
        if (_updatingDetail || _selected == null) return;
        _selected.Class = cls;
        RefreshAchievements(_selected);
    }

    void OnDetailDifficultyChanged()
    {
        if (_updatingDetail || _selected == null) return;
        _selected.Difficulty = _detailDifficulty.SelectedObject as string ?? "Normal";
    }

    void OnDetailStrengthChanged()
    {
        if (_updatingDetail || _selected == null) return;
        _selected.Strength = (float)_detailStrength.Value;
    }

    void OnDetailAgilityChanged()
    {
        if (_updatingDetail || _selected == null) return;
        _selected.Agility = (float)_detailAgility.Value;
    }

    void OnDetailPermadeathChanged(bool value)
    {
        if (_updatingDetail || _selected == null) return;
        _selected.Permadeath = value;
    }

    // ── Detail panel helpers ──────────────────────────────────────────────────

    void PopulateDetailPanel(CharacterData data)
    {
        _updatingDetail = true;
        try
        {
            _detailNameLabel.Text = data.Name;

            _detailWarrior.IsChecked = (data.Class == "Warrior");
            _detailMage.IsChecked    = (data.Class == "Mage");
            _detailRogue.IsChecked   = (data.Class == "Rogue");

            // Find matching difficulty index
            int diffIdx = data.Difficulty switch { "Easy" => 0, "Hard" => 2, _ => 1 };
            _detailDifficulty.SelectedIndex = diffIdx;

            _detailStrength.Value  = data.Strength;
            _detailAgility.Value   = data.Agility;
            _detailPermadeath.IsChecked = data.Permadeath;

            RefreshAchievements(data);
        }
        finally
        {
            _updatingDetail = false;
        }
    }

    void ClearDetailPanel()
    {
        _updatingDetail = true;
        try
        {
            _detailNameLabel.Text          = "(no character selected)";
            _detailWarrior.IsChecked       = false;
            _detailMage.IsChecked          = false;
            _detailRogue.IsChecked         = false;
            _detailDifficulty.SelectedIndex = -1;
            _detailStrength.Value          = 50;
            _detailAgility.Value           = 50;
            _detailPermadeath.IsChecked    = false;
            _detailAchievements.Items.Clear();
        }
        finally
        {
            _updatingDetail = false;
        }
    }

    void RefreshAchievements(CharacterData data)
    {
        _detailAchievements.Items.Clear();
        foreach (string achievement in CharacterData.AchievementsFor(data.Class))
            _detailAchievements.Items.Add($"• {achievement}");
    }

    void UpdateButtonStates()
    {
        bool hasSelection = _selected != null;
        _deleteButton.IsEnabled = hasSelection;
        _playButton.IsEnabled   = hasSelection;
    }

    // ── Game loop ─────────────────────────────────────────────────────────────

    protected override void Update(GameTime gameTime)
    {
        GumUI.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.DarkSlateGray);
        GumUI.Draw();
        base.Draw(gameTime);
    }
}
