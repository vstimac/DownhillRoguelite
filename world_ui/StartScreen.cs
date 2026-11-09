using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// The front end: the real mountain behind the menu (MenuCamera drifts past the carrier), the title,
/// and a menu of New Run, Character, Options, About and Quit. Pages (character, options, about) are
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
        Starting,
    }

    private static readonly StringName ActionPause = "pause";

    [Export] public string Title { get; set; } = "Downhill Roguelite";
    [Export] public string Subtitle { get; set; } = "Every run begins at the summit.";
    [Export] public string VersionText { get; set; } = "Playtest 1";
    /// <summary>Largest title size in pixels; it shrinks with the window's height.</summary>
    [Export] public int TitleSize { get; set; } = 92;
    /// <summary>The dark wash behind the menu column (left edge; it fades out towards the middle).</summary>
    [Export] public Color ShadeColor { get; set; } = new(0.04f, 0.03f, 0.02f, 0.82f);
    /// <summary>How long the master volume takes to fade back in after New Run, in seconds.</summary>
    [Export] public float FadeInSeconds { get; set; } = 3.0f;
    /// <summary>Seconds the world runs before it pauses, so the carrier lands and the camera settles behind the black.</summary>
    [Export] public float WarmupSeconds { get; set; } = 0.25f;
    /// <summary>Seconds the opening fade from black takes.</summary>
    [Export] public float CurtainSeconds { get; set; } = 1.2f;
    [Export] public float PageSeconds { get; set; } = 0.3f;

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
    public MenuCamera? Camera => _camera;
    public Label? TitleLabel => _title;
    public Label? FooterKeys => _keys;
    public Label? FooterHint => _hint;
    public Label? VersionLabel => _version;

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
    private ColorRect? _curtain;
    private OptionsPanel? _options;
    private CharacterPage? _characters;
    private CreditsPage? _about;
    private MenuCamera? _camera;
    private MenuItem? _characterItem;
    private MenuItem? _lastFocus;
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
    }

    public override void _ExitTree()
    {
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
            if (node is CanvasLayer layer && layer != this && layer is not ColourFilterLayer && layer.Visible && !_hidden.Contains(layer))
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
        _records = UiStyle.MakeLabel(RecordsText(), UiStyle.SmallSize, false, UiStyle.PaperDark);
        _column.AddChild(_records);

        _menu = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _menu.AddThemeConstantOverride("separation", 4);
        _column.AddChild(_menu);
        AddItem("New Run", "Start on the summit of a new mountain.", StartRun);
        if (CharacterPage.Roster(RosterPath).Count > 1)
        {
            _characterItem = AddItem("Character", "", () => ShowPage(Page.Character));
        }
        AddItem("Options", "Gameplay, video, audio, accessibility and controls.", () => ShowPage(Page.Options));
        AddItem("About", "What this is, and whose work it stands on.", () => ShowPage(Page.About));
        AddItem("Quit", "Back to the desktop.", () => GetTree().Quit());
        UpdateCharacterHint();

        _hint = UiStyle.MakeLabel("", UiStyle.TextSize - 2, false, UiStyle.Paper);
        _hint.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        _root.AddChild(_hint);
        _keys = UiStyle.MakeLabel("", UiStyle.SmallSize, false, UiStyle.PaperDark);
        _keys.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        _root.AddChild(_keys);
        _version = UiStyle.MakeLabel(VersionText, UiStyle.SmallSize, false, new Color(UiStyle.Paper, 0.7f));
        _version.HorizontalAlignment = HorizontalAlignment.Right;
        _version.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        _root.AddChild(_version);
        UpdateKeys();

        _options = new OptionsPanel { Name = "Options", ShowCharacter = false };
        _options.BackPressed += () => ClosePage(_options);
        _root.AddChild(_options);
        _options.SetOpenNow(false);
        _characters = new CharacterPage { Name = "Characters", RosterPath = RosterPath };
        _characters.BackPressed += () => ClosePage(_characters);
        _characters.PreviewTurnRequested += degrees => _camera?.TurnPortrait(degrees);
        _root.AddChild(_characters);
        _characters.SetOpenNow(false);
        _about = new CreditsPage { Name = "About", Version = VersionText };
        _about.BackPressed += () => ClosePage(_about);
        _root.AddChild(_about);
        _about.SetOpenNow(false);

        _curtain = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        _curtain.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_curtain);
        _column.Modulate = new Color(1, 1, 1, 0);
    }

    private MenuItem AddItem(string text, string hint, System.Action action)
    {
        MenuItem item = new() { Text = text, Hint = hint };
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
        _menu?.AddChild(item);
        _items.Add(item);
        return item;
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

    /// <summary>A left-to-right wash: dark under the menu, clear over the scene; darker along the bottom for the footer.</summary>
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

    /// <summary>Places everything for a screen of <paramref name="screen"/> (tests call it with set sizes).</summary>
    public void LayoutFor(Vector2 screen)
    {
        if (_column == null || _title == null || _subtitle == null || _records == null || _menu == null || _hint == null || _keys == null || _version == null)
        {
            return;
        }
        _screen = screen;
        float margin = Mathf.Max(28.0f, screen.X * 0.06f);
        float width = Mathf.Clamp(screen.X * 0.36f, 300.0f, 540.0f);
        int titleSize = (int)Mathf.Clamp(screen.Y * 0.1f, 44.0f, TitleSize);
        int itemSize = (int)Mathf.Clamp(screen.Y * 0.046f, 26.0f, 40.0f);

        _column.Position = new Vector2(margin, 0.0f);
        _column.Size = new Vector2(width, screen.Y);
        float y = screen.Y * 0.1f;
        _title.AddThemeFontSizeOverride("font_size", titleSize);
        Vector2 titleSize2 = UiStyle.Wrapped(Title, titleSize, width, true);
        _title.Position = new Vector2(0.0f, y);
        _title.Size = new Vector2(width, titleSize2.Y);
        y += titleSize2.Y + 6.0f;
        _subtitle.Position = new Vector2(4.0f, y);
        _subtitle.Size = new Vector2(width, UiStyle.TextSize * 1.5f);
        y += UiStyle.TextSize * 1.5f;
        _records.Position = new Vector2(4.0f, y);
        _records.Size = new Vector2(width, UiStyle.SmallSize * 1.5f);
        if (_records.Text.Length > 0)
        {
            y += UiStyle.SmallSize * 1.5f;
        }
        y += screen.Y * 0.045f;

        float itemHeight = itemSize * 1.45f;
        foreach (MenuItem item in _items)
        {
            item.FontSize = itemSize;
            item.CustomMinimumSize = new Vector2(width, itemHeight);
            item.QueueRedraw();
        }
        _menu.Position = new Vector2(0.0f, y);
        _menu.Size = new Vector2(width, (itemHeight + 4.0f) * _items.Count);

        float footer = screen.Y - Mathf.Max(20.0f, screen.Y * 0.04f);
        _keys.Position = new Vector2(margin, footer - UiStyle.SmallSize * 1.4f);
        _keys.Size = new Vector2(screen.X * 0.6f, UiStyle.SmallSize * 1.4f);
        _hint.Position = new Vector2(margin, _keys.Position.Y - UiStyle.TextSize * 1.6f);
        _hint.Size = new Vector2(Mathf.Max(width, screen.X * 0.5f), UiStyle.TextSize * 1.5f);
        _version.Size = new Vector2(240.0f, UiStyle.SmallSize * 1.4f);
        _version.Position = new Vector2(screen.X - margin - 240.0f, _keys.Position.Y);

        Rect2 full = new(new Vector2(24.0f, 24.0f), screen - new Vector2(48.0f, 48.0f));
        _options?.FitInto(full);
        _about?.FitInto(full);
        _characters?.FitInto(new Rect2(screen.X * 0.5f, margin * 0.6f, screen.X * 0.5f - margin * 0.6f, screen.Y - margin * 1.2f));
    }

    /// <summary>Where the menu column's bottom edge is (the last entry's), to check it clears the footer.</summary>
    public float MenuBottom()
    {
        if (_menu == null || _column == null)
        {
            return 0.0f;
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
        if (_items.Count > 0)
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
        if (Started)
        {
            return;
        }
        Started = true;
        GetNodeOrNull<RunState>("/root/RunState")?.BeginCharacterRun();
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
        foreach (Control? part in new Control?[] { _hint, _keys, _version, _shade })
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
