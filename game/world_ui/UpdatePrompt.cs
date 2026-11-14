using Godot;
using DownhillRoguelite.Core;
using DownhillRoguelite.Core.Update;
using DownhillRoguelite.Ui;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// The title screen's one place for a new version, in the top right corner over the sky. It opens
/// once as a card (which release, when it came out, how big, what happens) with "Update now" focused
/// and "Later"; while it is open the menu waits dimmed behind it. Later (or Esc / B) folds it into a
/// small "Update" tab in the same corner, which opens the card again. Nothing else on the screen
/// repeats the news.
/// </summary>
public partial class UpdatePrompt : Control
{
    [Signal] public delegate void UpdateChosenEventHandler();
    /// <summary>The card folded away (Later, Esc): the menu takes focus back.</summary>
    [Signal] public delegate void FoldedEventHandler();
    /// <summary>The card opened (first time, or from the tab).</summary>
    [Signal] public delegate void UnfoldedEventHandler();

    private static readonly Vector2 CardSize = new(470.0f, 236.0f);
    /// <summary>A press this soon after the card appears was meant for the menu underneath.</summary>
    private const ulong GuardMs = 500;

    private ParchmentPanel? _card;
    private Label? _name;
    private Label? _text;
    private InkButton? _update;
    private InkButton? _later;
    private InkButton? _tab;
    private ulong _shownAt;
    private string _release = "";

    public bool Open { get; private set; } = true;
    public InkButton? UpdateButton => _update;
    public InkButton? LaterButton => _later;
    /// <summary>The folded "Update" tab (only visible while the card is folded).</summary>
    public InkButton? Tab => _tab;
    public string Message => _text?.Text ?? "";
    public string Headline => _name?.Text ?? "";

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _card = new ParchmentPanel { Seed = 37.0f, Title = "A new version is out", Size = CardSize, Tear = 6.0f };
        AddChild(_card);
        _name = UiStyle.MakeLabel(_release, UiStyle.TextSize + 2, true);
        _name.Position = new Vector2(22.0f, 52.0f);
        _name.Size = new Vector2(CardSize.X - 44.0f, 30.0f);
        _name.ClipText = true;
        _card.AddChild(_name);
        _text = UiStyle.MakeLabel("", UiStyle.TextSize - 2, false, new Color(UiStyle.Ink, 0.85f));
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.Position = new Vector2(22.0f, 84.0f);
        _text.Size = new Vector2(CardSize.X - 44.0f, 80.0f);
        _card.AddChild(_text);

        _update = new InkButton { Text = "Update now", Paper = true, Primary = true, Focusable = true, FontSize = UiStyle.TextSize, CustomMinimumSize = new Vector2(170.0f, 42.0f) };
        _update.Pressed += () =>
        {
            if (Settled())
            {
                EmitSignal(SignalName.UpdateChosen);
            }
        };
        _card.AddChild(_update);
        _later = new InkButton { Text = "Later", Paper = true, Focusable = true, FontSize = UiStyle.TextSize - 2, CustomMinimumSize = new Vector2(110.0f, 42.0f) };
        _later.Pressed += () =>
        {
            if (Settled())
            {
                Fold();
            }
        };
        _card.AddChild(_later);
        _update.Position = new Vector2(22.0f, CardSize.Y - 60.0f);
        _update.Size = _update.CustomMinimumSize;
        _later.Position = new Vector2(_update.Position.X + _update.Size.X + 16.0f, CardSize.Y - 60.0f);
        _later.Size = _later.CustomMinimumSize;
        _update.FocusNeighborRight = _update.GetPathTo(_later);
        _later.FocusNeighborLeft = _later.GetPathTo(_update);
        _update.FocusNeighborLeft = _update.GetPathTo(_update);
        _later.FocusNeighborRight = _later.GetPathTo(_later);

        _tab = new InkButton { Text = "Update", Paper = true, Focusable = true, FontSize = UiStyle.TextSize - 2, CustomMinimumSize = new Vector2(112.0f, 38.0f), Visible = false };
        _tab.Pressed += Unfold;
        AddChild(_tab);
        _tab.Size = _tab.CustomMinimumSize;

        Resized += Place;
        Place();
        _shownAt = Time.GetTicksMsec();
        _update.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>Fills the card for <paramref name="offer"/> (before or after it is in the tree).</summary>
    public void Fill(UpdateOffer offer)
    {
        _release = offer.ReleaseName;
        if (_name != null)
        {
            _name.Text = _release;
        }
        SetMessage(Describe(offer));
    }

    /// <summary>Replaces the card's body (progress of an in-game install, or what went wrong) and opens it.</summary>
    public void SetMessage(string text)
    {
        if (_text != null)
        {
            _text.Text = text;
        }
    }

    /// <summary>The card's body for <paramref name="offer"/>: when it came out, how big, what happens.</summary>
    public static string Describe(UpdateOffer offer)
    {
        string size = "under 1 MB";
        if (offer.SizeMb >= 1.0)
        {
            size = $"{offer.SizeMb:0} MB";
        }
        string what = "The whole game";
        if (offer.Kind == UpdateKind.Patch)
        {
            what = "A small update";
        }
        string when = "";
        if (offer.Released > System.DateTime.MinValue.AddYears(1))
        {
            when = "Out " + GameVersion.Date(offer.Released) + ".  ";
        }
        return $"{when}{what}, {size}. The game closes, updates itself and starts again. Your saved runs stay.";
    }

    /// <summary>Later: the card folds into the small tab in the corner.</summary>
    public void Fold()
    {
        if (!Open || _card == null || _tab == null)
        {
            return;
        }
        Open = false;
        _card.Visible = false;
        _tab.Visible = true;
        EmitSignal(SignalName.Folded);
    }

    /// <summary>The tab was chosen: the card opens again, "Update now" focused.</summary>
    public void Unfold()
    {
        if (Open || _card == null || _tab == null)
        {
            return;
        }
        Open = true;
        _tab.Visible = false;
        _card.Visible = true;
        _shownAt = Time.GetTicksMsec();
        _update?.GrabFocus();
        EmitSignal(SignalName.Unfolded);
    }

    private bool Settled()
    {
        return Time.GetTicksMsec() - _shownAt >= GuardMs;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Open && Visible && @event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Fold();
        }
    }

    private void Place()
    {
        if (_card == null || _tab == null)
        {
            return;
        }
        Vector2 screen = Size;
        if (screen.X <= 0.0f || screen.Y <= 0.0f)
        {
            screen = GetViewportRect().Size;
        }
        float margin = Mathf.Max(24.0f, screen.X * 0.03f);
        _card.Position = new Vector2(screen.X - CardSize.X - margin, margin).Floor();
        _tab.Position = new Vector2(screen.X - _tab.Size.X - margin, margin).Floor();
    }
}
