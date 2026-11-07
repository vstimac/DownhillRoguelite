using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A round inked compass by the descent panel. The card turns with the view, so the N always points
/// to real north (-Z) and the carrier's heading is straight up; the nearest waypoint, if any, shows
/// as a small red mark on the rim. A blue arrow shows where the wind blows, longer the stronger it is.
/// </summary>
public partial class CompassDial : Control
{
    /// <summary>Real north's angle relative to the view (0 = straight ahead), radians.</summary>
    public float NorthAngle { get; private set; }
    /// <summary>Direction of the nearest waypoint relative to the view, radians; NaN when none.</summary>
    public float WaypointAngle { get; set; } = float.NaN;

    /// <summary>Where the wind blows toward, relative to the view (0 = straight ahead), radians.</summary>
    public float WindAngle { get; private set; }
    public float WindSpeed { get; private set; }
    /// <summary>The arrow is full length at this wind speed, in m/s.</summary>
    [Export] public float FullWind { get; set; } = 15.0f;
    /// <summary>No arrow below this wind speed, in m/s.</summary>
    [Export] public float CalmBelow { get; set; } = 1.5f;

    private float _shown;
    private float _windShown;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(130, 130);
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
                Vector3 wind = Core.GameEvents.CurrentWeather.Wind;
                wind.Y = 0.0f;
                WindSpeed = wind.Length();
                if (WindSpeed > 0.01f)
                {
                    WindAngle = Mathf.Atan2(wind.Dot(right), wind.Dot(forward));
                }
            }
        }
        // The card swings a little behind the view, like a real needle settling.
        _shown = Mathf.LerpAngle(_shown, NorthAngle, 1.0f - Mathf.Exp(-8.0f * (float)delta));
        _windShown = Mathf.LerpAngle(_windShown, WindAngle, 1.0f - Mathf.Exp(-5.0f * (float)delta));
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 c = Size * 0.5f;
        float r = Mathf.Min(Size.X, Size.Y) * 0.5f - 6.0f;
        InkDraw.Shape(this, InkDraw.Circle(c, r + 4.0f, 32), new Color(0.24f, 0.18f, 0.12f), 3.0f, 61);
        InkDraw.Shape(this, InkDraw.Circle(c, r - 4.0f, 32), UiStyle.Paper, 2.0f, 62);
        Font font = UiStyle.Heading;
        string[] marks = { "N", "E", "S", "W" };
        for (int i = 0; i < 4; i++)
        {
            float a = -_shown + i * Mathf.Pi * 0.5f - Mathf.Pi * 0.5f;
            Vector2 d = new(Mathf.Cos(a), Mathf.Sin(a));
            int size = 18;
            Color ink = UiStyle.Ink;
            if (i == 0)
            {
                size = 24;
                ink = UiStyle.Warning.Darkened(0.2f);
            }
            Vector2 text = font.GetStringSize(marks[i], HorizontalAlignment.Left, -1, size);
            DrawString(font, c + d * (r - 18.0f) - new Vector2(text.X * 0.5f, -text.Y * 0.3f), marks[i], HorizontalAlignment.Left, -1, size, ink);
            DrawLine(c + d * (r - 6.0f), c + d * (r - 2.0f), UiStyle.Ink, 2.0f);
        }
        if (WindSpeed >= CalmBelow)
        {
            // The wind: a blue arrow through the middle toward where it blows, with two streaks behind.
            float length = r * Mathf.Lerp(0.3f, 0.75f, Mathf.Clamp(WindSpeed / FullWind, 0.0f, 1.0f));
            float a = _windShown - Mathf.Pi * 0.5f;
            Vector2 d = new(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 side = new(-d.Y, d.X);
            Color blue = new(0.2f, 0.42f, 0.7f, 0.9f);
            Vector2 tail = c - d * length * 0.6f;
            Vector2 tip = c + d * length;
            DrawLine(tail, tip, blue, 3.0f, true);
            DrawColoredPolygon(new[] { tip + d * 6.0f, tip - d * 4.0f + side * 6.0f, tip - d * 4.0f - side * 6.0f }, blue);
            DrawLine(tail + side * 6.0f, tail + side * 6.0f + d * length * 0.35f, new Color(blue, 0.6f), 2.0f, true);
            DrawLine(tail - side * 6.0f, tail - side * 6.0f + d * length * 0.35f, new Color(blue, 0.6f), 2.0f, true);
        }
        // Star in the middle and the heading pointer.
        InkDraw.Shape(this, new[] { c + new Vector2(0, -r * 0.42f), c + new Vector2(r * 0.13f, 0), c + new Vector2(0, r * 0.2f), c + new Vector2(-r * 0.13f, 0) }, new Color(0.85f, 0.82f, 0.74f), 2.0f, 63);
        if (!float.IsNaN(WaypointAngle))
        {
            float a = WaypointAngle - Mathf.Pi * 0.5f;
            Vector2 d = new(Mathf.Cos(a), Mathf.Sin(a));
            InkDraw.Shape(this, InkDraw.Circle(c + d * (r - 4.0f), 5.0f, 10), UiStyle.Warning, 1.5f, 64);
        }
    }
}
