using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Map;

/// <summary>
/// Draws the chart onto the map's paper (inside the map's SubViewport, so it ends up on a 3D sheet,
/// not a screen). North (-Z) is up and the carrier is in the middle. Only ground the carrier has
/// seen is drawn: shaded by height with contour lines, the paths that cross it (by kind), stations,
/// the summit; then the waypoints, the pencil drawings, the carrier's arrow and a scale bar.
/// </summary>
public partial class MapCanvas : Control
{
    public FieldMap? Map { get; set; }

    [Export] public Color Paper { get; set; } = new(0.86f, 0.82f, 0.7f);
    [Export] public Color GroundLight { get; set; } = new(0.78f, 0.74f, 0.6f);
    [Export] public Color GroundDark { get; set; } = new(0.7f, 0.66f, 0.53f);
    [Export] public Color Contour { get; set; } = new(0.5f, 0.42f, 0.3f, 0.6f);
    [Export] public Color Ink { get; set; } = new(0.22f, 0.18f, 0.14f);
    [Export] public Color SafeInk { get; set; } = new(0.35f, 0.25f, 0.15f);
    [Export] public Color HazardInk { get; set; } = new(0.7f, 0.2f, 0.15f);
    [Export] public Color SupplyInk { get; set; } = new(0.25f, 0.5f, 0.25f);
    [Export] public Color WaypointInk { get; set; } = new(0.75f, 0.1f, 0.1f);
    [Export] public Color Graphite { get; set; } = new(0.25f, 0.25f, 0.28f);
    /// <summary>Height between contour lines, in m.</summary>
    [Export] public float ContourStep { get; set; } = 10.0f;

