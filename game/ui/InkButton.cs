using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A button in the expedition look: plain text that, under the mouse or with focus, gains a light
/// band with an amber rule at its left; a primary button is filled light with dark letters. It
/// presses down a touch when clicked. Shows a text label, or a glyph (close cross, page arrows).
/// (Named for the inked journal style it replaced.)
/// </summary>
public partial class InkButton : BaseButton
{
    public enum GlyphKind
    {
        None,
        Cross,
        Left,
        Right,
    }

    public string Text { get; set; } = "";
    public GlyphKind Glyph { get; set; }
    public int FontSize { get; set; } = UiStyle.TextSize;
    public bool Paper { get; set; }
    /// <summary>Takes keyboard and controller focus (menus); focus looks like the mouse hovering it.</summary>
    public bool Focusable { get; set; }
    /// <summary>The one action a card wants (paper buttons only): drawn inked in, with paper letters.</summary>
    public bool Primary { get; set; }

    private Tween? _tween;
    private bool _hover;

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.None;
        if (Focusable)
        {
            FocusMode = FocusModeEnum.All;
            MouseEntered += () => GrabFocus();
            FocusEntered += () => Hover(true);
            FocusExited += () => Hover(false);
        }
        else
        {
            MouseEntered += () => Hover(true);
            MouseExited += () => Hover(false);
        }
        ButtonDown += () => Bounce(0.88f, 0.06);
        ButtonUp += () => Bounce(HoverScale(), 0.12);
        Resized += () => PivotOffset = Size * 0.5f;
        PivotOffset = Size * 0.5f;
    }

    private float HoverScale()
    {
        if (_hover)
        {
            return 1.02f;
        }
        return 1.0f;
    }

    private void Hover(bool on)
    {
        _hover = on;
        Bounce(HoverScale(), 0.12);
        QueueRedraw();
    }

    private void Bounce(float scale, double time)
    {
        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(this, "scale", Vector2.One * scale, time).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    public override void _Draw()
    {
        Vector2 c = Size * 0.5f;
        float s = Mathf.Min(Size.X, Size.Y) * 0.26f;
        Color ink = UiStyle.Ink;
        if (_hover)
        {
            ink = UiStyle.Highlight;
        }
        if (Glyph == GlyphKind.Cross)
        {
            DrawLine(c + new Vector2(-s, -s), c + new Vector2(s, s), ink, 2.0f, true);
            DrawLine(c + new Vector2(s, -s), c + new Vector2(-s, s), ink, 2.0f, true);
            return;
        }
        if (Glyph == GlyphKind.Left || Glyph == GlyphKind.Right)
        {
            float dir = Glyph == GlyphKind.Left ? -1.0f : 1.0f;
            Vector2 tip = c + new Vector2(dir * s * 0.6f, 0);
            DrawPolyline(new[] { c + new Vector2(-dir * s * 0.6f, -s), tip, c + new Vector2(-dir * s * 0.6f, s) }, ink, 2.0f, true);
            return;
        }
        Font font = UiStyle.Hand;
        Vector2 size = font.GetStringSize(Text, HorizontalAlignment.Left, -1, FontSize);
        Color letters = UiStyle.Ink;
        Rect2 band = new(new Vector2(2.0f, 3.0f), Size - new Vector2(4.0f, 6.0f));
        if (Paper && Primary)
        {
            // Filled light, so it reads as the button to press before anything is focused.
            DrawRect(band, _hover ? UiStyle.Highlight : UiStyle.Ink);
            letters = UiStyle.Paper;
        }
        else
        {
            if (Paper)
            {
                // A paper button still shows its edge at rest, so it looks pressable.
                DrawRect(band, new Color(UiStyle.PaperDark, 0.55f));
                DrawRect(band, new Color(UiStyle.Line, 0.9f), false, 1.0f);
            }
            if (_hover)
            {
                // Mouse and keyboard focus read the same, at a glance: a light band and an amber rule.
                DrawRect(band, new Color(UiStyle.Ink, 0.12f));
                DrawRect(new Rect2(band.Position, new Vector2(2.0f, band.Size.Y)), UiStyle.Highlight);
            }
        }
        DrawString(font, new Vector2((Size.X - size.X) * 0.5f, (Size.Y + size.Y * 0.55f) * 0.5f), Text, HorizontalAlignment.Left, -1, FontSize, letters);
        if (Disabled)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(UiStyle.Paper, 0.6f));
        }
    }
}
