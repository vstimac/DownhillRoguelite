using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Core.Update;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// The front end: the real mountain behind the menu (MenuCamera drifts past the carrier), the title,
/// and a menu of New Run, Character, Co-op, Options, Achievements, About and Quit. Pages (character, options, about) are
/// parchment sheets that unfold over the scene. The world runs a moment so the carrier and camera
/// settle, then waits paused and silent; New Run fades the menu away, flies the camera into the run's
/// own camera and fades the sound in. Mouse, keyboard and controller all work; the footer shows the
/// keys for whichever was used last. Skipped in headless runs (tests, smoke) and with `-- --promo`
/// or `-- --skip-start`. Render checks: `-- --menu-page=character` (or options, about) opens that page.
/// </summary>
public partial class StartScreen : CanvasLayer
{
    public enum Page
    {
        Main,
        Character,
        Options,
        About,
        Achievements,
        Coop,
        Saves,
        Starting,
    }

    private static readonly StringName ActionPause = "pause";

    [Export] public string Title { get; set; } = "Downhill Roguelite";
    [Export] public string Subtitle { get; set; } = "Every run begins at the summit.";
    /// <summary>The version in the footer; the game's own one (core/GameVersion) gets its release date beside it.</summary>
    [Export] public string VersionText { get; set; } = GameVersion.Name;
    /// <summary>Largest title size in pixels; it shrinks with the window's height.</summary>
    [Export] public int TitleSize { get; set; } = 76;
    /// <summary>The dark wash behind the menu column (left edge; it fades out towards the middle).</summary>
    [Export] public Color ShadeColor { get; set; } = new(0.04f, 0.03f, 0.02f, 0.82f);
    /// <summary>How long the master volume takes to fade back in after New Run, in seconds.</summary>
    [Export] public float FadeInSeconds { get; set; } = 3.0f;
    /// <summary>Seconds the world runs before it pauses, so the carrier lands and the camera settles behind the black.</summary>
    [Export] public float WarmupSeconds { get; set; } = 0.25f;
    /// <summary>Seconds the opening fade from black takes.</summary>
    [Export] public float CurtainSeconds { get; set; } = 1.2f;
    [Export] public float PageSeconds { get; set; } = 0.3f;
    /// <summary>The fog's share while the menu is up (1 = as in the run): less haze gives the mountain depth.</summary>
    [Export] public float MenuFog { get; set; } = 0.3f;

    [Export(PropertyHint.File, "*.tres")] public string RosterPath { get; set; } = CarrierRoster.DefaultPath;

    /// <summary>Show it even where it is normally skipped (tests).</summary>
    public bool ForceShow { get; set; }
    public bool Started { get; private set; }
    public Page Current { get; private set; } = Page.Main;
    /// <summary>The world is paused and the menu is waiting for the player.</summary>
    public bool Waiting => !Started && _warmup <= 0.0f;

    public IReadOnlyList<MenuItem> Items => _items;
    public OptionsPanel? Options => _options;
    public CharacterPage? Characters => _characters;
    public CreditsPage? About => _about;
    public AchievementsPage? Achievements => _achievements;
    public CoopPage? Coop => _coop;
    public MenuCamera? Camera => _camera;
    public Label? TitleLabel => _title;
    public Label? FooterKeys => _keys;
    public Label? FooterHint => _hint;
    public Label? VersionLabel => _version;
    /// <summary>The new-version card (top right), once a newer release was found (core/update/UpdateCheck).</summary>
    public UpdatePrompt? UpdateCard => _prompt;

