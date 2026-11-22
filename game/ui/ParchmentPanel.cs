using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A window in the expedition look: the panel background (dark slate, hairline frame, a light rule
/// at its left), an optional heading in spaced capitals over a thin line, and an optional close
/// cross. Opens with a quick fade as it rises into place and closes by fading away.
/// (Named for the parchment sheet it replaced.)
/// </summary>
public partial class ParchmentPanel : Control
{
    [Signal] public delegate void ClosedEventHandler();

    public string Title { get; set; } = "";
    public bool Closable { get; set; }
    public float Seed { get; set; }
    public float Border { get; set; } = 4.0f;
    public float Tear { get; set; } = 5.0f;
    /// <summary>How solid the background is (negative: a page with a close cross, a window over the world, is nearly solid; other panels see-through).</summary>
    public float Opacity { get; set; } = -1.0f;
    private float ShownOpacity => Opacity >= 0.0f ? Opacity : (Closable ? UiStyle.PageOpacity : UiStyle.PanelOpacity);
    /// <summary>Space reserved at the top for the heading, in pixels.</summary>
    public float HeaderHeight
    {
        get
        {
            if (Title.Length > 0)
            {
                return 48.0f;
            }
            return 0.0f;
        }
    }
    public bool IsOpen { get; private set; } = true;

    private ColorRect? _paper;
    private ColorRect? _shadow;
    private Label? _title;
    private ColorRect? _rule;
    private InkButton? _close;
    private Tween? _tween;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        _shadow = new ColorRect { Color = UiStyle.PaperShadow, MouseFilter = MouseFilterEnum.Ignore, ShowBehindParent = true, Visible = false };
        AddChild(_shadow);
        _paper = new ColorRect { Color = Colors.White, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_paper);
        if (Title.Length > 0)
        {
            _title = UiStyle.MakeLabel(Title, UiStyle.HeadingSize, true);
            AddChild(_title);
            _rule = new ColorRect { Color = UiStyle.Line, MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_rule);
        }
        if (Closable)
        {
            _close = new InkButton { Glyph = InkButton.GlyphKind.Cross, CustomMinimumSize = new Vector2(30, 30) };
            _close.Pressed += () => Close();
            AddChild(_close);
        }
        Resized += Layout;
        Layout();
    }

    private void Layout()
    {
        if (_paper == null || _shadow == null)
        {
            return;
        }
        _paper.Position = Vector2.Zero;
        _paper.Size = Size;
        _paper.Material = UiStyle.Parchment(Size, Seed, Border, Tear, ShownOpacity);
        _shadow.Position = new Vector2(5, 6);
        _shadow.Size = Size;
        _shadow.Material = UiStyle.Parchment(Size, Seed, 0.0f, Tear);
        _shadow.SelfModulate = new Color(0, 0, 0, 0.35f);
        PivotOffset = Size * 0.5f;
        if (_title != null)
        {
            _title.Position = new Vector2(22, 9);
        }
        if (_rule != null)
        {
            _rule.Position = new Vector2(22, HeaderHeight - 6);
            _rule.Size = new Vector2(Mathf.Max(Size.X - 44, 0), 1);
        }
        if (_close != null)
        {
            _close.Position = new Vector2(Size.X - 42, 10);
            _close.Size = new Vector2(30, 30);
        }
        // Children of the panel move their own content; the paper stays at the back.
        MoveChild(_shadow, 0);
        MoveChild(_paper, 1);
    }

    /// <summary>Unfolds the sheet into view.</summary>
    public void Open()
    {
        IsOpen = true;
        Visible = true;
        KeepOnScreen();
        _tween?.Kill();
        PivotOffset = Size * 0.5f;
        Scale = Vector2.One;
        Rotation = 0.0f;
        Modulate = new Color(1, 1, 1, 0.0f);
        Vector2 settled = Position;
        Position = settled + new Vector2(0.0f, 12.0f);
        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(this, "position", settled, 0.22).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(this, "modulate:a", 1.0f, 0.18);
    }

    /// <summary>
    /// Opening a page brings all of it onto the screen (a smaller window, or a page dragged half off
    /// last time); a page bigger than the screen keeps its top-left corner in view.
    /// </summary>
    private void KeepOnScreen()
    {
        if (!IsInsideTree())
        {
            return;
        }
        Vector2 screen = GetViewportRect().Size;
        Vector2 parentOrigin = Vector2.Zero;
        if (GetParent() is Control parent)
        {
            parentOrigin = parent.GlobalPosition;
        }
        Vector2 at = GlobalPosition;
        float x = Mathf.Clamp(at.X, 0.0f, Mathf.Max(0.0f, screen.X - Size.X));
        float y = Mathf.Clamp(at.Y, 0.0f, Mathf.Max(0.0f, screen.Y - Size.Y));
        Position = new Vector2(x, y) - parentOrigin;
    }

    /// <summary>Folds the sheet away and hides it.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }
        IsOpen = false;
        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(this, "modulate:a", 0.0f, 0.14);
        _tween.Chain().TweenCallback(Callable.From(() =>
        {
            Visible = false;
            EmitSignal(SignalName.Closed);
        }));
    }

    /// <summary>Opens or closes it instantly (start-up, tests).</summary>
    public void SetOpenNow(bool open)
    {
        _tween?.Kill();
        IsOpen = open;
        Visible = open;
        Scale = Vector2.One;
        Rotation = 0.0f;
        Modulate = Colors.White;
    }
}
