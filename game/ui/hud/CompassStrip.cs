using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The compass as a thin strip along the top of the screen: a tape of ticks and letters that
/// slides with the view, so what is straight ahead sits under the notch in the middle. North is
/// red; the nearest waypoint (or the next station) is a red diamond on the tape, pinned to an end
/// when it lies behind; a blue feather under the tape shows where the wind blows, longer the
/// stronger it is. The round compass (CompassDial) stays on the map page.
/// </summary>
public partial class CompassStrip : Control
{
    /// <summary>Real north's angle relative to the view (0 = straight ahead), radians.</summary>
    public float NorthAngle { get; private set; }
    /// <summary>Direction of the nearest waypoint relative to the view, radians; NaN when none.</summary>
    public float WaypointAngle { get; set; } = float.NaN;
    /// <summary>Where the wind blows toward, relative to the view (0 = straight ahead), radians.</summary>
    public float WindAngle { get; private set; }
    public float WindSpeed { get; private set; }

    [Export] public Vector2 StripSize { get; set; } = new(380.0f, 34.0f);
    /// <summary>How much of the horizon the strip shows, in degrees.</summary>
    [Export] public float SpanDegrees { get; set; } = 150.0f;
    /// <summary>A tick every this many degrees; every third one is long.</summary>
    [Export] public float TickDegrees { get; set; } = 15.0f;
    /// <summary>The wind feather is full length at this wind speed, in m/s.</summary>
    [Export] public float FullWind { get; set; } = 15.0f;
    /// <summary>No feather below this wind speed, in m/s.</summary>
    [Export] public float CalmBelow { get; set; } = 1.5f;
    [Export] public Color Back { get; set; } = new(0.86f, 0.79f, 0.64f, 0.82f);
    [Export] public Color WindColour { get; set; } = new(0.2f, 0.42f, 0.7f, 0.95f);

    private float _shown;
    private float _windShown;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = StripSize;
        CustomMinimumSize = StripSize;
    }

    public override void _Process(double delta)
    {
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera != null)
        {
            Vector3 forward = -camera.GlobalBasis.Z;
            forward.Y = 0.0f;
            if (!forward.IsZeroApprox())
            {
                forward = forward.Normalized();
                Vector3 right = forward.Cross(Vector3.Up);
                NorthAngle = Mathf.Atan2(Vector3.Forward.Dot(right), Vector3.Forward.Dot(forward));
                Vector3 wind = GameEvents.CurrentWeather.Wind;
                wind.Y = 0.0f;
                WindSpeed = wind.Length();
                if (WindSpeed > 0.01f)
                {
                    WindAngle = Mathf.Atan2(wind.Dot(right), wind.Dot(forward));
                }
            }
        }
        // The tape swings a little behind the view, like a needle settling.
        _shown = Mathf.LerpAngle(_shown, NorthAngle, 1.0f - Mathf.Exp(-8.0f * (float)delta));
        _windShown = Mathf.LerpAngle(_windShown, WindAngle, 1.0f - Mathf.Exp(-5.0f * (float)delta));
        QueueRedraw();
    }

    /// <summary>Where a direction (relative to the view, radians) falls along the strip, in px from its left; outside 0..width when off the tape.</summary>
    public float TapeX(float angle)
    {
        float half = Mathf.DegToRad(SpanDegrees) * 0.5f;
        return Size.X * 0.5f + Mathf.Wrap(angle, -Mathf.Pi, Mathf.Pi) / half * (Size.X * 0.5f);
    }

    public override void _Draw()
    {
        float w = Size.X;
        float h = Size.Y;
        float pad = 10.0f;
        // A paper band with inked lines above and below, torn short at the ends.
        DrawRect(new Rect2(new Vector2(0.0f, 4.0f), new Vector2(w, h - 8.0f)), Back);
        DrawLine(new Vector2(0.0f, 4.0f), new Vector2(w, 4.0f), UiStyle.Ink, 2.0f, true);
        DrawLine(new Vector2(0.0f, h - 4.0f), new Vector2(w, h - 4.0f), UiStyle.Ink, 2.0f, true);

        Font font = UiStyle.Heading;
        string[] marks = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        int ticks = Mathf.RoundToInt(360.0f / Mathf.Max(TickDegrees, 1.0f));
        for (int i = 0; i < ticks; i++)
        {
            float bearing = Mathf.DegToRad(i * TickDegrees);
            float x = TapeX(_shown + bearing);
            if (x < pad || x > w - pad)
            {
                continue;
            }
            // Ticks and letters thin out toward the ends.
            float edge = Mathf.Clamp(Mathf.Min(x - pad, w - pad - x) / 40.0f, 0.0f, 1.0f);
            float degrees = i * TickDegrees;
            int eighth = Mathf.RoundToInt(degrees / 45.0f);
            if (Mathf.IsEqualApprox(eighth * 45.0f, degrees))
            {
                string mark = marks[eighth % 8];
                int size = 15;
                Color ink = UiStyle.Ink;
                if (eighth % 2 == 0)
                {
                    size = 20;
                }
                if (eighth % 8 == 0)
                {
                    ink = UiStyle.Warning.Darkened(0.2f);
                }
                Vector2 text = font.GetStringSize(mark, HorizontalAlignment.Left, -1, size);
                DrawString(font, new Vector2(x - text.X * 0.5f, h * 0.5f + text.Y * 0.28f), mark, HorizontalAlignment.Left, -1, size, new Color(ink, edge));
            }
            else
            {
                DrawLine(new Vector2(x, h * 0.5f - 4.0f), new Vector2(x, h * 0.5f + 4.0f), new Color(UiStyle.Ink, 0.6f * edge), 1.5f);
            }
        }

        if (WindSpeed >= CalmBelow)
        {
            // The wind: a feather under the tape at the bearing it blows toward, pointing the way it pushes across the view.
            float x = Mathf.Clamp(TapeX(_windShown), pad + 8.0f, w - pad - 8.0f);
            float length = Mathf.Lerp(8.0f, 22.0f, Mathf.Clamp(WindSpeed / FullWind, 0.0f, 1.0f));
            float side = 1.0f;
            if (Mathf.Wrap(_windShown, -Mathf.Pi, Mathf.Pi) < 0.0f)
            {
                side = -1.0f;
            }
            Vector2 tail = new(x - side * length * 0.5f, h - 4.0f);
            Vector2 tip = new(x + side * length * 0.5f, h - 4.0f);
            DrawLine(tail, tip, WindColour, 3.0f, true);
            DrawColoredPolygon(new[] { tip + new Vector2(side * 5.0f, 0.0f), tip + new Vector2(-side * 2.0f, -4.5f), tip + new Vector2(-side * 2.0f, 4.5f) }, WindColour);
        }

        if (!float.IsNaN(WaypointAngle))
        {
            // The way to go: a red diamond on the tape, pinned to an end when it is out of view.
            float x = Mathf.Clamp(TapeX(WaypointAngle), pad, w - pad);
            Vector2 at = new(x, 4.0f);
            InkDraw.Shape(this, new[] { at + new Vector2(0, -5.5f), at + new Vector2(5.5f, 0), at + new Vector2(0, 5.5f), at + new Vector2(-5.5f, 0) }, UiStyle.Warning, 1.5f, 64);
        }

        // The notch: straight ahead.
        Vector2 top = new(w * 0.5f, h - 5.0f);
        DrawColoredPolygon(new[] { top + new Vector2(0, -6.0f), top + new Vector2(5.0f, 3.0f), top + new Vector2(-5.0f, 3.0f) }, UiStyle.Ink);
    }
}
