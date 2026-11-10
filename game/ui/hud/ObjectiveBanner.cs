using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The card that says what to do, big, when it changes (the purpose patch): at the start of a run
/// ("Reach Station 1 before dark" / "Night kills outside a shelter…") and whenever the main goal
/// changes. A strip of parchment fades in near the top of the screen, stays a few seconds and fades
/// out. Waits while the game is paused (the title screen) or a page is open. Reads
/// GameEvents.ObjectiveAnnounced; presentation only.
/// </summary>
public partial class ObjectiveBanner : Control
{
    [Export] public float HoldSeconds { get; set; } = 5.5f;
    [Export] public float FadeSeconds { get; set; } = 0.6f;
    [Export] public Vector2 CardSize { get; set; } = new(600.0f, 104.0f);

    /// <summary>What the card shows now (empty when hidden).</summary>
    public string Showing { get; private set; } = "";
    public int Queued => _queue.Count;
    /// <summary>The card never starts above this y (set by the HUD: below the goal note).</summary>
    public float MinTop { get; set; } = 160.0f;
    /// <summary>The card stays between these x positions (set by the HUD: clear of the goal note and equipment).</summary>
    public Vector2 FreeSpan { get; set; } = new(0.0f, 100000.0f);
    /// <summary>The card is never narrower than this, in px.</summary>
    [Export] public float MinWidth { get; set; } = 260.0f;
    /// <summary>The title's ink shrinks down to this size to fit a narrow card.</summary>
    [Export] public int MinTitleSize { get; set; } = 20;
    /// <summary>Where the card was last placed.</summary>
    public Rect2 CardRect { get; private set; }

    private readonly Queue<ObjectiveNote> _queue = new();
    private ParchmentPanel? _card;
    private Label? _title;
    private Label? _why;
    private string _lastQueued = "";
    private float _left;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _card = new ParchmentPanel { Seed = 53.0f, Size = CardSize, Tear = 7.0f };
        AddChild(_card);
        _card.MouseFilter = MouseFilterEnum.Ignore;
        _title = UiStyle.MakeLabel("", UiStyle.HeadingSize, true);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.Position = new Vector2(16, 14);
        _title.Size = new Vector2(CardSize.X - 32, 40);
        _card.AddChild(_title);
        _why = UiStyle.MakeLabel("", UiStyle.TextSize, false, UiStyle.InkSoft);
        _why.HorizontalAlignment = HorizontalAlignment.Center;
        _why.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _why.Position = new Vector2(24, 54);
        _why.Size = new Vector2(CardSize.X - 48, 44);
        _card.AddChild(_why);
        _card.Modulate = new Color(1, 1, 1, 0);
        _card.Visible = false;
        GameEvents.ObjectiveAnnounced += OnAnnounced;
        GameEvents.RespawnRequested += OnNewRun;
        QueueCurrent();
    }

    public override void _ExitTree()
    {
        GameEvents.ObjectiveAnnounced -= OnAnnounced;
        GameEvents.RespawnRequested -= OnNewRun;
    }

    private void OnAnnounced(ObjectiveNote note)
    {
        if (note.Kind == ObjectiveKind.Main)
        {
            Enqueue(note);
        }
    }

    private void OnNewRun()
    {
        _lastQueued = "";
        _queue.Clear();
        QueueCurrent();
    }

    private void QueueCurrent()
    {
        IReadOnlyList<ObjectiveNote> current = GameEvents.CurrentObjectives;
        if (current.Count > 0 && current[0].Kind == ObjectiveKind.Main)
        {
            Enqueue(current[0]);
        }
    }

    /// <summary>Puts a goal on the card's queue, unless it is the same goal as the last one queued.</summary>
    public void Enqueue(ObjectiveNote note)
    {
        if (note.Title == _lastQueued)
        {
            return;
        }
        _lastQueued = note.Title;
        // Only the newest change matters: an older card that hasn't shown yet is dropped.
        _queue.Clear();
        _queue.Enqueue(note);
    }

    public override void _Process(double delta)
    {
        if (_card == null)
        {
            return;
        }
        bool held = GetTree().Paused && !GameEvents.PagesFrozen;
        if (_left > 0.0f)
        {
            if (!held)
            {
                _left -= (float)delta;
            }
            if (_left <= 0.0f)
            {
                Hide();
            }
            return;
        }
        if (_queue.Count == 0 || held || GameEvents.IsScreenOpen || Debug.DebugDraw3D.HideHud)
        {
            return;
        }
        ShowCard(_queue.Dequeue());
    }

    private void ShowCard(ObjectiveNote note)
    {
        if (_card == null || _title == null || _why == null)
        {
            return;
        }
        Fit(note, GetViewportRect().Size);
        Showing = note.Title;
        _card.Visible = true;
        _left = HoldSeconds + FadeSeconds;
        CreateTween().TweenProperty(_card, "modulate:a", 1.0f, FadeSeconds);
    }

    /// <summary>
    /// Sizes and places the card for a goal on a screen: as wide as CardSize allows within FreeSpan,
    /// centred on the screen when it can be, the title's ink made smaller until it fits on one line,
    /// and the reason wrapped below it. Returns where the card is (tests check it fits).
    /// </summary>
    public Rect2 Fit(ObjectiveNote note, Vector2 screen)
    {
        if (_card == null || _title == null || _why == null)
        {
            return new Rect2();
        }
        float spanLeft = Mathf.Max(FreeSpan.X, 0.0f);
        float spanRight = Mathf.Min(FreeSpan.Y, screen.X);
        float width = Mathf.Clamp(spanRight - spanLeft, MinWidth, CardSize.X);
        int size = UiStyle.HeadingSize;
        while (size > MinTitleSize && UiStyle.Wrapped(note.Title, size, 100000.0f, true).X > width - 32.0f)
        {
            size -= 2;
        }
        _title.AddThemeFontSizeOverride("font_size", size);
        _title.Text = note.Title;
        _title.Position = new Vector2(16, 14);
        _title.Size = new Vector2(width - 32, 40);
        _why.Text = note.Why;
        float whyHeight = UiStyle.Wrapped(note.Why, UiStyle.TextSize, width - 48.0f).Y;
        _why.Position = new Vector2(24, 54);
        _why.Size = new Vector2(width - 48, whyHeight);
        Vector2 cardSize = new(width, Mathf.Max(CardSize.Y, 62.0f + whyHeight));
        float x = Mathf.Clamp((screen.X - width) * 0.5f, spanLeft, Mathf.Max(spanLeft, spanRight - width));
        _card.Size = cardSize;
        _card.Position = new Vector2(x, Mathf.Max(MinTop, screen.Y * 0.26f));
        CardRect = new Rect2(_card.Position, cardSize);
        return CardRect;
    }

    private new void Hide()
    {
        if (_card == null)
        {
            return;
        }
        Showing = "";
        ParchmentPanel card = _card;
        Tween tween = CreateTween();
        tween.TweenProperty(card, "modulate:a", 0.0f, FadeSeconds);
        tween.TweenCallback(Callable.From(() => card.Visible = false));
    }
}
