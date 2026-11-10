using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// The Achievements page (main menu and pause menu): every achievement as a line in the journal,
/// with an ink box ticked in red-brown when it is done and its date, or faint with the progress so
/// far ("3 / 25") when it isn't. Reads RunState.Achievements; up/down or the wheel scrolls; Back,
/// Esc or B returns.
/// </summary>
public partial class AchievementsPage : ParchmentPanel
{
    [Export] public Vector2 MaxSize { get; set; } = new(760.0f, 640.0f);
    [Export] public float Margin { get; set; } = 26.0f;
    [Export] public float ScrollStep { get; set; } = 48.0f;
    [Export] public float TickSize { get; set; } = 40.0f;

    public event Action? BackPressed;

    public InkButton? BackButton => _back;
    /// <summary>The rows' tick boxes, in AchievementBook.All's order (tests).</summary>
    public IReadOnlyList<TickBox> Ticks => _ticks;
    public Label? Summary => _summary;

    private readonly List<TickBox> _ticks = new();
    private ScrollContainer? _scroll;
    private VBoxContainer? _column;
    private Label? _summary;
    private InkButton? _back;

    public AchievementsPage()
    {
        Title = "Achievements";
        Seed = 41.0f;
    }

    public override void _Ready()
    {
        base._Ready();
        _summary = UiStyle.MakeLabel("", UiStyle.TextSize, false, UiStyle.InkSoft);
        _summary.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_summary);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(_scroll);
        _column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _column.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(_column);
        _back = new InkButton { Text = "Back", Paper = true, Focusable = true, FontSize = UiStyle.TextSize + 2, CustomMinimumSize = new Vector2(130.0f, 42.0f) };
        _back.Pressed += () => BackPressed?.Invoke();
        AddChild(_back);
        Resized += LayoutContent;
        Refresh();
        LayoutContent();
    }

    /// <summary>Fills the list from the book as it stands now.</summary>
    public void Refresh()
    {
        if (_column == null)
        {
            return;
        }
        foreach (Node child in _column.GetChildren())
        {
            child.QueueFree();
        }
        _ticks.Clear();
        AchievementBook? book = GetNodeOrNull<RunState>("/root/RunState")?.Achievements;
        int unlocked = 0;
        int seed = 1;
        foreach (AchievementBook.Entry entry in AchievementBook.All)
        {
            bool done = book?.IsUnlocked(entry.Id) ?? false;
            if (done)
            {
                unlocked++;
            }
            _column.AddChild(Row(entry, done, book, seed));
            seed++;
        }
        if (_summary != null)
        {
            _summary.Text = $"{unlocked} of {AchievementBook.All.Length} done";
        }
    }

    private HBoxContainer Row(AchievementBook.Entry entry, bool done, AchievementBook? book, int seed)
    {
        HBoxContainer row = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 12);
        TickBox tick = new() { Ticked = done, Seed = seed, CustomMinimumSize = new Vector2(TickSize, TickSize) };
        row.AddChild(tick);
        _ticks.Add(tick);
        VBoxContainer text = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", -4);
        Color ink = UiStyle.Ink;
        if (!done)
        {
            ink = new Color(UiStyle.Ink, 0.62f);
        }
        text.AddChild(UiStyle.MakeLabel(entry.Title, UiStyle.HeadingSize - 8, true, ink));
        Label description = UiStyle.MakeLabel(entry.Description, UiStyle.TextSize - 2, false, ink);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.AddChild(description);
        row.AddChild(text);
        Label state = UiStyle.MakeLabel(State(entry, done, book), UiStyle.SmallSize, false, new Color(UiStyle.Ink, 0.7f));
        state.HorizontalAlignment = HorizontalAlignment.Right;
        state.VerticalAlignment = VerticalAlignment.Center;
        state.CustomMinimumSize = new Vector2(110.0f, 0.0f);
        state.SizeFlagsVertical = SizeFlags.Fill;
        row.AddChild(state);
        return row;
    }

    private static string State(AchievementBook.Entry entry, bool done, AchievementBook? book)
    {
        if (book == null)
        {
            return "";
        }
        if (done)
        {
            long at = book.UnlockedAt(entry.Id);
            if (at <= 0)
            {
                return "done";
            }
            return Time.GetDateStringFromUnixTime(at);
        }
        if (entry.Goal <= 1)
        {
            return "";
        }
        return $"{Mathf.Min(book.Tally(entry.Tally), entry.Goal)} / {entry.Goal}";
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
        if (_scroll == null || _back == null || _summary == null)
        {
            return;
        }
        float top = HeaderHeight + 4.0f;
        _summary.Position = new Vector2(Margin, top);
        _summary.Size = new Vector2(Size.X - Margin * 2.0f, 28.0f);
        top += 36.0f;
        _scroll.Position = new Vector2(Margin, top);
        _scroll.Size = new Vector2(Size.X - Margin * 2.0f, Mathf.Max(Size.Y - top - 74.0f, 40.0f));
        _back.Position = new Vector2((Size.X - 130.0f) * 0.5f, Size.Y - 60.0f);
        _back.Size = new Vector2(130.0f, 42.0f);
    }

    public void OpenSheet()
    {
        Refresh();
        Open();
        if (_scroll != null)
        {
            _scroll.ScrollVertical = 0;
        }
        _back?.GrabFocus();
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

    public override void _Input(InputEvent @event)
    {
        if (!Visible || !IsOpen || _scroll == null || !IsVisibleInTree())
        {
            return;
        }
        int direction = 0;
        if (@event.IsActionPressed("ui_up", true))
        {
            direction = -1;
        }
        else if (@event.IsActionPressed("ui_down", true))
        {
            direction = 1;
        }
        if (direction != 0)
        {
            _scroll.ScrollVertical += (int)(ScrollStep * direction);
            GetViewport().SetInputAsHandled();
        }
    }
}