    private readonly List<MenuItem> _items = new();
    private Control? _root;
    private Control? _column;
    private Label? _title;
    private Label? _subtitle;
    private Label? _records;
    private VBoxContainer? _menu;
    private Label? _hint;
    private Label? _keys;
    private Label? _version;
    private TextureRect? _shade;
    private TextureRect? _floor;
    private ColorRect? _curtain;
    private OptionsPanel? _options;
    private CharacterPage? _characters;
    private CreditsPage? _about;
    private AchievementsPage? _achievements;
    private CoopPage? _coop;
    private bool _characterForCoop;
    private SavesPage? _saves;
    /// <summary>The menu's letters never get smaller than this on short screens.</summary>
    [Export] public int SmallestItemSize { get; set; } = 20;
    private ulong? _chosenSeed;
    /// <summary>
    /// The title screen is up again after "Save and main menu": the run's numbers are still those of
    /// the run just left, so a New Run starts a new mountain and resets them (Continue does that itself).
    /// </summary>
    public static bool ReturnedFromRun { get; set; }
    private MenuCamera? _camera;
    private MenuItem? _characterItem;
    private MenuItem? _lastFocus;
    private UpdatePrompt? _prompt;
    private bool _offered;
    /// <summary>Each entry's rank: 0 the lead (Continue, or New Run), 1 New Run under Continue, 2 the other main entries, 3 the quiet row.</summary>
    private readonly Dictionary<MenuItem, int> _tier = new();
    private HBoxContainer? _secondary;
    private System.Threading.Tasks.Task? _install;
    private float _warmup;
    private bool _padUsed;
    private Vector2 _screen = new(1280, 720);
    private readonly List<CanvasLayer> _hidden = new();
    private readonly List<Tween> _intro = new();
    private bool _linesWereOn;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 60;
        if (!ForceShow && ShouldSkip())
        {
            QueueFree();
            return;
        }
        GameOptions.Load();
        OptionsApplier.SetHush(0.0f);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Build();
        DownhillRoguelite.Environment.DayNightCycle.MenuFogScale = MenuFog;
        Camera3D? game = GetViewport().GetCamera3D();
        if (game != null)
        {
            _camera = new MenuCamera { Name = "MenuCamera", GameCamera = game };
            AddChild(_camera);
            _camera.MakeCurrent();
        }
        _warmup = Mathf.Max(WarmupSeconds, 0.001f);
        HideWorldUi();
        GetViewport().SizeChanged += Relayout;
        Relayout();
        UpdateCheck.Start();
        // Check: `-- --return-to-menu-test` starts a run by itself (the pause menu then returns here, and the run is continued).
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--return-to-menu-test") >= 0)
        {
            GetTree().CreateTimer(3.0, true).Timeout += () =>
            {
                RunSave.UseFiles("user://test_menu_run.json", "user://test_menu_run_pack.json");
                bool again = ReturnedFromRun && RunSave.Exists;
                GD.Print("StartScreen: test run, continuing " + again);
                BeginRun(again);
            };
        }
    }

    public override void _ExitTree()
    {
        DownhillRoguelite.Environment.DayNightCycle.MenuFogScale = 1.0f;
        RestoreWorldUi();
        // A skipped screen never connected.
        if (_root != null && IsInstanceValid(GetViewport()))
        {
            GetViewport().SizeChanged -= Relayout;
        }
    }

    private static bool ShouldSkip()
    {
        if (DisplayServer.GetName() == "headless")
        {
            return true;
        }
        string[] args = OS.GetCmdlineUserArgs();
        return System.Array.IndexOf(args, "--promo") >= 0 || System.Array.IndexOf(args, "--skip-start") >= 0;
    }

    public override void _Process(double delta)
    {
        if (!_offered && !Started && Current == Page.Main && UpdateCheck.Offer is UpdateOffer offer)
        {
            OfferUpdate(offer);
        }
        if (_install != null)
        {
            FollowInstall();
        }
        if (_warmup > 0.0f)
        {
            _warmup -= (float)delta;
            if (_warmup <= 0.0f && !Started)
            {
                GetTree().Paused = true;
                HideWorldUi();
                PlayIntro();
            }
        }
    }

    /// <summary>
    /// The HUD, notes and debug overlays (every other visible canvas layer but the colour-blind
    /// filter) and the in-world debug lines stay hidden while the front end is up; New Run brings
    /// back exactly what was showing.
    /// </summary>
    private void HideWorldUi()
    {
        foreach (Node node in GetTree().Root.FindChildren("*", "CanvasLayer", true, false))
        {
            if (node is CanvasLayer layer && layer != this && layer is not ColourFilterLayer && layer is not LoadingScreen && layer.Visible && !_hidden.Contains(layer))
            {
                layer.Visible = false;
                _hidden.Add(layer);
            }
        }
        if (Debug.DebugDraw3D.Enabled)
        {
            _linesWereOn = true;
            Debug.DebugDraw3D.SetEnabled(false);
        }
    }

    private void RestoreWorldUi()
    {
        foreach (CanvasLayer layer in _hidden)
        {
            if (IsInstanceValid(layer))
            {
                layer.Visible = true;
            }
        }
        _hidden.Clear();
        if (_linesWereOn)
        {
            _linesWereOn = false;
            Debug.DebugDraw3D.SetEnabled(GameOptions.PathLines);
        }
    }

    // ---------------------------------------------------------------- building

    private void Build()
    {
        _root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        _shade = new TextureRect
        {
            Texture = ShadeTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_shade);
        _floor = new TextureRect
        {
            Texture = FloorTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _floor.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_floor);

        _column = new Control { Name = "Column", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_column);
        _title = UiStyle.MakeLabel(Title, TitleSize, true, UiStyle.Paper);
        _title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _title.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        _title.AddThemeConstantOverride("shadow_offset_x", 3);
        _title.AddThemeConstantOverride("shadow_offset_y", 3);
        _title.AddThemeConstantOverride("line_spacing", -10);
        _column.AddChild(_title);
        _subtitle = UiStyle.MakeLabel(Subtitle, UiStyle.TextSize + 2, false, UiStyle.Highlight);
        _subtitle.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        _column.AddChild(_subtitle);
        _records = UiStyle.MakeLabel(RecordsText(), UiStyle.TextSize - 2, false, new Color(UiStyle.Paper, 0.85f));
        _records.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        _column.AddChild(_records);

        _menu = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _menu.AddThemeConstantOverride("separation", 4);
        _column.AddChild(_menu);
        // Three ranks: what most players want (Continue, else New Run) biggest, then New Run, then the rest.
        int newest = RunSave.NewestSlot();
        int newRunTier = 0;
        if (newest > 0)
        {
            RunSave? saved = RunSave.Peek(newest);
            MenuItem resume = AddItem("Continue", "Carry on with the descent you saved last.", ContinueRun, 0);
            if (saved != null)
            {
                resume.Detail = $"Run {saved.RunNumber}  ·  {saved.Depth:N0} m down  ·  day {saved.Day}";
            }
            newRunTier = 1;
        }
        AddItem("New Run", "Start on the summit of a new mountain.", StartRun, newRunTier);
        AddItem("Saved runs", "Pick one of your saved descents, or start a mountain from a seed you choose.", () => ShowPage(Page.Saves), 2);
        AddItem("Co-op (beta)", "Host a descent, or join a friend's: up to four carriers.", () => ShowPage(Page.Coop), 2);
        if (CharacterPage.Roster(RosterPath).Count > 1)
        {
            _characterItem = AddItem("Character", "", () => ShowPage(Page.Character), 2);
        }
        // The rest sit in one quieter row under the main entries.
        _secondary = new HBoxContainer { Name = "Secondary", MouseFilter = Control.MouseFilterEnum.Ignore };
        _secondary.AddThemeConstantOverride("separation", 10);
        _column.AddChild(_secondary);
        AddItem("Options", "Gameplay, video, audio, accessibility and controls.", () => ShowPage(Page.Options), 3);
        AddItem("Achievements", "What you've done on the mountain, ticked off.", () => ShowPage(Page.Achievements), 3);
        AddItem("About", "What this is, and whose work it stands on.", () => ShowPage(Page.About), 3);
        AddItem("Quit", "Back to the desktop.", () => GetTree().Quit(), 3);
        LinkRows();

        // What the focused entry does sits right under the menu, where the eye already is.
        _hint = UiStyle.MakeLabel("", UiStyle.TextSize - 2, false, new Color(UiStyle.Paper, 0.9f));
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hint.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.75f));
        _column.AddChild(_hint);
        UpdateCharacterHint();
        _keys = UiStyle.MakeLabel("", UiStyle.SmallSize, false, UiStyle.PaperDark);
        _keys.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        _root.AddChild(_keys);
        _version = UiStyle.MakeLabel(VersionLine(), UiStyle.SmallSize, false, new Color(UiStyle.Paper, 0.8f));
        _version.HorizontalAlignment = HorizontalAlignment.Right;
        _version.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        _root.AddChild(_version);
        UpdateKeys();

        _options = new OptionsPanel { Name = "Options", ShowCharacter = false };
        _options.BackPressed += () => ClosePage(_options);
        _root.AddChild(_options);
        _options.SetOpenNow(false);
        _characters = new CharacterPage { Name = "Characters", RosterPath = RosterPath };
        _characters.BackPressed += () =>
        {
            ClosePage(_characters);
            // Opened from the Co-op page: back there, with the new look.
            if (_characterForCoop)
            {
                _characterForCoop = false;
                ShowPage(Page.Coop);
            }
        };
        _characters.PreviewTurnRequested += degrees => _camera?.TurnPortrait(degrees);
        _root.AddChild(_characters);
        _characters.SetOpenNow(false);
        _about = new CreditsPage { Name = "About", Version = VersionLine() };
        _about.BackPressed += () => ClosePage(_about);
        _root.AddChild(_about);
        _about.SetOpenNow(false);
        _achievements = new AchievementsPage { Name = "Achievements" };
        _achievements.BackPressed += () => ClosePage(_achievements);
        _root.AddChild(_achievements);
        _achievements.SetOpenNow(false);
        _coop = new CoopPage { Name = "Coop" };
        _coop.BackPressed += () =>
        {
            // Backing out while still connecting gives up the attempt.
            Network.NetSession? session = Network.NetSession.Instance;
            if (session != null && session.Active && !session.Joined)
            {
                session.Leave();
            }
            ClosePage(_coop);
        };
        _coop.CharacterRequested += () =>
        {
            _coop.Close();
            _characterForCoop = true;
            Current = Page.Main;
            ShowPage(Page.Character);
        };
        _coop.RunReady += () =>
        {
            ClosePage(_coop);
            StartRun();
        };
        _root.AddChild(_coop);
        _coop.SetOpenNow(false);
        _saves = new SavesPage { Name = "Saves" };
        _saves.BackPressed += () => ClosePage(_saves);
        _saves.SlotChosen += PlaySlot;
        _saves.SeedChosen += StartRunWithSeed;
        _root.AddChild(_saves);
        _saves.SetOpenNow(false);

        _curtain = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        _curtain.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_curtain);
        _column.Modulate = new Color(1, 1, 1, 0);
    }

    /// <summary>"Beta 3.0  ·  14 Nov 2026" for this game; test scenes keep their own text.</summary>
    private string VersionLine()
    {
        if (VersionText == GameVersion.Name)
        {
            return GameVersion.Label;
        }
        return VersionText;
    }

    private MenuItem AddItem(string text, string hint, System.Action action, int tier)
    {
        bool secondary = tier >= 3;
        MenuItem item = new() { Text = text, Hint = hint };
        _tier[item] = tier;
        if (secondary)
        {
            item.Slide = 6.0f;
        }
        item.Pressed += () =>
        {
            if (!Started && Current == Page.Main)
            {
                action();
            }
        };
        item.FocusEntered += () =>
        {
            _lastFocus = item;
            if (_hint != null)
            {
                _hint.Text = item.Hint;
            }
        };
        if (secondary && _secondary != null)
        {
            _secondary.AddChild(item);
        }
        else
        {
            _menu?.AddChild(item);
        }
        _items.Add(item);
        return item;
    }

    /// <summary>Down from the last main entry reaches the quiet row, and up from the row comes back.</summary>
    private void LinkRows()
    {
        if (_menu == null || _secondary == null || _menu.GetChildCount() == 0 || _secondary.GetChildCount() == 0)
        {
            return;
        }
        MenuItem? last = _menu.GetChild(_menu.GetChildCount() - 1) as MenuItem;
        MenuItem? first = _secondary.GetChild(0) as MenuItem;
        if (last == null || first == null)
        {
            return;
        }
        last.FocusNeighborBottom = last.GetPathTo(first);
        foreach (Node child in _secondary.GetChildren())
        {
            if (child is MenuItem quiet)
            {
                quiet.FocusNeighborTop = quiet.GetPathTo(last);
            }
        }
    }

    /// <summary>
    /// Shows the new-version card for <paramref name="offer"/> (the background check calls it once; tests too).
    /// The card is the only place that tells: no menu entry, no footer note.
    /// </summary>
    public void OfferUpdate(UpdateOffer offer)
    {
        if (_offered || _root == null || Started)
        {
            return;
        }
        _offered = true;
        _prompt = new UpdatePrompt { Name = "UpdatePrompt" };
        GetViewport().GuiReleaseFocus();
        _root.AddChild(_prompt);
        // Pages and the curtain stay above the card.
        if (_column != null)
        {
            _root.MoveChild(_prompt, _column.GetIndex() + 1);
        }
        _prompt.Fill(offer);
        _prompt.UpdateChosen += () => ApplyUpdate(offer);
        _prompt.Folded += () =>
        {
            if (Current == Page.Main)
            {
                (_lastFocus ?? FirstItem())?.GrabFocus();
            }
        };
        LinkCard();
    }

    /// <summary>Up from the first entry reaches the card (or its tab), and down from them comes back.</summary>
    private void LinkCard()
    {
        MenuItem? first = FirstItem();
        UpdatePrompt? prompt = _prompt;
        if (prompt == null || first == null || prompt.UpdateButton == null || prompt.LaterButton == null || prompt.Tab == null)
        {
            return;
        }
        InkButton update = prompt.UpdateButton;
        InkButton later = prompt.LaterButton;
        InkButton tab = prompt.Tab;
        foreach (Control control in new Control[] { update, later, tab })
        {
            control.FocusNeighborBottom = control.GetPathTo(first);
        }
        tab.FocusNeighborLeft = tab.GetPathTo(first);
        update.FocusNeighborTop = update.GetPathTo(update);
        later.FocusNeighborTop = later.GetPathTo(later);
        first.FocusNeighborTop = first.GetPathTo(update);
        prompt.Unfolded += () => first.FocusNeighborTop = first.GetPathTo(update);
        prompt.Folded += () => first.FocusNeighborTop = first.GetPathTo(tab);
    }

    private void ApplyUpdate(UpdateOffer offer)
    {
        if (_install != null)
        {
            return;
        }
        if (UpdateCheck.InstallsInGame)
        {
            // Linux: the game downloads and installs the update itself, then starts again.
            string dir = UpdateCheck.GameDir;
            UpdatePlatform platform = UpdateCheck.Platform;
            _install = System.Threading.Tasks.Task.Run(() => UpdateInstaller.InstallAsync(offer, dir, platform));
            UpdateHint("Updating...");
            return;
        }
        try
        {
            if (UpdateCheck.Launch(offer))
            {
                GetTree().Quit();
                return;
            }
        }
        catch (System.Exception error)
        {
            GD.PushWarning("StartScreen: could not start the updater: " + error.Message);
        }
        UpdateHint("Couldn't start the updater. Run \"Update game.bat\" in the game folder, or download it from GitHub.");
    }

    /// <summary>Shows how an in-game install is going; when it is done the game starts again.</summary>
    private void FollowInstall()
    {
        if (_install == null)
        {
            return;
        }
        if (!_install.IsCompleted)
        {
            string stage = UpdateInstaller.Stage;
            if (stage == "Downloading")
            {
                stage += $" {UpdateInstaller.Fraction * 100.0f:0}%";
            }
            UpdateHint(stage + "... The game starts again when it is done.");
            return;
        }
        System.Threading.Tasks.Task finished = _install;
        _install = null;
        if (finished.IsFaulted)
        {
            string why = finished.Exception?.GetBaseException().Message ?? "unknown error";
            GD.PushWarning("StartScreen: the update failed: " + why);
            UpdateHint($"The update didn't work: {why}. You can try again, or download the game from GitHub.");
            return;
        }
        if (UpdateCheck.Restart())
        {
            GetTree().Quit();
            return;
        }
        UpdateHint("Updated. Close the game and start it again.");
    }

    /// <summary>How the update is going, on the card (opened again if it was folded).</summary>
    private void UpdateHint(string text)
    {
        if (_prompt == null)
        {
            return;
        }
        _prompt.Unfold();
        _prompt.SetMessage(text);
    }

    /// <summary>The menu entry with this text, or null.</summary>
    public MenuItem? Item(string text)
    {
        foreach (MenuItem item in _items)
        {
            if (item.Text == text)
            {
                return item;
            }
        }
        return null;
    }

    /// <summary>A soft dark band along the bottom edge, under the footer, so it reads over bright snow.</summary>
    private Texture2D FloorTexture()
    {
        Gradient gradient = new();
        gradient.SetColor(0, new Color(ShadeColor, 0.0f));
        gradient.SetColor(1, new Color(ShadeColor, ShadeColor.A * 0.6f));
        gradient.AddPoint(0.8f, new Color(ShadeColor, 0.0f));
        return new GradientTexture2D { Gradient = gradient, Width = 4, Height = 128, Fill = GradientTexture2D.FillEnum.Linear, FillFrom = Vector2.Zero, FillTo = new Vector2(0.0f, 1.0f) };
    }

    /// <summary>A left-to-right wash: dark under the menu, clear over the scene.</summary>
    private Texture2D ShadeTexture()
    {
        Gradient gradient = new();
        gradient.SetColor(0, ShadeColor);
        gradient.SetColor(1, new Color(ShadeColor, 0.0f));
        gradient.AddPoint(0.42f, new Color(ShadeColor, ShadeColor.A * 0.55f));
        return new GradientTexture2D { Gradient = gradient, Width = 256, Height = 4, Fill = GradientTexture2D.FillEnum.Linear, FillFrom = Vector2.Zero, FillTo = new Vector2(0.85f, 0.0f) };
    }

    private string RecordsText()
    {
        RunState? run = GetNodeOrNull<RunState>("/root/RunState");
        if (run == null || run.Records.Runs <= 0)
        {
            return "";
        }
        RunRecords records = run.Records;
        string text = $"{records.Runs} descents so far  ·  deepest {records.BestDepth:N0} m";
        if (records.BestStation > 0)
        {
            text += $"  ·  reached station {records.BestStation}";
        }
        return text;
    }

    private void UpdateCharacterHint()
    {
        if (_characterItem == null)
        {
            return;
        }
        CarrierRoster? roster = CarrierRoster.Load(RosterPath);
        string name = "";
        if (roster?.Pick(GameOptions.Character) is CarrierCharacter character)
        {
            name = character.DisplayName;
        }
        _characterItem.Hint = $"Playing as {name}. Choose who carries the load.";
        if (_hint != null && _lastFocus == _characterItem)
        {
            _hint.Text = _characterItem.Hint;
        }
    }

    // ---------------------------------------------------------------- layout

    private void Relayout()
    {
        LayoutFor(GetViewport().GetVisibleRect().Size);
    }

    /// <summary>The letter size of an entry of <paramref name="tier"/> when a main entry is <paramref name="itemSize"/>.</summary>
    private int SizeFor(int tier, float itemSize)
    {
        if (tier == 0)
        {
            return (int)(itemSize * 1.25f);
        }
        if (tier == 1)
        {
            return (int)itemSize;
        }
        if (tier == 2)
        {
            return (int)Mathf.Max(SmallestItemSize, itemSize * 0.82f);
        }
        return (int)Mathf.Max(SmallestItemSize, itemSize * 0.62f);
    }

    private int TierOf(MenuItem item)
    {
        if (_tier.TryGetValue(item, out int tier))
        {
            return tier;
        }
        return 2;
    }

    /// <summary>The main entries' height for <paramref name="itemSize"/> (the quiet row not counted).</summary>
    private float MainHeight(float itemSize, float spacing)
    {
        float total = 0.0f;
        foreach (MenuItem item in _items)
        {
            int tier = TierOf(item);
            if (tier < 3)
            {
                item.FontSize = SizeFor(tier, itemSize);
                total += item.NeededHeight(SizeFor(tier, itemSize) * spacing) + 4.0f;
            }
        }
        return total;
    }

    /// <summary>Places everything for a screen of <paramref name="screen"/> (tests call it with set sizes).</summary>
    public void LayoutFor(Vector2 screen)
    {
        if (_column == null || _title == null || _subtitle == null || _records == null || _menu == null || _hint == null || _keys == null || _version == null)
        {
            return;
        }
        _screen = screen;
        float margin = Mathf.Max(28.0f, screen.X * 0.06f);
        float width = Mathf.Clamp(screen.X * 0.34f, 300.0f, 520.0f);
        int titleSize = (int)Mathf.Clamp(screen.Y * 0.085f, 40.0f, TitleSize);
        float itemSize = Mathf.Clamp(screen.Y * 0.044f, 24.0f, 38.0f);

        _column.Position = new Vector2(margin, 0.0f);
        _column.Size = new Vector2(width, screen.Y);
        float y = screen.Y * 0.09f;
        _title.AddThemeFontSizeOverride("font_size", titleSize);
        Vector2 titleSize2 = UiStyle.Wrapped(Title, titleSize, width, true);
        _title.Position = new Vector2(0.0f, y);
        _title.Size = new Vector2(width, titleSize2.Y);
        y += titleSize2.Y + 4.0f;
        _subtitle.Position = new Vector2(4.0f, y);
        _subtitle.Size = new Vector2(width, UiStyle.TextSize * 1.5f);
        y += UiStyle.TextSize * 1.5f;
        _records.Position = new Vector2(4.0f, y);
        _records.Size = new Vector2(width, UiStyle.TextSize * 1.4f);
        if (_records.Text.Length > 0)
        {
            y += UiStyle.TextSize * 1.4f;
        }
        y += screen.Y * 0.05f;

        // Footer: the keys on the left, the version and its date on the right, one line.
        float footerHeight = UiStyle.SmallSize * 1.5f;
        float footer = screen.Y - Mathf.Max(18.0f, screen.Y * 0.035f) - footerHeight;
        float hintHeight = UiStyle.TextSize * 1.35f * 2.0f;
        float quietGap = screen.Y * 0.025f;
        int quietSize = SizeFor(3, itemSize);
        float quietHeight = quietSize * 1.45f;
        // Short screens: the entries close up, then shrink, to clear the quiet row, the hint and the footer.
        float room = footer - y - quietGap - quietHeight - 8.0f - hintHeight - 8.0f;
        float spacing = 1.45f;
        if (MainHeight(itemSize, spacing) > room)
        {
            spacing = 1.2f;
            float fit = room / Mathf.Max(MainHeight(itemSize, spacing), 1.0f);
            if (fit < 1.0f)
            {
                itemSize = Mathf.Max(SmallestItemSize, itemSize * fit);
                quietSize = SizeFor(3, itemSize);
                quietHeight = quietSize * 1.25f;
            }
        }
        float mainHeight = MainHeight(itemSize, spacing);
        foreach (MenuItem item in _items)
        {
            int tier = TierOf(item);
            if (tier >= 3)
            {
                item.FontSize = quietSize;
                item.CustomMinimumSize = new Vector2(item.TextWidth(), quietHeight);
            }
            else
            {
                item.FontSize = SizeFor(tier, itemSize);
                item.CustomMinimumSize = new Vector2(width, item.NeededHeight(item.FontSize * spacing));
            }
            item.Refresh();
        }
        _menu.Position = new Vector2(0.0f, y);
        _menu.Size = new Vector2(width, mainHeight);
        y += mainHeight + quietGap;
        if (_secondary != null)
        {
            _secondary.Position = new Vector2(0.0f, y);
            _secondary.Size = new Vector2(Mathf.Max(width, screen.X - margin * 2.0f), quietHeight);
            y += quietHeight + 8.0f;
        }
        _hint.Position = new Vector2(4.0f, y);
        _hint.Size = new Vector2(width, hintHeight);

        _keys.Position = new Vector2(margin, footer);
        _keys.Size = new Vector2(screen.X * 0.55f, footerHeight);
        float versionWidth = Mathf.Min(320.0f, screen.X * 0.35f);
        _version.Size = new Vector2(versionWidth, footerHeight);
        _version.Position = new Vector2(screen.X - margin - versionWidth, footer);

        Rect2 full = new(new Vector2(24.0f, 24.0f), screen - new Vector2(48.0f, 48.0f));
        _options?.FitInto(full);
        _about?.FitInto(full);
        _achievements?.FitInto(full);
        _coop?.FitInto(full);
        _saves?.FitInto(full);
        _characters?.FitInto(new Rect2(screen.X * 0.5f, margin * 0.6f, screen.X * 0.5f - margin * 0.6f, screen.Y - margin * 1.2f));
    }

    /// <summary>Where the menu column's bottom edge is (the hint under the entries), to check it clears the footer.</summary>
    public float MenuBottom()
    {
        if (_menu == null || _column == null)
        {
            return 0.0f;
        }
        if (_hint != null)
        {
            return _column.Position.Y + _hint.Position.Y + _hint.Size.Y;
        }
        if (_secondary != null && _secondary.GetChildCount() > 0)
        {
            return _column.Position.Y + _secondary.Position.Y + _secondary.Size.Y;
        }
        return _column.Position.Y + _menu.Position.Y + _menu.Size.Y;
    }

    // ---------------------------------------------------------------- flow

    private void PlayIntro()
    {
        if (_curtain != null)
        {
            Tween curtain = CreateTween();
            curtain.TweenProperty(_curtain, "modulate:a", 0.0f, CurtainSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            curtain.TweenCallback(Callable.From(() => _curtain.Visible = false));
        }
        if (_column != null)
        {
            Tween column = CreateTween().SetParallel();
            _intro.Add(column);
            column.TweenProperty(_column, "modulate:a", 1.0f, 0.7).SetDelay(CurtainSeconds * 0.5);
        }
        // Entries slide in one after another.
        for (int i = 0; i < _items.Count; i++)
        {
            MenuItem item = _items[i];
            item.Modulate = new Color(1, 1, 1, 0);
            Tween slide = CreateTween();
            _intro.Add(slide);
            slide.TweenProperty(item, "modulate:a", 1.0f, 0.35).SetDelay(CurtainSeconds * 0.6 + 0.07 * i);
        }
        // A card that came up during the fade keeps its focus.
        if (_prompt != null && _prompt.Open && _prompt.UpdateButton != null)
        {
            _prompt.UpdateButton.GrabFocus();
        }
        else if (_items.Count > 0)
        {
            _items[0].GrabFocus();
        }
        OpenPageFromCommandLine();
    }

    private void OpenPageFromCommandLine()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--menu-page=character" && _characterItem != null)
            {
                ShowPage(Page.Character);
            }
            else if (arg == "--menu-page=options")
            {
                ShowPage(Page.Options);
            }
            else if (arg == "--menu-page=about")
            {
                ShowPage(Page.About);
            }
            else if (arg == "--menu-page=achievements")
            {
                ShowPage(Page.Achievements);
            }
            else if (arg == "--menu-page=coop")
            {
                ShowPage(Page.Coop);
            }
        }
    }

    /// <summary>Opens a page over the scene (the menu column steps aside), or returns to the menu.</summary>
    public void ShowPage(Page page)
    {
        if (Started)
        {
            return;
        }
        Current = page;
        if (_column == null)
        {
            return;
        }
        bool main = page == Page.Main;
        // Opening a page during the intro: the intro's fade-ins stop, so they can't bring the column back.
        foreach (Tween intro in _intro)
        {
            intro.Kill();
        }
        _intro.Clear();
        foreach (MenuItem item in _items)
        {
            item.Modulate = Colors.White;
        }
        Tween tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        float alpha = 0.0f;
        float x = Mathf.Max(28.0f, _screen.X * 0.06f) - 40.0f;
        if (main)
        {
            alpha = 1.0f;
            x += 40.0f;
        }
        tween.TweenProperty(_column, "modulate:a", alpha, PageSeconds);
        tween.TweenProperty(_column, "position:x", x, PageSeconds);
        if (_hint != null)
        {
            tween.TweenProperty(_hint, "modulate:a", alpha, PageSeconds);
        }
        // While a page is open, keyboard and controller focus stays on it.
        Control.FocusModeEnum focus = Control.FocusModeEnum.None;
        if (main)
        {
            focus = Control.FocusModeEnum.All;
        }
        foreach (MenuItem item in _items)
        {
            item.FocusMode = focus;
        }
        // The card belongs to the menu: it steps aside with it while a page is open.
        if (_prompt != null)
        {
            _prompt.Visible = main;
        }
        MenuCamera.Shot shot = MenuCamera.Shot.Vista;
        if (page == Page.Character)
        {
            shot = MenuCamera.Shot.Portrait;
        }
        _camera?.GoTo(shot);
        if (page == Page.Options)
        {
            _options?.OpenSheet();
        }
        else if (page == Page.Character)
        {
            _characters?.OpenSheet();
        }
        else if (page == Page.About)
        {
            _about?.OpenSheet();
        }
        else if (page == Page.Achievements)
        {
            _achievements?.OpenSheet();
        }
        else if (page == Page.Coop)
        {
            _coop?.OpenSheet();
        }
        else if (page == Page.Saves)
        {
            _saves?.OpenSheet();
        }
        else
        {
            UpdateCharacterHint();
            (_lastFocus ?? FirstItem())?.GrabFocus();
        }
    }

    private MenuItem? FirstItem()
    {
        if (_items.Count > 0)
        {
            return _items[0];
        }
        return null;
    }

    private void ClosePage(ParchmentPanel page)
    {
        if (Current == Page.Main || Started)
        {
            return;
        }
        page.Close();
        ShowPage(Page.Main);
    }

    /// <summary>New Run: the menu fades, the camera flies into the run's camera, the world wakes and the sound fades in.</summary>
    public void StartRun()
    {
        // A new run takes an empty slot; with all of them used, the one saved longest ago.
        RunSave.UseSlot(RunSave.SlotForNewRun());
        BeginRun(false);
    }

    /// <summary>Continue: as New Run, but RunKeeper puts the saved run back (the one saved last).</summary>
    public void ContinueRun()
    {
        int newest = RunSave.NewestSlot();
        if (newest > 0)
        {
            RunSave.UseSlot(newest);
        }
        BeginRun(true);
    }

    /// <summary>Saved runs: carry on the run in that slot, or start a new one there when it is empty.</summary>
    public void PlaySlot(int slot)
    {
        _saves?.Close();
        Current = Page.Main;
        RunSave.UseSlot(slot);
        BeginRun(RunSave.Exists);
    }

    /// <summary>Saved runs: a new run on the mountain this seed makes (the same seed, the same mountain).</summary>
    public void StartRunWithSeed(ulong seed)
    {
        _saves?.Close();
        Current = Page.Main;
        _chosenSeed = seed;
        StartRun();
    }

    private void BeginRun(bool continuing)
    {
        if (Started)
        {
            return;
        }
        Started = true;
        // The fog comes back as the camera flies down into the run.
        Tween fog = GetTree().CreateTween();
        fog.SetPauseMode(Tween.TweenPauseMode.Process);
        fog.TweenMethod(Callable.From<float>(scale => DownhillRoguelite.Environment.DayNightCycle.MenuFogScale = scale), MenuFog, 1.0f, 1.5);
        GetNodeOrNull<RunState>("/root/RunState")?.BeginCharacterRun();
        GameEvents.PublishRunBegun(continuing);
        bool sharedRun = Network.NetSession.Instance?.Joined == true;
        if (!sharedRun && !continuing && _chosenSeed != null)
        {
            GetNodeOrNull<RunState>("/root/RunState")?.StartNewRun(_chosenSeed.Value);
        }
        else if (!sharedRun && !continuing && ReturnedFromRun)
        {
            GetNodeOrNull<RunState>("/root/RunState")?.StartNewRun();
        }
        ReturnedFromRun = false;
        _chosenSeed = null;
        Current = Page.Starting;
        _warmup = 0.0f;
        GetViewport().GuiReleaseFocus();
        if (_root != null)
        {
            _root.MouseFilter = Control.MouseFilterEnum.Ignore;
        }
        GetTree().Paused = false;
        Tween fade = CreateTween().SetParallel();
        if (_column != null)
        {
            fade.TweenProperty(_column, "modulate:a", 0.0f, 0.45);
        }
        foreach (Control? part in new Control?[] { _hint, _keys, _version, _shade, _floor, _prompt })
        {
            if (part != null)
            {
                fade.TweenProperty(part, "modulate:a", 0.0f, 0.9);
            }
        }
        if (_curtain != null)
        {
            _curtain.Visible = false;
        }
        // The sound's fade must outlive this node, so it runs on the tree.
        Tween sound = GetTree().CreateTween();
        sound.SetPauseMode(Tween.TweenPauseMode.Process);
        sound.TweenMethod(Callable.From<float>(OptionsApplier.SetHush), OptionsApplier.Hush, 1.0f, FadeInSeconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        if (_camera != null && _camera.GameCamera != null && IsInstanceValid(_camera.GameCamera))
        {
            _camera.Arrived += Finish;
            _camera.GoTo(MenuCamera.Shot.Game);
        }
        else
        {
            GetTree().CreateTimer(0.6, true).Timeout += Finish;
        }
    }

    private void Finish()
    {
        if (!IsInsideTree())
        {
            return;
        }
        if (!GameEvents.IsPacking && !GameEvents.IsScreenOpen)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        RestoreWorldUi();
        QueueFree();
    }

    // ---------------------------------------------------------------- input

    public override void _Input(InputEvent @event)
    {
        bool pad = _padUsed;
        if (@event is InputEventJoypadButton || (@event is InputEventJoypadMotion motion && Mathf.Abs(motion.AxisValue) > 0.5f))
        {
            pad = true;
        }
        else if (@event is InputEventKey || @event is InputEventMouseButton)
        {
            pad = false;
        }
        if (pad != _padUsed)
        {
            _padUsed = pad;
            UpdateKeys();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Started)
        {
            return;
        }
        // Keep Esc / Start from opening the pause menu underneath.
        if (@event.IsActionPressed(ActionPause) || @event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        // Nothing focused (the mouse wandered off): the first key or button press focuses the menu again.
        if (Current == Page.Main && GetViewport().GuiGetFocusOwner() == null
            && (@event.IsActionPressed("ui_up") || @event.IsActionPressed("ui_down") || @event.IsActionPressed("ui_accept")))
        {
            GetViewport().SetInputAsHandled();
            (_lastFocus ?? FirstItem())?.GrabFocus();
        }
    }

    /// <summary>The footer's key hints, for the keyboard or a controller (whichever was used last).</summary>
    private void UpdateKeys()
    {
        if (_keys == null)
        {
            return;
        }
        if (_padUsed)
        {
            _keys.Text = "D-pad / stick  choose     A  select     B  back     LB / RB  tabs";
        }
        else
        {
            _keys.Text = "↑ ↓  choose     Enter  select     Esc  back     Q / E  tabs";
        }
    }
}