    public override void _Draw()
    {
        if (Map == null)
        {
            return;
        }
        Vector2 size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), Paper);
        MapChart chart = Map.Chart;
        IRouteQuery? route = Map.Route;
        float metresPerPixel = Map.MetresAcross / size.X;
        Vector2 centre = Map.Centre;
        Font font = ThemeDB.FallbackFont;

        // Seen ground: a tone per contour band, and a line where the band changes.
        float cellPixels = chart.CellSize / metresPerPixel;
        Vector2 half = size * 0.5f * metresPerPixel;
        Vector2I low = chart.CellAt(centre.X - half.X, centre.Y - half.Y);
        Vector2I high = chart.CellAt(centre.X + half.X, centre.Y + half.Y);
        for (int cx = low.X; cx <= high.X; cx++)
        {
            for (int cz = low.Y; cz <= high.Y; cz++)
            {
                Vector2I cell = new(cx, cz);
                if (!chart.Seen.TryGetValue(cell, out float height))
                {
                    continue;
                }
                int band = Mathf.FloorToInt(height / ContourStep);
                Color tone = GroundLight;
                if ((band & 1) == 1)
                {
                    tone = GroundDark;
                }
                Vector2 corner = ToPixel(new Vector2(cx * chart.CellSize, cz * chart.CellSize), centre, metresPerPixel, size);
                DrawRect(new Rect2(corner, new Vector2(cellPixels + 0.5f, cellPixels + 0.5f)), tone);
                if (chart.Seen.TryGetValue(new Vector2I(cx + 1, cz), out float east) && Mathf.FloorToInt(east / ContourStep) != band)
                {
                    DrawLine(corner + new Vector2(cellPixels, 0.0f), corner + new Vector2(cellPixels, cellPixels), Contour, 1.0f);
                }
                if (chart.Seen.TryGetValue(new Vector2I(cx, cz + 1), out float south) && Mathf.FloorToInt(south / ContourStep) != band)
                {
                    DrawLine(corner + new Vector2(0.0f, cellPixels), corner + new Vector2(cellPixels, cellPixels), Contour, 1.0f);
                }
            }
        }

        if (route != null)
        {
            DrawPaths(route, chart, centre, metresPerPixel, size, half);
            DrawStations(route, chart, centre, metresPerPixel, size, half, font);
        }

        // Pencil drawings.
        foreach (List<Vector2> stroke in chart.Drawings)
        {
            for (int i = 1; i < stroke.Count; i++)
            {
                DrawLine(ToPixel(stroke[i - 1], centre, metresPerPixel, size), ToPixel(stroke[i], centre, metresPerPixel, size), Graphite, 2.5f, true);
            }
        }

        // Waypoints: a red cross and a number.
        for (int i = 0; i < chart.Waypoints.Count; i++)
        {
            Vector2 p = ToPixel(chart.Waypoints[i], centre, metresPerPixel, size);
            DrawLine(p + new Vector2(-8, -8), p + new Vector2(8, 8), WaypointInk, 3.0f, true);
            DrawLine(p + new Vector2(-8, 8), p + new Vector2(8, -8), WaypointInk, 3.0f, true);
            DrawString(font, p + new Vector2(10, -6), (i + 1).ToString(), HorizontalAlignment.Left, -1, 22, WaypointInk);
        }

        // The carrier: an arrow pointing the way it faces.
        Vector2 me = size * 0.5f;
        Vector2 facing = Map.Facing;
        if (facing.IsZeroApprox())
        {
            facing = new Vector2(0.0f, -1.0f);
        }
        facing = facing.Normalized();
        Vector2 side = new(-facing.Y, facing.X);
        DrawColoredPolygon(new[] { me + facing * 16.0f, me - facing * 9.0f + side * 9.0f, me - facing * 4.0f, me - facing * 9.0f - side * 9.0f }, Ink);

        // Scale bar: 100 m (or 50 or 200, whatever fits the zoom).
        float bar = 100.0f;
        if (Map.MetresAcross < 400.0f)
        {
            bar = 50.0f;
        }
        else if (Map.MetresAcross > 900.0f)
        {
            bar = 200.0f;
        }
        Vector2 start = new(24.0f, size.Y - 28.0f);
        Vector2 end = start + new Vector2(bar / metresPerPixel, 0.0f);
        DrawLine(start, end, Ink, 3.0f);
        DrawLine(start + new Vector2(0, -6), start + new Vector2(0, 6), Ink, 2.0f);
        DrawLine(end + new Vector2(0, -6), end + new Vector2(0, 6), Ink, 2.0f);
        DrawString(font, start + new Vector2(0, -10), $"{bar:0} m", HorizontalAlignment.Left, -1, 20, Ink);
        DrawString(font, new Vector2(size.X * 0.5f - 6.0f, 26.0f), "N", HorizontalAlignment.Left, -1, 24, Ink);
        if (Map.CanDraw)
        {
            DrawString(font, new Vector2(size.X - 150.0f, size.Y - 18.0f), "pencil ready", HorizontalAlignment.Left, -1, 18, Graphite);
        }
    }

    private void DrawPaths(IRouteQuery route, MapChart chart, Vector2 centre, float metresPerPixel, Vector2 size, Vector2 half)
    {
        float step = Mathf.Max(chart.CellSize, 4.0f);
        float zTop = centre.Y - half.Y;
        float zBottom = centre.Y + half.Y;
        for (float z = zTop; z < zBottom; z += step)
        {
            int stage = route.StageAt(z);
            int paths = route.PathCount(stage);
            Vector2 zone = route.BranchZone(stage);
            for (int path = 0; path < paths; path++)
            {
                if (path > 0 && (z > zone.X || z < zone.Y))
                {
                    continue;
                }
                Vector3 a = route.PathPoint(stage, path, z);
                Vector3 b = route.PathPoint(stage, path, z + step);
                if (!chart.IsSeen(a.X, a.Z) || !chart.IsSeen(b.X, b.Z))
                {
                    continue;
                }
                RouteKind kind = route.PathKind(stage, path);
                Color colour = SafeInk;
                float width = 3.0f;
                if (kind == RouteKind.Hazardous)
                {
                    colour = HazardInk;
                    width = 2.0f;
                }
                else if (kind == RouteKind.SupplyRich)
                {
                    colour = SupplyInk;
                }
                DrawLine(ToPixel(new Vector2(a.X, a.Z), centre, metresPerPixel, size), ToPixel(new Vector2(b.X, b.Z), centre, metresPerPixel, size), colour, width, true);
            }
        }
    }

    private void DrawStations(IRouteQuery route, MapChart chart, Vector2 centre, float metresPerPixel, Vector2 size, Vector2 half, Font font)
    {
        Vector3 summit = route.RoutePoint(route.StartZ);
        if (chart.IsSeen(summit.X, summit.Z))
        {
            Vector2 p = ToPixel(new Vector2(summit.X, summit.Z), centre, metresPerPixel, size);
            DrawColoredPolygon(new[] { p + new Vector2(0, -12), p + new Vector2(10, 7), p + new Vector2(-10, 7) }, Ink);
        }
        int first = route.NextStationIndex(centre.Y + half.Y);
        for (int index = first; index < first + 8; index++)
        {
            Vector3 station = route.Station(index);
            if (station.Z < centre.Y - half.Y)
            {
                break;
            }
            if (!chart.IsSeen(station.X, station.Z))
            {
                continue;
            }
            Vector2 p = ToPixel(new Vector2(station.X, station.Z), centre, metresPerPixel, size);
            DrawRect(new Rect2(p - new Vector2(8, 8), new Vector2(16, 16)), Ink, false, 3.0f);
            DrawString(font, p + new Vector2(12, 8), index.ToString(), HorizontalAlignment.Left, -1, 22, Ink);
        }
    }

    public static Vector2 ToPixel(Vector2 world, Vector2 centre, float metresPerPixel, Vector2 size)
    {
        return size * 0.5f + (world - centre) / metresPerPixel;
    }

    public static Vector2 ToWorld(Vector2 pixel, Vector2 centre, float metresPerPixel, Vector2 size)
    {
        return centre + (pixel - size * 0.5f) * metresPerPixel;
    }
}
