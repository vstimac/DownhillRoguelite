using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// The Saved runs page (main menu): the run kept in each slot (RunSave.SlotCount of them) as a
/// button that carries it on, or "empty: start a new run here"; and a box for a seed of the
/// player's own, which starts a new run on exactly that mountain (digits are the seed itself, any
/// other words are turned into one, so friends can share a word). Back, Esc or B returns.
/// </summary>
public partial class SavesPage : ParchmentPanel
{
    [Export] public Vector2 MaxSize { get; set; } = new(720.0f, 520.0f);
    [Export] public float Margin { get; set; } = 26.0f;
    [Export] public float RowHeight { get; set; } = 54.0f;

    public event Action? BackPressed;
    /// <summary>A slot was picked (1..RunSave.SlotCount).</summary>
    public event Action<int>? SlotChosen;
    /// <summary>A new run on this seed was asked for.</summary>
    public event Action<ulong>? SeedChosen;

    public IReadOnlyList<InkButton> SlotButtons => _slots;
    public LineEdit? SeedBox => _seed;
    public InkButton? BackButton => _back;

    private readonly List<InkButton> _slots = new();
    private Label? _seedLabel;
    private LineEdit? _seed;
    private InkButton? _start;
    private InkButton? _back;

    public SavesPage()
    {
        Title = "Saved runs";
        Seed = 47.0f;
    }

    public override void _Ready()
    {
        base._Ready();
        for (int slot = 1; slot <= RunSave.SlotCount; slot++)
        {
            int chosen = slot;
            InkButton button = new() { Paper = true, Focusable = true, FontSize = UiStyle.TextSize };
            button.Pressed += () => SlotChosen?.Invoke(chosen);
            AddChild(button);
            _slots.Add(button);
        }
        _seedLabel = UiStyle.MakeLabel("A mountain of your own: type a seed (a number or any word)", UiStyle.SmallSize, false, UiStyle.InkSoft);
        AddChild(_seedLabel);
        _seed = new LineEdit { PlaceholderText = "seed", MaxLength = 40 };
        _seed.TextSubmitted += _ => StartWithSeed();
        AddChild(_seed);
        _start = new InkButton { Text = "Start this mountain", Paper = true, Focusable = true, FontSize = UiStyle.TextSize };
        _start.Pressed += StartWithSeed;
        AddChild(_start);
        _back = new InkButton { Text = "Back", Paper = true, Focusable = true, FontSize = UiStyle.TextSize + 2 };
        _back.Pressed += () => BackPressed?.Invoke();
        AddChild(_back);
        Resized += LayoutContent;
        Refresh();
        LayoutContent();
    }

    /// <summary>Reads the slots as they are on disk now.</summary>
    public void Refresh()
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            RunSave? save = RunSave.Peek(i + 1);
            if (save == null)
            {
                _slots[i].Text = "Slot " + (i + 1) + " · empty: start a new run here";
            }
            else
            {
                _slots[i].Text = "Slot " + (i + 1) + " · " + save.Describe();
            }
            _slots[i].QueueRedraw();
        }
    }

    /// <summary>Digits are the seed itself; anything else is turned into one (the same text, the same mountain).</summary>
    public static ulong SeedFrom(string text)
    {
        text = text.Trim();
        if (ulong.TryParse(text, out ulong number) && number != 0)
        {
            return number;
        }
        // FNV-1a over the text, lower-cased so "Olimp" and "olimp" are one mountain.
        ulong hash = 14695981039346656037UL;
        foreach (char letter in text.ToLowerInvariant())
        {
            hash ^= letter;
            hash *= 1099511628211UL;
        }
        if (hash == 0)
        {
            hash = 1;
        }
        return hash;
    }

    private void StartWithSeed()
    {
        if (_seed == null || _seed.Text.Trim().Length == 0)
        {
            _seed?.GrabFocus();
            return;
        }
        SeedChosen?.Invoke(SeedFrom(_seed.Text));
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
        if (_seed == null || _seedLabel == null || _start == null || _back == null)
        {
            return;
        }
        float width = Size.X - Margin * 2.0f;
        float y = HeaderHeight + 10.0f;
        foreach (InkButton slot in _slots)
        {
            slot.Position = new Vector2(Margin, y);
            slot.Size = new Vector2(width, RowHeight - 8.0f);
            y += RowHeight;
        }
        y += 12.0f;
        _seedLabel.Position = new Vector2(Margin, y);
        _seedLabel.Size = new Vector2(width, 24.0f);
        y += 30.0f;
        float button = 220.0f;
        _seed.Position = new Vector2(Margin, y);
        _seed.Size = new Vector2(width - button - 12.0f, 42.0f);
        _start.Position = new Vector2(Margin + width - button, y);
        _start.Size = new Vector2(button, 42.0f);
        _back.Position = new Vector2((Size.X - 130.0f) * 0.5f, Size.Y - 60.0f);
        _back.Size = new Vector2(130.0f, 42.0f);
    }

    public void OpenSheet()
    {
        Refresh();
        Open();
        if (_slots.Count > 0)
        {
            _slots[0].GrabFocus();
        }
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
            BackPressed?.Invoke();
        }
    }
}
