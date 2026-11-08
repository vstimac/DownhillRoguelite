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
    [Export] public Vector2 MaxSize { get; set; } = new(560.0f, 420.0f);
    [Export] public float Margin { get; set; } = 24.0f;
    [Export(PropertyHint.File, "*.tres")] public string RosterPath { get; set; } = CarrierRoster.DefaultPath;
    /// <summary>Seconds the chooser must rest on a character before the carrier's model is swapped (holding an arrow doesn't reload every model).</summary>
    [Export] public float ApplyDelay { get; set; } = 0.3f;

    /// <summary>Raised by Done, Esc or B.</summary>
    public event Action? BackPressed;

    public CharacterCarousel? Carousel => _carousel;
    public InkButton? DoneButton => _done;

    private CharacterCarousel? _carousel;
    private InkButton? _done;
    private CarrierCharacter? _pending;
    private float _pendingLeft;

    public CharacterPage()
    {
        Title = "Who carries the load?";
        Seed = 21.0f;
    }

    public override void _Ready()
    {
        base._Ready();
        GameOptions.Load();
        _carousel = new CharacterCarousel();
        _carousel.SetCharacters(Roster(RosterPath), GameOptions.Character);
        _carousel.Shown += character =>
        {
            _pending = null;
            if (character.Available)
            {
                _pending = character;
                _pendingLeft = ApplyDelay;
            }
        };
        AddChild(_carousel);
        _done = new InkButton { Text = "Done", Paper = true, Focusable = true, FontSize = UiStyle.TextSize + 2, CustomMinimumSize = new Vector2(140.0f, 42.0f) };
        _done.Pressed += Leave;
        AddChild(_done);
        Resized += LayoutContent;
        LayoutContent();
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
            if (ResourceLoader.Exists(entry) && ResourceLoader.Load(entry) is CarrierCharacter character)
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
        if (_carousel == null || _done == null)
        {
            return;
        }
        float top = HeaderHeight + 14.0f;
        float bottom = Size.Y - 70.0f;
        _carousel.Position = new Vector2(Margin, top);
        _carousel.Size = new Vector2(Size.X - Margin * 2.0f, Mathf.Max(bottom - top, 60.0f));
        _done.Position = new Vector2((Size.X - 140.0f) * 0.5f, Size.Y - 60.0f);
        _done.Size = new Vector2(140.0f, 42.0f);
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
