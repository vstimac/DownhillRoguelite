using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// The start screen's character page: a parchment sheet beside the carrier (the menu camera turns
/// to face them). Resting on a character who is in the game picks them (GameOptions) a moment later,
/// so the carrier on screen changes with it; "coming soon" characters are shown but not picked.
/// </summary>
public partial class CharacterPage : ParchmentPanel
{
    [Export] public Vector2 MaxSize { get; set; } = new(620.0f, 630.0f);
    [Export] public float Margin { get; set; } = 24.0f;
    [Export(PropertyHint.File, "*.tres")] public string RosterPath { get; set; } = CarrierRoster.DefaultPath;
    /// <summary>Seconds the chooser must rest on a character before the carrier's model is swapped (holding an arrow doesn't reload every model).</summary>
    [Export] public float ApplyDelay { get; set; } = 0.3f;

    /// <summary>Raised by Done, Esc or B.</summary>
    public event Action? BackPressed;
    public event Action<float>? PreviewTurnRequested;

    public CharacterCarousel? Carousel => _carousel;
    public InkButton? DoneButton => _done;

    private CharacterCarousel? _carousel;
    private InkButton? _done;
    private CarrierCharacter? _pending;
    private float _pendingLeft;
    private ScrollContainer? _scroll;
    private Label? _presetName;
    private Label? _stats;

    public CharacterPage()
    {
        Title = "Create your carrier";
        Seed = 21.0f;
    }

