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

        // Seen ground: painted once into tile textures (ChartTiles), drawn here as a few rectangles.
        Vector2 half = size * 0.5f * metresPerPixel;
        Map.Tiles.Draw(this, centre, metresPerPixel, size);

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

        // The way to the next station: always known (its beacon is seen from afar), even under fog.
        if (route != null)
        {
            DrawStationArrow(route, centre, metresPerPixel, size, font);
        }

        // Supplies lying on ground already seen: a dot in their colour (food, water, gear, cloth, mending).
        DrawLoot(chart, centre, metresPerPixel, size);

        // The carrier: a bright disc under an arrow pointing the way he faces, easy to find at a glance.
        Vector2 me = ToPixel(Map.CarrierPoint, centre, metresPerPixel, size);
        DrawCircle(me, 15.0f, new Color(Paper, 0.9f));
        DrawArc(me, 15.0f, 0.0f, Mathf.Tau, 24, YouInk, 3.0f, true);
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
                bool seen = chart.IsSeen(a.X, a.Z) && chart.IsSeen(b.X, b.Z);
                // Unseen paths ahead (downhill of the carrier, this stage and the next) show faintly, dotted.
                bool ahead = z > Map!.CarrierPoint.Y - AheadReach && z < Map.CarrierPoint.Y;
                if (!seen && (!ahead || Mathf.PosMod(z, step * 2.0f) >= step))
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
                if (!seen)
                {
                    colour = new Color(colour, 0.4f);
                    width *= 0.7f;
                }
                DrawLine(ToPixel(new Vector2(a.X, a.Z), centre, metresPerPixel, size), ToPixel(new Vector2(b.X, b.Z), centre, metresPerPixel, size), colour, width, true);
            }
        }
    }

    /// <summary>How far ahead (downhill) unseen paths are sketched in, in m.</summary>
    [Export] public float AheadReach { get; set; } = 900.0f;
    [Export] public Color StationInk { get; set; } = new(0.85f, 0.45f, 0.1f);

    /// <summary>An arrow from the carrier toward the next station, with its number and distance; at the sheet's edge if it's off the sheet.</summary>
    private void DrawStationArrow(IRouteQuery route, Vector2 centre, float metresPerPixel, Vector2 size, Font font)
    {
        Vector2 me = Map!.CarrierPoint;
        int index = route.NextStationIndex(me.Y);
        Vector3 station = route.Station(index);
        Vector2 target = new(station.X, station.Z);
        Vector2 to = target - me;
        if (to.Length() < 1.0f)
        {
            return;
        }
        Vector2 direction = to.Normalized();
        Vector2 tipWorld = target;
        Vector2 tip = ToPixel(tipWorld, centre, metresPerPixel, size);
        Rect2 inner = new(new Vector2(34, 44), size - new Vector2(68, 88));
        if (!inner.HasPoint(tip))
        {
            // Off the sheet: pin the arrow to the edge, along the line from the carrier.
            Vector2 from = ToPixel(me, centre, metresPerPixel, size);
            float t = 1.0f;
            for (int i = 0; i < 24; i++)
            {
                t *= 0.85f;
                if (inner.HasPoint(from + (tip - from) * t))
                {
                    break;
                }
            }
            tip = from + (tip - from) * t;
        }
        Vector2 side = new(-direction.Y, direction.X);
        Vector2 tail = tip - direction * 34.0f;
        DrawLine(tail, tip - direction * 8.0f, StationInk, 5.0f, true);
        DrawColoredPolygon(new[] { tip, tip - direction * 16.0f + side * 10.0f, tip - direction * 16.0f - side * 10.0f }, StationInk);
        string label = $"Station {index} \u00b7 {to.Length() / 1000.0f:0.0} km";
        if (to.Length() < 1000.0f)
        {
            label = $"Station {index} \u00b7 {to.Length():0} m";
        }
        Vector2 at = tail - direction * 10.0f + new Vector2(-40.0f, side.Y * 8.0f);
        at.X = Mathf.Clamp(at.X, 8.0f, size.X - 170.0f);
        at.Y = Mathf.Clamp(at.Y, 40.0f, size.Y - 30.0f);
        DrawString(font, at, label, HorizontalAlignment.Left, -1, 20, StationInk);
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

    /// <summary>The ring round the carrier's arrow.</summary>
    [Export] public Color YouInk { get; set; } = new(0.15f, 0.45f, 0.85f);
    /// <summary>Dots for supplies on seen ground; the map's own items group and colour meta (WorldItem).</summary>
    [Export] public string LootGroup { get; set; } = "map_loot";
    [Export] public string LootColourMeta { get; set; } = "map_colour";
    [Export] public float LootDot { get; set; } = 5.0f;

    private void DrawLoot(MapChart chart, Vector2 centre, float metresPerPixel, Vector2 size)
    {
        foreach (Node node in GetTree().GetNodesInGroup(LootGroup))
        {
            if (node is not Node3D item || !item.IsInsideTree())
            {
                continue;
            }
            Vector3 at = item.GlobalPosition;
            if (!chart.IsSeen(at.X, at.Z))
            {
                continue;
            }
            Vector2 p = ToPixel(new Vector2(at.X, at.Z), centre, metresPerPixel, size);
            if (p.X < 0.0f || p.Y < 0.0f || p.X > size.X || p.Y > size.Y)
            {
                continue;
            }
            Color colour = Ink;
            if (item.HasMeta(LootColourMeta))
            {
                colour = item.GetMeta(LootColourMeta).AsColor();
            }
            DrawCircle(p, LootDot + 1.5f, Ink);
            DrawCircle(p, LootDot, colour);
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
