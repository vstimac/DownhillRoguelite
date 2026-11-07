using DownhillRoguelite.Map;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The map's legend, beside the chart on the map page: each symbol drawn the way the chart draws it
/// (in the chart's own inks) with what it means: the carrier, the way to the next station,
/// stations, the summit, waypoints, the three kinds of path, paths ahead not yet seen, pencil
/// lines, contour lines and unexplored ground.
/// </summary>
public partial class MapLegend : Control
{
    public MapCanvas? Canvas { get; set; }

    private static readonly string[] Entries =
    {
        "You (the way you face)",
        "Next station, and how far",
        "Station (fire, lean-to)",
        "The summit",
        "Waypoint (click to set)",
        "Safe path, with cairns",
        "Hazardous path: shorter",
        "Supply-rich path: longer",
        "Path ahead, not yet seen",
        "Your pencil lines",
        "Contour line (10 m)",
        "Unexplored ground",
    };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        Color ink = Canvas?.Ink ?? UiStyle.Ink;
        Color safe = Canvas?.SafeInk ?? new Color(0.35f, 0.25f, 0.15f);
        Color hazard = Canvas?.HazardInk ?? new Color(0.7f, 0.2f, 0.15f);
        Color supply = Canvas?.SupplyInk ?? new Color(0.25f, 0.5f, 0.25f);
        Color waypoint = Canvas?.WaypointInk ?? new Color(0.75f, 0.1f, 0.1f);
        Color graphite = Canvas?.Graphite ?? new Color(0.25f, 0.25f, 0.28f);
        Color contour = Canvas?.Contour ?? new Color(0.5f, 0.42f, 0.3f, 0.6f);
        Color station = Canvas?.StationInk ?? new Color(0.85f, 0.45f, 0.1f);
        Color paper = Canvas?.Paper ?? UiStyle.Paper;
        Color ground = Canvas?.GroundLight ?? new Color(0.78f, 0.74f, 0.6f);
        Font font = UiStyle.Hand;
        DrawString(UiStyle.Heading, new Vector2(0, 22), "Legend", HorizontalAlignment.Left, -1, 24, UiStyle.Ink);
        float row = Mathf.Clamp((Size.Y - 40.0f) / Entries.Length, 22.0f, 34.0f);
        for (int i = 0; i < Entries.Length; i++)
        {
            Vector2 c = new(16, 46 + i * row);
            switch (i)
            {
                case 0:
                    DrawColoredPolygon(new[] { c + new Vector2(0, -10), c + new Vector2(7, 7), c + new Vector2(0, 3), c + new Vector2(-7, 7) }, ink);
                    break;
                case 1:
                    DrawLine(c + new Vector2(-12, 6), c + new Vector2(6, -4), station, 4.0f, true);
                    DrawColoredPolygon(new[] { c + new Vector2(12, -8), c + new Vector2(2, -9), c + new Vector2(7, 0) }, station);
                    break;
                case 2:
                    DrawRect(new Rect2(c - new Vector2(7, 7), new Vector2(14, 14)), ink, false, 3.0f);
                    break;
                case 3:
                    DrawColoredPolygon(new[] { c + new Vector2(0, -9), c + new Vector2(8, 6), c + new Vector2(-8, 6) }, ink);
                    break;
                case 4:
                    DrawLine(c + new Vector2(-7, -7), c + new Vector2(7, 7), waypoint, 3.0f, true);
                    DrawLine(c + new Vector2(-7, 7), c + new Vector2(7, -7), waypoint, 3.0f, true);
                    break;
                case 5:
                    DrawLine(c + new Vector2(-12, 0), c + new Vector2(12, 0), safe, 3.0f, true);
                    break;
                case 6:
                    DrawLine(c + new Vector2(-12, 0), c + new Vector2(12, 0), hazard, 2.0f, true);
                    break;
                case 7:
                    DrawLine(c + new Vector2(-12, 0), c + new Vector2(12, 0), supply, 3.0f, true);
                    break;
                case 8:
                    DrawLine(c + new Vector2(-12, 0), c + new Vector2(-5, 0), new Color(safe, 0.4f), 2.0f, true);
                    DrawLine(c + new Vector2(2, 0), c + new Vector2(9, 0), new Color(safe, 0.4f), 2.0f, true);
                    break;
                case 9:
                    DrawPolyline(new[] { c + new Vector2(-12, 4), c + new Vector2(-4, -5), c + new Vector2(4, 4), c + new Vector2(12, -4) }, graphite, 2.5f, true);
                    break;
                case 10:
                    DrawRect(new Rect2(c - new Vector2(12, 8), new Vector2(24, 16)), ground);
                    DrawLine(c + new Vector2(-12, 0), c + new Vector2(12, 0), contour, 1.5f);
                    break;
                default:
                    DrawRect(new Rect2(c - new Vector2(12, 8), new Vector2(24, 16)), paper);
                    DrawRect(new Rect2(c - new Vector2(12, 8), new Vector2(24, 16)), UiStyle.InkSoft, false, 1.0f);
                    break;
            }
            DrawString(font, c + new Vector2(22, 6), Entries[i], HorizontalAlignment.Left, Size.X - 40, UiStyle.SmallSize, UiStyle.Ink);
        }
    }
}
