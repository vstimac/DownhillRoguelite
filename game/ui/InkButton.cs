using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A button drawn in ink on a scrap of parchment: it leans and grows a touch under the mouse and
/// presses down physically when clicked. Shows a text label, or a glyph (close cross, page arrows).
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
        if (Paper && Glyph == GlyphKind.None)
        {
            ColorRect paper = new() { MouseFilter = MouseFilterEnum.Ignore, ShowBehindParent = true };
            AddChild(paper);
            Resized += () =>
            {
                paper.Size = Size;
                paper.Material = UiStyle.Parchment(Size, Text.Length, 2.5f, 2.5f);
            };
        }
        PivotOffset = Size * 0.5f;
    }

    private float HoverScale()
    {
        if (_hover)
        {
            return 1.06f;
        }
        return 1.0f;
    }

    private void Hover(bool on)
    {
        _hover = on;
        Bounce(HoverScale(), 0.12);
        float lean = 0.0f;
        if (on)
        {
            lean = Mathf.DegToRad(-2.0f);
        }
        _tween?.TweenProperty(this, "rotation", lean, 0.12);
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
        float s = Mathf.Min(Size.X, Size.Y) * 0.32f;
        Color ink = UiStyle.Ink;
        if (_hover)
        {
            ink = new Color(0.45f, 0.18f, 0.05f);
        }
        if (Glyph == GlyphKind.Cross)
        {
            DrawPolyline(InkDraw.Wobble(new[] { c + new Vector2(-s, -s), c + new Vector2(s, s) }, 1.0f, 3), ink, 3.5f, true);
            DrawPolyline(InkDraw.Wobble(new[] { c + new Vector2(s, -s), c + new Vector2(-s, s) }, 1.0f, 4), ink, 3.5f, true);
            return;
        }
        if (Glyph == GlyphKind.Left || Glyph == GlyphKind.Right)
        {
            float dir = 1.0f;
            if (Glyph == GlyphKind.Left)
            {
                dir = -1.0f;
            }
            InkDraw.Shape(this, new[] { c + new Vector2(dir * s, 0), c + new Vector2(-dir * s * 0.7f, -s), c + new Vector2(-dir * s * 0.7f, s) }, ink, 2.0f, 5);
            return;
        }
        Font font = UiStyle.Hand;
        Vector2 size = font.GetStringSize(Text, HorizontalAlignment.Left, -1, FontSize);
        Color letters = ink;
        if (Paper && Primary)
        {
            // Inked in, so it reads as the button to press before anything is focused.
            Color fill = new(UiStyle.Ink, 0.9f);
            if (_hover)
            {
                fill = new Color(0.45f, 0.18f, 0.05f, 0.95f);
            }
            DrawRect(new Rect2(new Vector2(5.0f, 5.0f), Size - new Vector2(10.0f, 10.0f)), fill);
            letters = UiStyle.Paper;
        }
        else if (Paper && Focusable && !_hover)
        {
            // At rest a paper button still shows its edge, so it looks pressable.
            Vector2 a0 = new(4.0f, 4.0f);
            Vector2 b0 = Size - new Vector2(4.0f, 4.0f);
            DrawPolyline(InkDraw.Wobble(new[] { a0, new(b0.X, a0.Y), b0, new(a0.X, b0.Y), a0 }, 0.6f, Text.Length + 5), new Color(ink, 0.45f), 1.5f, true);
        }
        DrawString(font, new Vector2((Size.X - size.X) * 0.5f, (Size.Y + size.Y * 0.55f) * 0.5f), Text, HorizontalAlignment.Left, -1, FontSize, letters);
        if (_hover)
        {
            DrawPolyline(InkDraw.Wobble(new[] { new Vector2((Size.X - size.X) * 0.5f, Size.Y * 0.78f), new Vector2((Size.X + size.X) * 0.5f, Size.Y * 0.78f) }, 1.0f, 7), letters, 2.0f, true);
            if (Paper && Focusable)
            {
                // Keyboard and controller focus must read at a glance, not only as a colour change: an inked frame.
                Vector2 a = new(3.0f, 3.0f);
                Vector2 b = Size - new Vector2(3.0f, 3.0f);
                Vector2[] frame = { a, new(b.X, a.Y), b, new(a.X, b.Y), a };
                DrawPolyline(InkDraw.Wobble(frame, 0.8f, Text.Length + 11), ink, 2.0f, true);
            }
        }
        if (Disabled)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(UiStyle.Paper, 0.55f));
        }
    }
}
