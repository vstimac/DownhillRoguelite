using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// "Achievement unlocked": a small parchment card with an ink tick slides in at the top right when
/// an achievement is unlocked for the first time (GameEvents.AchievementUnlocked), stays a few
/// seconds and slides away; several wait their turn. Presentation only.
/// </summary>
public partial class AchievementToast : Control
{
    [Export] public float HoldSeconds { get; set; } = 4.5f;
    [Export] public float SlideSeconds { get; set; } = 0.45f;
    [Export] public Vector2 CardSize { get; set; } = new(330.0f, 78.0f);
    [Export] public float TopMargin { get; set; } = 170.0f;
    [Export] public float RightMargin { get; set; } = 18.0f;
    [Export] public string Heading { get; set; } = "Achievement unlocked";

    /// <summary>The title showing now (empty when hidden).</summary>
    public string Showing { get; private set; } = "";
    public int Queued => _queue.Count;

    private readonly Queue<string> _queue = new();
    private ParchmentPanel? _card;
    private Label? _heading;
    private Label? _title;
    private TickBox? _tick;
    private float _left;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _card = new ParchmentPanel { Seed = 71.0f, Size = CardSize, Tear = 5.0f, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_card);
        _tick = new TickBox { Ticked = true, Seed = 5, Position = new Vector2(14.0f, (CardSize.Y - 46.0f) * 0.5f), Size = new Vector2(46.0f, 46.0f) };
        _card.AddChild(_tick);
        _heading = UiStyle.MakeLabel(Heading, UiStyle.SmallSize, false, UiStyle.InkSoft);
        _heading.Position = new Vector2(72.0f, 10.0f);
        _heading.Size = new Vector2(CardSize.X - 84.0f, 24.0f);
        _card.AddChild(_heading);
        _title = UiStyle.MakeLabel("", UiStyle.HeadingSize - 6, true);
        _title.Position = new Vector2(72.0f, 32.0f);
        _title.Size = new Vector2(CardSize.X - 84.0f, 36.0f);
        _title.ClipText = true;
        _card.AddChild(_title);
        _card.Visible = false;
        GameEvents.AchievementUnlocked += OnUnlocked;
    }

    public override void _ExitTree()
    {
        GameEvents.AchievementUnlocked -= OnUnlocked;
    }

    private void OnUnlocked(string id, string title)
    {
        _queue.Enqueue(title);
    }

    public override void _Process(double delta)
    {
        if (_card == null)
        {
            return;
        }
        if (Showing.Length > 0)
        {
            _left -= (float)delta;
            if (_left <= 0.0f)
            {
                SlideOut(_card);
            }
            return;
        }
        if (_queue.Count > 0 && !GetTree().Paused)
        {
            SlideIn(_card, _queue.Dequeue());
        }
    }

    private void SlideIn(ParchmentPanel card, string title)
    {
        Showing = title;
        _left = HoldSeconds + SlideSeconds;
        if (_title != null)
        {
            _title.Text = title;
        }
        float width = GetViewportRect().Size.X;
        Vector2 shown = new(width - CardSize.X - RightMargin, TopMargin);
        card.Position = shown + new Vector2(CardSize.X + RightMargin + 20.0f, 0.0f);
        card.Visible = true;
        card.Modulate = Colors.White;
        Tween tween = CreateTween().SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(card, "position", shown, SlideSeconds);
    }

    private void SlideOut(ParchmentPanel card)
    {
        Showing = "";
        Tween tween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        tween.TweenProperty(card, "modulate:a", 0.0f, SlideSeconds);
        tween.TweenCallback(Callable.From(() =>
        {
            if (Showing.Length == 0)
            {
                card.Visible = false;
            }
        }));
    }
}
