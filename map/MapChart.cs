using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Map;

/// <summary>
/// What the carrier's map knows (M13): the ground it has seen (cells, with their height, for
/// shading), its waypoints and its pencil drawings, all in world XZ. Plain data with no engine
/// nodes, so tests can drive it; FieldMap fills it and draws it. Cleared with every new mountain.
/// </summary>
public sealed class MapChart
{
    /// <summary>Size of a discovered cell, in m.</summary>
    public float CellSize { get; }
    public int MaxWaypoints { get; }

    public IReadOnlyDictionary<Vector2I, float> Seen => _seen;
    public IReadOnlyList<Vector2> Waypoints => _waypoints;
    public IReadOnlyList<List<Vector2>> Drawings => _drawings;

    private readonly Dictionary<Vector2I, float> _seen = new();
    private readonly List<Vector2> _waypoints = new();
    private readonly List<List<Vector2>> _drawings = new();

    public MapChart(float cellSize, int maxWaypoints)
    {
        CellSize = cellSize;
        MaxWaypoints = maxWaypoints;
    }

    public Vector2I CellAt(float x, float z)
    {
        return new Vector2I(Mathf.FloorToInt(x / CellSize), Mathf.FloorToInt(z / CellSize));
    }

    public Vector2 CellCentre(Vector2I cell)
    {
        return new Vector2((cell.X + 0.5f) * CellSize, (cell.Y + 0.5f) * CellSize);
    }

    public bool IsSeen(float x, float z)
    {
        return _seen.ContainsKey(CellAt(x, z));
    }

    /// <summary>Marks every cell within <paramref name="radius"/> of a point as seen; returns how many were new.</summary>
    public int Reveal(Vector2 centre, float radius, System.Func<float, float, float> heightAt)
    {
        int added = 0;
        Vector2I low = CellAt(centre.X - radius, centre.Y - radius);
        Vector2I high = CellAt(centre.X + radius, centre.Y + radius);
        float radiusSquared = radius * radius;
        for (int cx = low.X; cx <= high.X; cx++)
        {
            for (int cz = low.Y; cz <= high.Y; cz++)
            {
                Vector2I cell = new(cx, cz);
                if (_seen.ContainsKey(cell))
                {
                    continue;
                }
                Vector2 middle = CellCentre(cell);
                if (middle.DistanceSquaredTo(centre) > radiusSquared)
                {
                    continue;
                }
                _seen[cell] = heightAt(middle.X, middle.Y);
                added++;
            }
        }
        return added;
    }

    /// <summary>Adds a waypoint; when the map is full the oldest makes room. Returns its index.</summary>
    public int AddWaypoint(Vector2 point)
    {
        if (_waypoints.Count >= MaxWaypoints)
        {
            _waypoints.RemoveAt(0);
        }
        _waypoints.Add(point);
        return _waypoints.Count - 1;
    }

    /// <summary>Removes the waypoint nearest a point if it is within <paramref name="reach"/>; returns whether one went.</summary>
    public bool RemoveWaypointNear(Vector2 point, float reach)
    {
        int best = -1;
        float bestDistance = reach;
        for (int i = 0; i < _waypoints.Count; i++)
        {
            float distance = _waypoints[i].DistanceTo(point);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        if (best < 0)
        {
            return false;
        }
        _waypoints.RemoveAt(best);
        return true;
    }

    /// <summary>Starts a new pencil line.</summary>
    public void BeginStroke(Vector2 point)
    {
        _drawings.Add(new List<Vector2> { point });
    }

    /// <summary>Extends the current pencil line; returns the length added, in m.</summary>
    public float ExtendStroke(Vector2 point)
    {
        if (_drawings.Count == 0)
        {
            BeginStroke(point);
            return 0.0f;
        }
        List<Vector2> stroke = _drawings[^1];
        float length = stroke[^1].DistanceTo(point);
        stroke.Add(point);
        return length;
    }

    public void Clear()
    {
        _seen.Clear();
        _waypoints.Clear();
        _drawings.Clear();
    }
}
