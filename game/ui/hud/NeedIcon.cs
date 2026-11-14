using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A small survival icon under the hearts (hunger, thirst, warmth): an inked icon on a paper disc
/// with a thin ring for the value. While the need is fine it stays small and faded so it doesn't
/// ask for a look; as it nears the attention level it grows and turns solid, and below the warning
/// level it pulses red. With Options: bars the ring becomes a straight bar under the icon.
/// Presentation only: Hud feeds it.
/// </summary>
public partial class NeedIcon : Control
{
    public InkIcon Icon { get; set; }
    public Color RingColour { get; set; } = UiStyle.Hunger;
    /// <summary>Below this share the icon starts to grow and turn solid.</summary>
    [Export] public float AttentionBelow { get; set; } = 0.5f;
    /// <summary>Below this share it pulses red.</summary>
    [Export] public float WarnBelow { get; set; } = 0.25f;
    [Export] public float IconSize { get; set; } = 44.0f;
    /// <summary>How see-through it is while the need is fine.</summary>
    [Export] public float CalmAlpha { get; set; } = 0.6f;
    /// <summary>Scale while the need is fine, and at the warning level.</summary>
    [Export] public float CalmScale { get; set; } = 0.8f;
    [Export] public float UrgentScale { get; set; } = 1.12f;
    [Export] public float PulseSpeed { get; set; } = 7.0f;
    [Export] public float PulseAmount { get; set; } = 0.07f;
    [Export] public float EaseRate { get; set; } = 6.0f;

    public float Value { get; private set; }
    public float Max { get; private set; } = 100.0f;
    /// <summary>The value the ring shows right now (easing toward Value).</summary>
    public float Shown { get; private set; } = -1.0f;
    public bool Warning => Max > 0.0f && Value / Max < WarnBelow;
    /// <summary>0 while the need is fine, rising to 1 at the warning level: how much it asks for a look.</summary>
    public float Attention
    {
        get
        {
            float share = Value / Mathf.Max(Max, 0.001f);
            return Mathf.Clamp((AttentionBelow - share) / Mathf.Max(AttentionBelow - WarnBelow, 0.001f), 0.0f, 1.0f);
        }
    }

    private float _time;
    private float _attention;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(IconSize, IconSize);
        CustomMinimumSize = Size;
        PivotOffset = Size * 0.5f;
    }

    /// <summary>Feeds the real value; changes ease.</summary>
    public void SetValue(float value, float max)
    {
        Value = value;
        Max = Mathf.Max(max, 0.001f);
        if (Shown < 0.0f)
        {
            Shown = value;
            _attention = Attention;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        float ease = 1.0f - Mathf.Exp(-EaseRate * dt);
        if (Shown >= 0.0f)
        {
            Shown = Mathf.Lerp(Shown, Value, ease);
        }
        _attention = Mathf.Lerp(_attention, Attention, ease);
        float scale = Mathf.Lerp(CalmScale, UrgentScale, _attention);
        if (Warning)
        {
            scale *= 1.0f + PulseAmount * Mathf.Sin(_time * PulseSpeed);
        }
        Scale = Vector2.One * scale;
        Modulate = new Color(1, 1, 1, Mathf.Lerp(CalmAlpha, 1.0f, _attention));
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 c = Size * 0.5f;
        float radius = Size.X * 0.5f - 3.0f;
        float fraction = Mathf.Clamp(Shown / Max, 0.0f, 1.0f);
        Color ring = RingColour;
        if (Warning)
        {
            ring = ring.Lerp(UiStyle.Warning, 0.5f + 0.5f * Mathf.Sin(_time * PulseSpeed));
        }
        InkDraw.Shape(this, InkDraw.Circle(c, radius - 3.0f, 22), UiStyle.Paper, 2.0f, (int)Icon + 30);
        float inset = Size.X * 0.24f;
        InkDraw.Icon(this, Icon, new Rect2(new Vector2(inset, inset), Size - new Vector2(inset, inset) * 2.0f), (int)Icon + 2);
        if (GameOptions.Stats == StatStyle.Bars)
        {
            Rect2 track = new(new Vector2(2.0f, Size.Y - 7.0f), new Vector2(Size.X - 4.0f, 5.0f));
            DrawRect(track.Grow(1.5f), UiStyle.Ink);
            DrawRect(new Rect2(track.Position, new Vector2(track.Size.X * fraction, track.Size.Y)), ring);
            return;
        }
        InkDraw.Arc(this, c, radius, 0.0f, Mathf.Tau, 5.0f, new Color(0.1f, 0.08f, 0.06f));
        if (fraction > 0.001f)
        {
            InkDraw.Arc(this, c, radius, 0.0f, Mathf.Tau * fraction, 3.5f, ring);
        }
    }
}
