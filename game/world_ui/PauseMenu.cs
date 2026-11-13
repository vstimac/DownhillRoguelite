using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// Esc (or P, or a controller's Start) pauses the run and shows the one flat screen the game has
/// during a run: Resume, Options (the same tabbed sheet as the main menu: volumes, video, controls
/// reference and the rest) and Quit, styled like the start screen. It exists only while the run is
/// stopped, so the "no flat UI during runs" rule holds.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    private static readonly StringName ActionPause = "pause";

    [Export] public Color DimColor { get; set; } = new(0.02f, 0.015f, 0.01f, 0.55f);
    [Export] public int TitleSize { get; set; } = 64;
    [Export(PropertyHint.MultilineText)] public string TipsText { get; set; } =
        "Death is permanent: every run starts on the summit of a new mountain.\n" +
        "Reach the next station before night. Below each station the paths split: a lookout sign compares them; cairns show the way.\n" +
        "Options → Controls lists every key and button.";

    public bool IsOpen => Visible;
    public OptionsPanel? Options => _options;
    public IReadOnlyList<MenuItem> Items => _items;
    public bool ShowingOptions => _options != null && _options.Visible && _options.IsOpen;

    private readonly List<MenuItem> _items = new();
    private Control? _root;
    private Control? _column;
    private Label? _title;
    private VBoxContainer? _menu;
    private Label? _tips;
    private OptionsPanel? _options;
    private AchievementsPage? _achievements;
    private AudioStreamPlayer? _click;
    private AudioStream? _openSound;
    private AudioStream? _closeSound;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 50;
        Build();
        Visible = false;
        _openSound = LoadSound("res://audio/sfx/kenney_interface_sounds/open_001.ogg");
        _closeSound = LoadSound("res://audio/sfx/kenney_interface_sounds/close_001.ogg");
        _click = new AudioStreamPlayer { VolumeDb = -6.0f };
        AddChild(_click);
        GetViewport().SizeChanged += Relayout;
        Relayout();
        // Check: `-- --return-to-menu-test` goes back to the title screen after a moment.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--return-to-menu-test") >= 0 && !StartScreen.ReturnedFromRun)
        {
            GetTree().CreateTimer(12.0, true).Timeout += ReturnToMenu;
        }
        // Render checks: `-- --options` opens the menu on its Options page after a moment.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--options") >= 0)
        {
            GetTree().CreateTimer(2.0, true).Timeout += () =>
            {
                Open();
                ShowOptions();
            };
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Render captures (`-- --no-pause`) never pause, whatever keys reach their window.
        if (@event.IsActionPressed(ActionPause) && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-pause") < 0)
        {
            GD.Print($"Pause menu toggled by {@event.GetType().Name} {@event.AsText()} (device {@event.Device})");
            Toggle();
            GetViewport().SetInputAsHandled();
            return;
        }
        // B / Esc on the menu itself resumes (the Options sheet handles its own Back first).
        if (Visible && @event.IsActionPressed("ui_cancel"))
        {
            Resume();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Toggle()
    {
        if (Visible)
        {
            Resume();
        }
        else
        {
            Open();
        }
    }

    private static AudioStream? LoadSound(string path)
    {
        if (DisplayServer.GetName() == "headless" || !ResourceLoader.Exists(path))
        {
            return null;
        }
        return ResourceLoader.Load<AudioStream>(path);
    }

    private void Sound(AudioStream? sound)
    {
        if (_click != null && sound != null)
        {
            _click.Stream = sound;
            _click.Play();
        }
    }

    public override void _ExitTree()
    {
        if (_click != null)
        {
            _click.Stop();
            _click.Stream = null;
        }
        _openSound = null;
        _closeSound = null;
        if (IsInstanceValid(GetViewport()))
        {
            GetViewport().SizeChanged -= Relayout;
        }
    }

    public void Open()
    {
        Sound(_openSound);
        Visible = true;
        GetTree().Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        ShowMenu();
        if (_root != null)
        {
            _root.Modulate = new Color(1, 1, 1, 0);
            CreateTween().TweenProperty(_root, "modulate:a", 1.0f, 0.15);
        }
    }

    public void Resume()
    {
        Sound(_closeSound);
        Visible = false;
        // A sheet left open would keep taking Esc during the run.
        _options?.SetOpenNow(false);
        _achievements?.SetOpenNow(false);
        GetViewport().GuiReleaseFocus();
        GetTree().Paused = false;
        if (!GameEvents.IsPacking)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    /// <summary>The menu's first page: the entries, Resume focused.</summary>
    /// <summary>
    /// Saves the run and goes back to the title screen without closing the game: the scene is loaded
    /// afresh (the title screen is part of it), and the run waits under Continue and Saved runs.
    /// </summary>
    public void ReturnToMenu()
    {
        GameEvents.RequestSave();
        StartScreen.ReturnedFromRun = true;
        GD.Print("PauseMenu: back to the main menu (run saved: " + RunSave.Exists + ")");
        GetTree().Paused = false;
        GetTree().ReloadCurrentScene();
    }

    public void ShowMenu()
    {
        if (_options != null)
        {
            _options.SetOpenNow(false);
        }
        _achievements?.SetOpenNow(false);
        SetMenuFocusable(true);
        if (_column != null)
        {
            _column.Visible = true;
        }
        if (_items.Count > 0)
        {
            _items[0].GrabFocus();
        }
    }

    public void ShowOptions()
    {
        if (_options == null)
        {
            return;
        }
        if (_column != null)
        {
            _column.Visible = false;
        }
        SetMenuFocusable(false);
        _options.OpenSheet();
    }

    public void ShowAchievements()
    {
        if (_achievements == null)
        {
            return;
        }
        if (_column != null)
        {
            _column.Visible = false;
        }
        SetMenuFocusable(false);
        _achievements.OpenSheet();
    }

    private void SetMenuFocusable(bool on)
    {
        Control.FocusModeEnum mode = Control.FocusModeEnum.None;
        if (on)
        {
            mode = Control.FocusModeEnum.All;
        }
        foreach (MenuItem item in _items)
        {
            item.FocusMode = mode;
        }
    }

    private void Build()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        ColorRect dim = new() { Color = DimColor, MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        _column = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_column);
        _title = UiStyle.MakeLabel("Paused", TitleSize, true, UiStyle.Paper);
        _title.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        _title.AddThemeConstantOverride("shadow_offset_x", 3);
        _title.AddThemeConstantOverride("shadow_offset_y", 3);
        _column.AddChild(_title);
        _menu = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _menu.AddThemeConstantOverride("separation", 4);
        _column.AddChild(_menu);
        AddItem("Resume", Resume);
        AddItem("Options", ShowOptions);
        AddItem("Achievements", ShowAchievements);
        AddItem("Save and main menu", ReturnToMenu);
        AddItem("Save and quit", () =>
        {
            GameEvents.RequestSave();
            GetTree().Quit();
        });
        _tips = UiStyle.MakeLabel(TipsText, UiStyle.SmallSize, false, UiStyle.PaperDark);
        _tips.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _tips.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        _column.AddChild(_tips);

        _options = new OptionsPanel { Name = "Options", ShowCharacter = true };
        _options.BackPressed += ShowMenu;
        _root.AddChild(_options);
        _options.SetOpenNow(false);
        _achievements = new AchievementsPage { Name = "Achievements" };
        _achievements.BackPressed += ShowMenu;
        _root.AddChild(_achievements);
        _achievements.SetOpenNow(false);
    }

    private void AddItem(string text, System.Action action)
    {
        MenuItem item = new() { Text = text };
        item.Pressed += () => action();
        _menu?.AddChild(item);
        _items.Add(item);
    }

    private void Relayout()
    {
        LayoutFor(GetViewport().GetVisibleRect().Size);
    }

    public void LayoutFor(Vector2 screen)
    {
        if (_column == null || _title == null || _menu == null || _tips == null)
        {
            return;
        }
        float margin = Mathf.Max(28.0f, screen.X * 0.06f);
        float width = Mathf.Clamp(screen.X * 0.36f, 300.0f, 540.0f);
        int titleSize = (int)Mathf.Clamp(screen.Y * 0.09f, 40.0f, TitleSize);
        int itemSize = (int)Mathf.Clamp(screen.Y * 0.05f, 26.0f, 40.0f);
        _column.Position = new Vector2(margin, 0.0f);
        _column.Size = new Vector2(width, screen.Y);
        float y = screen.Y * 0.16f;
        _title.AddThemeFontSizeOverride("font_size", titleSize);
        _title.Position = new Vector2(0.0f, y);
        _title.Size = new Vector2(width, titleSize * 1.3f);
        y += titleSize * 1.3f + screen.Y * 0.04f;
        float itemHeight = itemSize * 1.45f;
        foreach (MenuItem item in _items)
        {
            item.FontSize = itemSize;
            item.CustomMinimumSize = new Vector2(width, itemHeight);
            item.QueueRedraw();
        }
        _menu.Position = new Vector2(0.0f, y);
        _menu.Size = new Vector2(width, (itemHeight + 4.0f) * _items.Count);
        y += _menu.Size.Y + screen.Y * 0.05f;
        Vector2 tips = UiStyle.Wrapped(TipsText, UiStyle.SmallSize, width);
        _tips.Position = new Vector2(4.0f, y);
        _tips.Size = new Vector2(width, tips.Y);
        _options?.FitInto(new Rect2(new Vector2(24.0f, 24.0f), screen - new Vector2(48.0f, 48.0f)));
        _achievements?.FitInto(new Rect2(new Vector2(24.0f, 24.0f), screen - new Vector2(48.0f, 48.0f)));
    }
}