    public override void _Ready()
    {
        base._Ready();
        GameOptions.Load();
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(_scroll);
        VBoxContainer content = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 10);
        _scroll.AddChild(content);
        Label steps = UiStyle.MakeLabel("01  ORIGIN    /    02  APPEARANCE    /    03  EQUIPMENT", 14);
        content.AddChild(steps);
        _carousel = new CharacterCarousel { NameSize = 32, TextSize = 18 };
        string selected = GameOptions.Character;
        foreach (string argument in OS.GetCmdlineUserArgs())
        { if (argument.StartsWith("--character=", StringComparison.Ordinal)) { selected = argument["--character=".Length..]; } }
        _carousel.SetCharacters(Roster(RosterPath), selected);
        _carousel.Shown += character =>
        {
            _pending = null;
            if (_stats != null) { _stats.Text = character.Stats?.Summary() ?? ""; }
            if (_presetName != null)
            {
                if (character.Id == "vucic") { _presetName.Text = "Aleksandar Vučić · origin preset"; }
                else { _presetName.Text = character.DisplayName + " · origin preset"; }
            }
            if (character.Available)
            {
                _pending = character;
                _pendingLeft = ApplyDelay;
            }
        };
        content.AddChild(_carousel);
        _presetName = UiStyle.MakeLabel("Origin preset", 17);
        if (_carousel.Character?.Id == "vucic") { _presetName.Text = "Aleksandar Vučić · origin preset"; }
        else { _presetName.Text = (_carousel.Character?.DisplayName ?? "Carrier") + " · origin preset"; }
        content.AddChild(_presetName);
        _stats = UiStyle.MakeLabel(_carousel.Character?.Stats?.Summary() ?? "", 16);
        _stats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(_stats);
        content.AddChild(UiStyle.MakeLabel("Base outfit colours", 22, true));
        content.AddChild(Palette("Shirt", colour => GameOptions.SetBaseOutfitColours(colour, GameOptions.BasePackColour)));
        content.AddChild(Palette("Pack", colour => GameOptions.SetBaseOutfitColours(GameOptions.BaseShirtColour, colour)));
        HBoxContainer turn = new();
        foreach ((string text, float angle) in new[] { ("Turn left", -45.0f), ("Turn right", 45.0f) })
        {
            InkButton button = new() { Text = text, Paper = true, Focusable = true, FontSize = 16,
                CustomMinimumSize = new Vector2(110, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            button.Pressed += () => PreviewTurnRequested?.Invoke(angle);
            turn.AddChild(button);
        }
        content.AddChild(turn);
        Label note = UiStyle.MakeLabel("Worn equipment keeps its own colour and condition.", 15);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(note);
        _done = new InkButton { Text = "Confirm appearance", Paper = true, Focusable = true, FontSize = UiStyle.TextSize, CustomMinimumSize = new Vector2(240.0f, 42.0f) };
        _done.Pressed += Leave;
        AddChild(_done);
        Resized += LayoutContent;
        LayoutContent();
    }

    private static HBoxContainer Palette(string title, Action<Color> apply)
    {
        HBoxContainer row = new();
        Label label = UiStyle.MakeLabel(title, 17);
        label.CustomMinimumSize = new Vector2(55, 32);
        row.AddChild(label);
        foreach ((string name, Color colour) in new[] { ("Blue", new Color(0.24f, 0.34f, 0.43f)), ("Moss", new Color(0.25f, 0.34f, 0.22f)),
            ("Sand", new Color(0.68f, 0.61f, 0.47f)), ("Coal", new Color(0.14f, 0.14f, 0.15f)) })
        {
            InkButton button = new() { Text = name, Paper = true, Focusable = true, FontSize = 14, SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(42, 32), TooltipText = name + " " + title.ToLowerInvariant() };
            button.Pressed += () => apply(colour);
            row.AddChild(button);
        }
        return row;
    }

    /// <summary>Every character in the roster, the ones not in the game yet included (shown as coming soon).</summary>
    public static List<CarrierCharacter> Roster(string path)
    {
        List<CarrierCharacter> list = new();
        CarrierRoster? roster = CarrierRoster.Load(path);
        if (roster == null)
        {
            return list;
        }
        foreach (string entry in roster.CharacterPaths)
        {
            if (CarrierRoster.LoadCharacter(entry) is CarrierCharacter character)
            {
                list.Add(character);
            }
        }
        return list;
    }

    public void FitInto(Rect2 area)
    {
        Vector2 size = new(Mathf.Min(MaxSize.X, area.Size.X), Mathf.Min(MaxSize.Y, area.Size.Y));
        Position = area.Position + (area.Size - size) * 0.5f;
        Size = size;
        LayoutContent();
    }

    private void LayoutContent()
    {
        if (_carousel == null || _done == null || _scroll == null)
        {
            return;
        }
        float top = HeaderHeight + 14.0f;
        _scroll.Position = new Vector2(Margin, top);
        _scroll.Size = new Vector2(Size.X - Margin * 2.0f, Mathf.Max(Size.Y - top - 75.0f, 90.0f));
        _carousel.Size = new Vector2(_scroll.Size.X - 14.0f, 170.0f);
        _carousel.CustomMinimumSize = new Vector2(0, Mathf.Max(170, _carousel.NeededHeight() + 8));
        _done.Position = new Vector2((Size.X - 240.0f) * 0.5f, Size.Y - 60.0f);
        _done.Size = new Vector2(240.0f, 42.0f);
    }

    public override void _Process(double delta)
    {
        if (_pending == null)
        {
            return;
        }
        _pendingLeft -= (float)delta;
        if (_pendingLeft <= 0.0f)
        {
            ApplyPending();
        }
    }

    /// <summary>Picks the character the chooser rests on (at once when leaving the page).</summary>
    public void ApplyPending()
    {
        CarrierCharacter? character = _pending;
        _pending = null;
        if (character != null && character.Id != GameOptions.Character)
        {
            GameOptions.SetCharacter(character.Id);
            _carousel?.QueueRedraw();
        }
    }

    private void Leave()
    {
        ApplyPending();
        BackPressed?.Invoke();
    }

    public void OpenSheet()
    {
        Open();
        _carousel?.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || !IsOpen || !IsVisibleInTree())
        {
            return;
        }
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Leave();
        }
    }
}
