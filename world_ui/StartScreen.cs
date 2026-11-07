using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// Title card shown when the game launches: the world waits paused and silent until the player
/// presses Start (or Enter / Space), then the sound fades in instead of starting at full volume.
/// The player picks their character here (left/right; saved in GameOptions, CarrierRoster).
/// Skipped in headless runs (tests, smoke) and with `-- --promo` or `-- --skip-start`.
/// </summary>
public partial class StartScreen : CanvasLayer
{
    private static readonly StringName ActionPause = "pause";

    [Export] public string Title { get; set; } = "Downhill Roguelite";
    [Export] public string Subtitle { get; set; } = "Every run begins at the summit.";
    [Export] public Color BackdropColor { get; set; } = new(0.05f, 0.04f, 0.03f, 0.92f);
    [Export] public int TitleSize { get; set; } = 64;
    /// <summary>How long the master volume takes to fade back in after Start, in seconds.</summary>
    [Export] public float FadeInSeconds { get; set; } = 3.0f;
    /// <summary>Master volume while the title card is up, in dB (silent).</summary>
    [Export] public float SilentDb { get; set; } = -80.0f;

    [Export(PropertyHint.File, "*.tres")] public string RosterPath { get; set; } = CarrierRoster.DefaultPath;

    private readonly List<CarrierCharacter> _characters = new();
    private int _character;
    private Label? _characterName;
    private int _masterBus;
    private float _masterDb;
    private bool _started;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 60;
        if (ShouldSkip())
        {
            QueueFree();
            return;
        }
        _masterBus = AudioServer.GetBusIndex("Master");
        _masterDb = AudioServer.GetBusVolumeDb(_masterBus);
        AudioServer.SetBusVolumeDb(_masterBus, SilentDb);
        GetTree().Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Build();
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

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_started)
        {
            return;
        }
        // Keep Esc from opening the pause menu underneath the title card.
        if (@event.IsActionPressed(ActionPause))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is InputEventKey key && key.Pressed && !key.Echo
            && (key.Keycode == Key.Enter || key.Keycode == Key.KpEnter || key.Keycode == Key.Space))
        {
            GetViewport().SetInputAsHandled();
            Start();
            return;
        }
        if (@event is InputEventKey arrow && arrow.Pressed && (arrow.Keycode == Key.Left || arrow.Keycode == Key.Right || arrow.Keycode == Key.A || arrow.Keycode == Key.D))
        {
            GetViewport().SetInputAsHandled();
            int step = 1;
            if (arrow.Keycode == Key.Left || arrow.Keycode == Key.A)
            {
                step = -1;
            }
            StepCharacter(step);
        }
    }

    private void Start()
    {
        if (_started)
        {
            return;
        }
        _started = true;
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        // The tween must outlive this node, so it runs on the tree.
        Tween fade = GetTree().CreateTween();
        fade.SetPauseMode(Tween.TweenPauseMode.Process);
        int bus = _masterBus;
        fade.TweenMethod(Callable.From<float>(db => AudioServer.SetBusVolumeDb(bus, db)),
            SilentDb, _masterDb, FadeInSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        QueueFree();
    }

    private void Build()
    {
        Control root = new() { MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        ColorRect backdrop = new() { Color = BackdropColor, MouseFilter = Control.MouseFilterEnum.Ignore };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(backdrop);

        CenterContainer center = new();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        VBoxContainer column = new() { CustomMinimumSize = new Vector2(420.0f, 0.0f) };
        column.AddThemeConstantOverride("separation", 18);
        center.AddChild(column);

        Label title = new() { Text = Title, HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontOverride("font", UiStyle.Heading);
        title.AddThemeFontSizeOverride("font_size", TitleSize);
        title.AddThemeColorOverride("font_color", UiStyle.Paper);
        column.AddChild(title);

        Label subtitle = new() { Text = Subtitle, HorizontalAlignment = HorizontalAlignment.Center };
        subtitle.AddThemeFontOverride("font", UiStyle.Hand);
        subtitle.AddThemeFontSizeOverride("font_size", UiStyle.TextSize);
        subtitle.AddThemeColorOverride("font_color", UiStyle.PaperDark);
        column.AddChild(subtitle);

        column.AddChild(new Control { CustomMinimumSize = new Vector2(0.0f, 24.0f) });
        AddCharacterPicker(column);

        InkButton start = new()
        {
            Text = "Start",
            Paper = true,
            FontSize = UiStyle.HeadingSize,
            CustomMinimumSize = new Vector2(240.0f, 64.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        start.Pressed += Start;
        column.AddChild(start);

        InkButton quit = new()
        {
            Text = "Quit",
            Paper = true,
            CustomMinimumSize = new Vector2(160.0f, 44.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        quit.Pressed += () => GetTree().Quit();
        column.AddChild(quit);

        Label hint = new() { Text = "Enter / Space to start", HorizontalAlignment = HorizontalAlignment.Center };
        hint.AddThemeFontOverride("font", UiStyle.Hand);
        hint.AddThemeFontSizeOverride("font_size", UiStyle.SmallSize);
        hint.AddThemeColorOverride("font_color", UiStyle.PaperDark);
        column.AddChild(hint);
    }

    /// <summary>"Character: < name >" when more than one character's model is in the game.</summary>
    private void AddCharacterPicker(VBoxContainer column)
    {
        _characters.Clear();
        CarrierRoster? roster = CarrierRoster.Load(RosterPath);
        if (roster != null)
        {
            _characters.AddRange(roster.Available());
        }
        if (_characters.Count < 2)
        {
            return;
        }
        GameOptions.Load();
        _character = 0;
        for (int i = 0; i < _characters.Count; i++)
        {
            if (_characters[i].Id == GameOptions.Character)
            {
                _character = i;
            }
        }
        Label heading = new() { Text = "Character", HorizontalAlignment = HorizontalAlignment.Center };
        heading.AddThemeFontOverride("font", UiStyle.Hand);
        heading.AddThemeFontSizeOverride("font_size", UiStyle.SmallSize);
        heading.AddThemeColorOverride("font_color", UiStyle.PaperDark);
        column.AddChild(heading);

        HBoxContainer row = new() { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 16);
        column.AddChild(row);
        InkButton previous = new() { Text = "<", Paper = true, CustomMinimumSize = new Vector2(48.0f, 44.0f) };
        previous.Pressed += () => StepCharacter(-1);
        row.AddChild(previous);
        _characterName = new Label { HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(240.0f, 0.0f) };
        _characterName.AddThemeFontOverride("font", UiStyle.Heading);
        _characterName.AddThemeFontSizeOverride("font_size", UiStyle.HeadingSize);
        _characterName.AddThemeColorOverride("font_color", UiStyle.Paper);
        row.AddChild(_characterName);
        InkButton next = new() { Text = ">", Paper = true, CustomMinimumSize = new Vector2(48.0f, 44.0f) };
        next.Pressed += () => StepCharacter(1);
        row.AddChild(next);
        ShowCharacter();
    }

    private void StepCharacter(int step)
    {
        if (_characters.Count < 2)
        {
            return;
        }
        _character = (_character + step + _characters.Count) % _characters.Count;
        GameOptions.SetCharacter(_characters[_character].Id);
        ShowCharacter();
    }

    private void ShowCharacter()
    {
        if (_characterName != null && _characters.Count > 0)
        {
            _characterName.Text = _characters[_character].DisplayName;
        }
    }
}
