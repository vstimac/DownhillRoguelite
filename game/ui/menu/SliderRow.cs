using System;
using System.Globalization;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A setting on a scale: an inked track with a filled part and a blot for the handle, the value
/// written beside it. Left/right steps it; the mouse clicks or drags it. Live rows hand every step
/// to <see cref="Changed"/> (volumes, so you hear them); the others commit when let go (the
/// interface size, so the slider doesn't move under the mouse while it rescales).
/// </summary>
public partial class SliderRow : OptionRow
{
    public double Min { get; set; }
    public double Max { get; set; } = 1.0;
    public double StepSize { get; set; } = 0.05;
    public double Value { get; set; }
    /// <summary>string.Format pattern for the shown value, e.g. "{0:0}°".</summary>
    public string Format { get; set; } = "{0:0.00}";
    /// <summary>Shown values are multiplied by this first (0–1 volumes shown as 0–100 %).</summary>
    public double ShowScale { get; set; } = 1.0;
    public bool Live { get; set; }
    public Action<double>? Changed { get; set; }

    private const float ValueWidth = 64.0f;
    private bool _dragging;

    public override string ValueText => string.Format(CultureInfo.InvariantCulture, Format, Value * ShowScale);

    protected override bool RepeatSteps => true;

    public override bool Fits()
    {
        string widest = string.Format(CultureInfo.InvariantCulture, Format, Max * ShowScale);
        return UiStyle.Hand.GetStringSize(widest, HorizontalAlignment.Left, -1, FontSize).X <= ValueWidth && Track().Size.X >= 60.0f && base.Fits();
    }

    /// <summary>The share of the way from Min to Max, 0–1.</summary>
    public float Fraction
    {
        get
        {
            if (Max <= Min)
            {
                return 0.0f;
            }
            return (float)((Value - Min) / (Max - Min));
        }
    }

    public override void Step(int direction)
    {
        SetValue(Value + StepSize * direction, true);
    }

    protected override void Activate()
    {
    }

    private void SetValue(double value, bool commit)
    {
        double snapped = Mathf.Snapped(Mathf.Clamp(value, Min, Max), StepSize);
        snapped = Mathf.Clamp(snapped, Min, Max);
        bool moved = !Mathf.IsEqualApprox(snapped, Value);
        Value = snapped;
        if ((moved && Live) || commit)
        {
            Changed?.Invoke(Value);
        }
        QueueRedraw();
    }

    private Rect2 Track()
    {
        Rect2 box = ValueRect();
        return new Rect2(box.Position.X + 8.0f, Size.Y * 0.5f - 3.0f, box.Size.X - ValueWidth - 16.0f, 6.0f);
    }

    private double ValueAt(float x)
    {
        Rect2 track = Track();
        float t = Mathf.Clamp((x - track.Position.X) / Mathf.Max(track.Size.X, 1.0f), 0.0f, 1.0f);
        return Min + (Max - Min) * t;
    }

    protected override void RowInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton click && click.ButtonIndex == MouseButton.Left)
        {
            if (click.Pressed)
            {
                _dragging = true;
                SetValue(ValueAt(click.Position.X), false);
            }
            else if (_dragging)
            {
                _dragging = false;
                SetValue(ValueAt(click.Position.X), true);
            }
            AcceptEvent();
        }
        else if (@event is InputEventMouseMotion motion && _dragging)
        {
            SetValue(ValueAt(motion.Position.X), false);
            AcceptEvent();
        }
    }

    protected override void DrawValue()
    {
        Rect2 track = Track();
        Color ink = UiStyle.Ink;
        DrawRect(track, new Color(UiStyle.Ink, 0.18f));
        DrawRect(new Rect2(track.Position, new Vector2(track.Size.X * Fraction, track.Size.Y)), new Color(UiStyle.Ink, 0.75f));
        // Tick marks every quarter.
        for (int i = 0; i <= 4; i++)
        {
            float x = track.Position.X + track.Size.X * i * 0.25f;
            DrawLine(new Vector2(x, track.End.Y + 3.0f), new Vector2(x, track.End.Y + 7.0f), new Color(UiStyle.Ink, 0.35f), 1.5f);
        }
        Vector2 handle = new(track.Position.X + track.Size.X * Fraction, track.Position.Y + track.Size.Y * 0.5f);
        float radius = 7.0f + 2.0f * Lit;
        InkDraw.Shape(this, InkDraw.Circle(handle, radius, 14), UiStyle.Paper, 2.5f, 9);
        DrawCircle(handle, radius * 0.38f, ink);
        Font font = UiStyle.Hand;
        DrawString(font, new Vector2(track.End.X + 12.0f, Baseline(FontSize)), ValueText, HorizontalAlignment.Left, ValueWidth, FontSize, ink);
    }
}
