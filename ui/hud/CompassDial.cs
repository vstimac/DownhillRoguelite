using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A round inked compass by the descent panel. The card turns with the view, so the N always points
/// to real north (-Z) and the carrier's heading is straight up; the nearest waypoint, if any, shows
/// as a small red mark on the rim.
/// </summary>
public partial class CompassDial : Control
{
    /// <summary>Real north's angle relative to the view (0 = straight ahead), radians.</summary>
    public float NorthAngle { get; private set; }
    /// <summary>Direction of the nearest waypoint relative to the view, radians; NaN when none.</summary>
    public float WaypointAngle { get; set; } = float.NaN;

    private float _shown;

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
            }
        }
        // The card swings a little behind the view, like a real needle settling.
        _shown = Mathf.LerpAngle(_shown, NorthAngle, 1.0f - Mathf.Exp(-8.0f * (float)delta));
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
