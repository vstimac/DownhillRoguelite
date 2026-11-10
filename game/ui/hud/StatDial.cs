using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A survival dial: a parchment disc with a hand-inked rim, an illustrated icon in the middle and a
/// coloured ring around it showing the value. The ring eases toward the real value instead of
/// jumping; a drop gives it a small jolt and a red flash, a rise a soft glow; below the warning
/// level the whole dial pulses and the rim turns red. The value is written on a paper tag below.
/// </summary>
public partial class StatDial : Control
{
    public InkIcon Icon { get; set; }
    public Color RingColour { get; set; } = UiStyle.Health;
    public float WarnBelow { get; set; } = 0.25f;
    public float Value { get; private set; }
    public float Max { get; private set; } = 100.0f;
    /// <summary>The value the ring shows right now (easing toward Value).</summary>
    public float Shown { get; private set; } = -1.0f;
    public bool Warning => Max > 0.0f && Value / Max < WarnBelow;

    private float _time;
    private float _flash;
    private Color _flashColour;
    private float _jolt;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(96, 116);
        PivotOffset = new Vector2(48, 48);
    }

    /// <summary>Feeds the real value; changes animate.</summary>
    public void SetValue(float value, float max)
    {
        float before = Value;
        Value = value;
        Max = Mathf.Max(max, 0.001f);
        if (Shown < 0.0f)
        {
            Shown = value;
            return;
        }
        float change = (value - before) / Max;
        if (change < -0.02f)
        {
            _flash = 1.0f;
            _flashColour = UiStyle.Warning;
            _jolt = Mathf.Min(1.0f, -change * 8.0f);
        }
        else if (change > 0.05f)
        {
            _flash = 1.0f;
            _flashColour = UiStyle.Highlight;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        Shown = Mathf.Lerp(Shown, Value, 1.0f - Mathf.Exp(-6.0f * dt));
        _flash = Mathf.MoveToward(_flash, 0.0f, dt * 2.0f);
        _jolt = Mathf.MoveToward(_jolt, 0.0f, dt * 3.0f);
        float pulse = 1.0f;
        if (Warning)
        {
            pulse = 1.0f + 0.06f * Mathf.Sin(_time * 7.0f);
        }
        Scale = Vector2.One * pulse;
        Rotation = Mathf.Sin(_time * 40.0f) * 0.06f * _jolt;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (GameOptions.Stats == StatStyle.Bars)
        {
            DrawBar();
            DrawTag();
            return;
        }
        Vector2 c = new(48, 48);
        float radius = 40.0f;
        // Disc and rim.
        InkDraw.Shape(this, InkDraw.Circle(c, radius + 3.0f, 30), new Color(0.22f, 0.17f, 0.12f), 3.0f, (int)Icon + 20);
        InkDraw.Shape(this, InkDraw.Circle(c, radius - 7.0f, 28), UiStyle.Paper, 2.0f, (int)Icon + 30);
        // The ring: a dark track, then the value.
        float fraction = Mathf.Clamp(Shown / Max, 0.0f, 1.0f);
        InkDraw.Arc(this, c, radius - 2.5f, 0.0f, Mathf.Tau, 7.0f, new Color(0.1f, 0.08f, 0.06f));
        Color ring = RingColour;
        if (Warning)
        {
            ring = ring.Lerp(UiStyle.Warning, 0.5f + 0.5f * Mathf.Sin(_time * 7.0f));
        }
        if (fraction > 0.001f)
        {
            InkDraw.Arc(this, c, radius - 2.5f, 0.0f, Mathf.Tau * fraction, 6.0f, ring);
        }
        if (_flash > 0.0f)
        {
            InkDraw.Arc(this, c, radius + 4.0f, 0.0f, Mathf.Tau, 3.0f, new Color(_flashColour, _flash * 0.8f));
        }
        // Little tick marks like a hand-drawn gauge.
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8.0f - Mathf.Pi * 0.5f;
            Vector2 d = new(Mathf.Cos(a), Mathf.Sin(a));
            DrawLine(c + d * (radius - 7.0f), c + d * (radius - 10.0f), UiStyle.InkSoft, 1.5f);
        }
        InkDraw.Icon(this, Icon, new Rect2(c - new Vector2(22, 22), new Vector2(44, 44)), (int)Icon + 2);
        DrawTag();
    }

    /// <summary>The plain look (Options: bars): the icon over a straight 0-100 bar.</summary>
    private void DrawBar()
    {
        InkDraw.Icon(this, Icon, new Rect2(new Vector2(30, 8), new Vector2(36, 36)), (int)Icon + 2);
        Rect2 track = new(new Vector2(6, 54), new Vector2(84, 18));
        DrawRect(track.Grow(2.0f), new Color(0.22f, 0.17f, 0.12f));
        DrawRect(track, new Color(0.1f, 0.08f, 0.06f));
        float fraction = Mathf.Clamp(Shown / Max, 0.0f, 1.0f);
        Color fill = RingColour;
        if (Warning)
        {
            fill = fill.Lerp(UiStyle.Warning, 0.5f + 0.5f * Mathf.Sin(_time * 7.0f));
        }
        DrawRect(new Rect2(track.Position, new Vector2(track.Size.X * fraction, track.Size.Y)), fill);
        if (_flash > 0.0f)
        {
            DrawRect(track.Grow(3.0f), new Color(_flashColour, _flash * 0.8f), false, 2.0f);
        }
    }

    /// <summary>The value written on a paper tag below.</summary>
    private void DrawTag()
    {
        string text = $"{Mathf.RoundToInt(Value)} / {Mathf.RoundToInt(Max)}";
        Font font = UiStyle.Hand;
        Vector2 size = font.GetStringSize(text, HorizontalAlignment.Left, -1, UiStyle.SmallSize);
        Rect2 tag = new(new Vector2(48 - size.X * 0.5f - 8, 88), new Vector2(size.X + 16, 22));
        InkDraw.Shape(this, new[] { tag.Position, new Vector2(tag.End.X, tag.Position.Y), tag.End, new Vector2(tag.Position.X, tag.End.Y) }, UiStyle.Paper, 2.0f, (int)Icon + 40);
        Color ink = UiStyle.Ink;
        if (Warning)
        {
            ink = UiStyle.Warning.Darkened(0.2f);
        }
        DrawString(font, new Vector2(tag.Position.X + 8, tag.End.Y - 5), text, HorizontalAlignment.Left, -1, UiStyle.SmallSize, ink);
    }
}
