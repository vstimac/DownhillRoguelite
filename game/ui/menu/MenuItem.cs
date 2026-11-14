using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// One entry of a front-end menu (main menu, pause menu): old-print text in paper colour on the dark
/// scene. Pointing at it with the mouse, the keyboard or a controller gives it focus, and the focused
/// entry slides right onto a torn parchment strip with inked text. Disabled entries are dimmed.
/// </summary>
public partial class MenuItem : BaseButton
{
    public string Text { get; set; } = "";
    /// <summary>One line telling what the entry does, shown by the menu's footer while it is focused.</summary>
    public string Hint { get; set; } = "";
    public int FontSize { get; set; } = 34;
    /// <summary>An optional second line under the entry in plain hand (Continue's "Station 3, day 2"): always shown, not only on focus.</summary>
    public string Detail { get; set; } = "";
    public int DetailSize { get; set; } = UiStyle.SmallSize + 2;
    /// <summary>How far the focused entry slides right, in pixels.</summary>
    public float Slide { get; set; } = 14.0f;
    public float AnimSeconds { get; set; } = 0.16f;

    /// <summary>0 at rest, 1 fully focused (animated).</summary>
    public float Lit { get; private set; }

    private ColorRect? _strip;
    private Tween? _tween;

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        CustomMinimumSize = new Vector2(CustomMinimumSize.X, Mathf.Max(CustomMinimumSize.Y, FontSize * 1.45f));
        _strip = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, ShowBehindParent = true, Modulate = new Color(1, 1, 1, 0) };
        AddChild(_strip);
        MouseEntered += () =>
        {
            if (!Disabled)
            {
                GrabFocus();
            }
        };
        FocusEntered += () => Light(true);
        FocusExited += () => Light(false);
        ButtonDown += Press;
        Resized += Layout;
        Layout();
    }

    private void Layout()
    {
        if (_strip == null)
        {
            return;
        }
        float text = Mathf.Max(UiStyle.Heading.GetStringSize(Text, HorizontalAlignment.Left, -1, FontSize).X, DetailWidth());
        Vector2 size = new(Mathf.Min(Size.X, text + 44.0f), Size.Y);
        _strip.Position = new Vector2(-12.0f, 0.0f);
        _strip.Size = size;
        _strip.Material = UiStyle.Parchment(size, Text.Length * 1.7f, 2.0f, 3.0f);
    }

    /// <summary>The width the entry's text needs, for layout.</summary>
    public float TextWidth()
    {
        return Mathf.Max(UiStyle.Heading.GetStringSize(Text, HorizontalAlignment.Left, -1, FontSize).X, DetailWidth()) + 44.0f + Slide;
    }

    /// <summary>The height the entry needs: its line, plus the detail line when it has one.</summary>
    public float NeededHeight(float lineHeight)
    {
        if (Detail.Length == 0)
        {
            return lineHeight;
        }
        return lineHeight + DetailSize * 1.5f;
    }

    private float DetailWidth()
    {
        if (Detail.Length == 0)
        {
            return 0.0f;
        }
        return UiStyle.Hand.GetStringSize(Detail, HorizontalAlignment.Left, -1, DetailSize).X;
    }

    /// <summary>Re-measures the parchment strip after the text, sizes or detail changed.</summary>
    public void Refresh()
    {
        Layout();
        QueueRedraw();
    }

    private void Light(bool on)
    {
        float target = 0.0f;
        if (on)
        {
            target = 1.0f;
        }
        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        _tween.TweenMethod(Callable.From<float>(SetLit), Lit, target, AnimSeconds).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        if (_strip != null)
        {
            _tween.TweenProperty(_strip, "modulate:a", target, AnimSeconds);
        }
    }

    private void SetLit(float value)
    {
        Lit = value;
        if (_strip != null)
        {
            _strip.Position = new Vector2(-12.0f + Slide * value, 0.0f);
        }
        QueueRedraw();
    }

    /// <summary>A quick dip when clicked.</summary>
    private void Press()
    {
        PivotOffset = new Vector2(0.0f, Size.Y * 0.5f);
        Tween press = CreateTween();
        press.TweenProperty(this, "scale", new Vector2(0.96f, 0.96f), 0.05);
        press.TweenProperty(this, "scale", Vector2.One, 0.12).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    public override void _Draw()
    {
        Font font = UiStyle.Heading;
        Vector2 text = font.GetStringSize(Text, HorizontalAlignment.Left, -1, FontSize);
        float x = Slide * Lit + 6.0f;
        float body = Size.Y;
        if (Detail.Length > 0)
        {
            body = Size.Y - DetailSize * 1.5f;
        }
        float y = (body + font.GetAscent(FontSize) - font.GetDescent(FontSize)) * 0.5f;
        Color colour = UiStyle.Paper.Lerp(UiStyle.Ink, Lit);
        if (Disabled)
        {
            colour = new Color(UiStyle.PaperDark, 0.45f);
        }
        if (Lit < 0.5f && !Disabled)
        {
            // A soft shadow keeps paper-coloured text readable over bright snow and sky.
            DrawString(font, new Vector2(x + 2.0f, y + 2.0f), Text, HorizontalAlignment.Left, -1, FontSize, new Color(0, 0, 0, 0.55f * (1.0f - Lit)));
        }
        DrawString(font, new Vector2(x, y), Text, HorizontalAlignment.Left, -1, FontSize, colour);
        if (Detail.Length > 0)
        {
            Font hand = UiStyle.Hand;
            float dy = body + DetailSize * 0.75f;
            Color detail = new Color(UiStyle.Paper, 0.8f).Lerp(new Color(UiStyle.Ink, 0.85f), Lit);
            if (Lit < 0.5f)
            {
                DrawString(hand, new Vector2(x + 2.0f + 1.5f, dy + 1.5f), Detail, HorizontalAlignment.Left, -1, DetailSize, new Color(0, 0, 0, 0.6f * (1.0f - Lit)));
            }
            DrawString(hand, new Vector2(x + 2.0f, dy), Detail, HorizontalAlignment.Left, -1, DetailSize, detail);
        }
        if (Lit > 0.01f)
        {
            // An inked tick in front of the focused entry.
            float tick = x - 2.0f;
            Vector2[] mark = { new(tick - 12.0f * Lit, y - text.Y * 0.32f), new(tick - 3.0f, y - text.Y * 0.18f), new(tick - 12.0f * Lit, y - text.Y * 0.04f) };
            DrawPolyline(InkDraw.Wobble(mark, 0.6f, Text.Length), new Color(UiStyle.Ink, Lit), 2.5f, true);
        }
    }
}
