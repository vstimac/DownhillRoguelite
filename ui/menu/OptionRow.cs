using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// One line of the Options sheet: the setting's name in ink on the left, its value drawn by the
/// subclass on the right. Rows take focus from the mouse, the keyboard and a controller alike
/// (up/down moves between rows, left/right changes the value), and the focused row is washed in
/// soft ink with a margin tick, so it is always clear what left/right will change.
/// </summary>
public abstract partial class OptionRow : Control
{
    public string Label { get; set; } = "";
    /// <summary>A short explanation shown by the sheet's footer while the row is focused.</summary>
    public string Hint { get; set; } = "";
    public int FontSize { get; set; } = UiStyle.TextSize;
    public float RowHeight { get; set; } = 40.0f;
    /// <summary>Share of the width given to the name; the value fills the rest.</summary>
    public float LabelShare { get; set; } = 0.5f;

    /// <summary>Holding left/right repeats (sliders); choices step once per press.</summary>
    protected virtual bool RepeatSteps => false;

    /// <summary>0 at rest, 1 focused (animated).</summary>
    public float Lit { get; private set; }

    private Tween? _tween;

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(CustomMinimumSize.X, RowHeight);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseEntered += () => GrabFocus();
        FocusEntered += () => Light(1.0f);
        FocusExited += () => Light(0.0f);
        Resized += QueueRedraw;
    }

    private void Light(float target)
    {
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(v =>
        {
            Lit = v;
            QueueRedraw();
        }), Lit, target, 0.12);
    }

    /// <summary>The rectangle the value is drawn in.</summary>
    protected Rect2 ValueRect()
    {
        float left = Size.X * LabelShare;
        return new Rect2(left, 0.0f, Size.X - left - 10.0f, Size.Y);
    }

    /// <summary>Baseline for text of <paramref name="size"/> centred in the row.</summary>
    protected float Baseline(int size)
    {
        Font font = UiStyle.Hand;
        return (Size.Y + font.GetAscent(size) - font.GetDescent(size)) * 0.5f;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_left", RepeatSteps))
        {
            Step(-1);
            AcceptEvent();
        }
        else if (@event.IsActionPressed("ui_right", RepeatSteps))
        {
            Step(1);
            AcceptEvent();
        }
        else if (@event.IsActionPressed("ui_accept"))
        {
            Activate();
            AcceptEvent();
        }
        else
        {
            RowInput(@event);
        }
    }

    /// <summary>Left (-1) or right (+1) on the focused row.</summary>
    public virtual void Step(int direction)
    {
    }

    /// <summary>Enter, Space or the controller's A button on the focused row.</summary>
    protected virtual void Activate()
    {
        Step(1);
    }

    /// <summary>Other input (mouse clicks, drags) for the subclass.</summary>
    protected virtual void RowInput(InputEvent @event)
    {
    }

    /// <summary>Whether the name and every value it can show fit the row unclipped (layout tests).</summary>
    public virtual bool Fits()
    {
        float label = UiStyle.Hand.GetStringSize(Label, HorizontalAlignment.Left, -1, FontSize).X;
        return label <= Size.X * LabelShare - 20.0f;
    }

    /// <summary>The value's text as shown (for tests and the footer).</summary>
    public abstract string ValueText { get; }

    public override void _Draw()
    {
        if (Lit > 0.01f)
        {
            DrawRect(new Rect2(0.0f, 2.0f, Size.X, Size.Y - 4.0f), new Color(UiStyle.Ink, 0.10f * Lit));
            DrawRect(new Rect2(0.0f, 4.0f, 3.0f, Size.Y - 8.0f), new Color(UiStyle.Ink, 0.8f * Lit));
        }
        Font font = UiStyle.Hand;
        Color ink = UiStyle.Ink;
        if (Lit > 0.5f)
        {
            ink = new Color(0.36f, 0.15f, 0.04f);
        }
        DrawString(font, new Vector2(14.0f, Baseline(FontSize)), Label, HorizontalAlignment.Left, Size.X * LabelShare - 20.0f, FontSize, ink);
        DrawValue();
    }

    protected abstract void DrawValue();
}
