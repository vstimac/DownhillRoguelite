using System;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A setting with a few named values ("‹ Dials ›"): left/right or clicking the inked arrows steps
/// through them (wrapping), and each change is handed to <see cref="Changed"/> at once.
/// A toggle is a choice of Off / On.
/// </summary>
public partial class ChoiceRow : OptionRow
{
    public string[] Options { get; set; } = Array.Empty<string>();
    public int Selected { get; set; }
    public bool Wrap { get; set; } = true;
    public Action<int>? Changed { get; set; }

    private const float ArrowWidth = 30.0f;

    public static ChoiceRow Toggle(string label, bool on, Action<bool> set, string hint = "")
    {
        int selected = 0;
        if (on)
        {
            selected = 1;
        }
        return new ChoiceRow { Label = label, Hint = hint, Options = new[] { "Off", "On" }, Selected = selected, Changed = i => set(i == 1) };
    }

    public override bool Fits()
    {
        float room = ValueRect().Size.X - ArrowWidth * 2.0f;
        foreach (string option in Options)
        {
            if (UiStyle.Hand.GetStringSize(option, HorizontalAlignment.Left, -1, FontSize).X > room)
            {
                return false;
            }
        }
        return base.Fits();
    }

    public override string ValueText
    {
        get
        {
            if (Options.Length == 0)
            {
                return "";
            }
            return Options[Mathf.Clamp(Selected, 0, Options.Length - 1)];
        }
    }

    public override void Step(int direction)
    {
        if (Options.Length < 2)
        {
            return;
        }
        int next = Selected + direction;
        if (Wrap)
        {
            next = (next + Options.Length) % Options.Length;
        }
        else
        {
            next = Mathf.Clamp(next, 0, Options.Length - 1);
        }
        if (next == Selected)
        {
            return;
        }
        Selected = next;
        Changed?.Invoke(Selected);
        Nudge(direction);
        QueueRedraw();
    }

    private float _nudge;

    /// <summary>The value hops a little towards the side it was stepped to.</summary>
    private void Nudge(int direction)
    {
        Tween tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(v =>
        {
            _nudge = v;
            QueueRedraw();
        }), 6.0f * direction, 0.0f, 0.18).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    protected override void RowInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton click && click.Pressed && click.ButtonIndex == MouseButton.Left)
        {
            Rect2 value = ValueRect();
            int direction = 1;
            if (click.Position.X < value.Position.X + ArrowWidth)
            {
                direction = -1;
            }
            Step(direction);
            AcceptEvent();
        }
    }

    protected override void DrawValue()
    {
        Rect2 box = ValueRect();
        Font font = UiStyle.Hand;
        float baseline = Baseline(FontSize);
        Color ink = UiStyle.Ink;
        Color arrow = new(UiStyle.Ink, 0.35f + 0.55f * Lit);
        float mid = Size.Y * 0.5f;
        float left = box.Position.X + 8.0f;
        float right = box.End.X - 8.0f;
        DrawPolyline(new[] { new Vector2(left + 10.0f, mid - 8.0f), new Vector2(left, mid), new Vector2(left + 10.0f, mid + 8.0f) }, arrow, 2.5f, true);
        DrawPolyline(new[] { new Vector2(right - 10.0f, mid - 8.0f), new Vector2(right, mid), new Vector2(right - 10.0f, mid + 8.0f) }, arrow, 2.5f, true);
        string text = ValueText;
        float width = box.Size.X - ArrowWidth * 2.0f;
        float textWidth = font.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize).X;
        float x = box.Position.X + ArrowWidth + Mathf.Max(0.0f, (width - textWidth) * 0.5f) + _nudge;
        DrawString(font, new Vector2(x, baseline), text, HorizontalAlignment.Left, width, FontSize, ink);
        // Dots under the value show how many choices there are and which one this is.
        if (Options.Length > 1 && Options.Length <= 8)
        {
            float gap = 9.0f;
            float start = box.Position.X + box.Size.X * 0.5f - gap * (Options.Length - 1) * 0.5f;
            for (int i = 0; i < Options.Length; i++)
            {
                float radius = 1.6f;
                Color dot = new(UiStyle.Ink, 0.3f);
                if (i == Selected)
                {
                    radius = 2.6f;
                    dot = UiStyle.Ink;
                }
                DrawCircle(new Vector2(start + gap * i, Size.Y - 6.0f), radius, dot);
            }
        }
    }
}
