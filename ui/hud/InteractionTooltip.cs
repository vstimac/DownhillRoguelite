using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The note that slides in from the right edge when the carrier looks at something it can use
/// (GameEvents.FocusChanged with an IDescribed): its icon, name, a line about it, its real
/// properties and the key to press, drawn as a key cap. Slides back out when the carrier looks away.
/// </summary>
public partial class InteractionTooltip : ParchmentPanel
{
    public bool Showing { get; private set; }
    public string TitleText => _title?.Text ?? "";

    private ItemSlot? _icon;
    private Label? _title;
    private Label? _body;
    private KeyPrompt? _prompt;
    private Tween? _slide;
    private Vector2 _home;

    public override void _Ready()
    {
        Seed = 31.0f;
        base._Ready();
        _icon = new ItemSlot { Position = new Vector2(16, 18), Size = new Vector2(70, 70), Index = 77 };
        AddChild(_icon);
        _title = UiStyle.MakeLabel("", 26, true);
        _title.Position = new Vector2(98, 12);
        AddChild(_title);
        _body = UiStyle.MakeLabel("", UiStyle.SmallSize);
        _body.Position = new Vector2(98, 48);
        _body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _body.Size = new Vector2(Size.X - 112, 80);
        AddChild(_body);
        _prompt = new KeyPrompt { Position = new Vector2(16, Size.Y - 40), Size = new Vector2(Size.X - 30, 30) };
        AddChild(_prompt);
        GameEvents.FocusChanged += OnFocus;
        SetOpenNow(false);
    }

    public override void _ExitTree()
    {
        GameEvents.FocusChanged -= OnFocus;
    }

    /// <summary>Remembers where it rests on screen (the Hud lays it out).</summary>
    public void SetHome(Vector2 home)
    {
        _home = home;
        Position = home;
    }

    private void OnFocus(object? focused)
    {
        if (focused is not IDescribed described || _title == null || _body == null || _prompt == null || _icon == null)
        {
            Hide(false);
            return;
        }
        _title.Text = described.Title;
        _body.Text = described.Description + "\n" + described.Details;
        _prompt.Text = described.Prompt;
        _prompt.QueueRedraw();
        _icon.SetItem(described.IconSource as ItemDefinition, 1);
        _icon.EmptyGlyph = InkIcon.Backpack;
        Hide(true);
    }

    private void Hide(bool show)
    {
        if (show == Showing && Visible == show)
        {
            return;
        }
        Showing = show;
        _slide?.Kill();
        _slide = CreateTween().SetParallel();
        if (show)
        {
            Visible = true;
            Position = _home + new Vector2(80, 0);
            Modulate = new Color(1, 1, 1, 0);
            Rotation = Mathf.DegToRad(4.0f);
            _slide.TweenProperty(this, "position", _home, 0.25).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            _slide.TweenProperty(this, "modulate:a", 1.0f, 0.15);
            _slide.TweenProperty(this, "rotation", Mathf.DegToRad(-1.0f), 0.3);
        }
        else
        {
            _slide.TweenProperty(this, "position", _home + new Vector2(80, 0), 0.18).SetEase(Tween.EaseType.In);
            _slide.TweenProperty(this, "modulate:a", 0.0f, 0.18);
            _slide.Chain().TweenCallback(Callable.From(() => Visible = false));
        }
    }

    /// <summary>"E  Pick up" drawn as an inked key cap and its action.</summary>
    private partial class KeyPrompt : Control
    {
        public string Text { get; set; } = "";

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            int split = Text.IndexOf("  ");
            if (split < 0)
            {
                return;
            }
            string key = Text.Substring(0, split);
            string action = Text.Substring(split + 2);
            Font font = UiStyle.Hand;
            Rect2 cap = new(new Vector2(0, 2), new Vector2(26, 26));
            InkDraw.Shape(this, new[] { cap.Position, new Vector2(cap.End.X, cap.Position.Y), cap.End, new Vector2(cap.Position.X, cap.End.Y) }, new Color(0.3f, 0.24f, 0.18f), 2.0f, 9);
            DrawString(font, new Vector2(7, 23), key, HorizontalAlignment.Left, -1, 20, UiStyle.Paper);
            DrawString(font, new Vector2(36, 23), action, HorizontalAlignment.Left, -1, 20, UiStyle.Ink);
        }
    }
}
