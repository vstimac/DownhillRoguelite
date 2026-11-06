using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// Scatters supplies around the carrier as WorldItems, cell by cell in a window that reaches further
/// downhill (-Z), and frees cells left behind. What lies where is decided by the run seed and the
/// cell, so a cell always gets the same items; items already taken this run never come back. Items
/// lie mostly along the walkable route (RunState.Route), with a light scatter everywhere; both fall
/// with depth and scale with difficulty. Ground height comes from a raycast, so cells wait until terrain has loaded under them.
/// A respawn starts a new run: taken items are forgotten and cells regenerate.
/// </summary>
public partial class SupplySpawner : Node3D
{
    [Export] public SupplyTable? Table { get; set; }
    [Export] public float CellSize { get; set; } = 64.0f;
    [Export] public int CellsAhead { get; set; } = 3;
    [Export] public int CellsBehind { get; set; } = 1;
    [Export] public int CellsSide { get; set; } = 2;
    /// <summary>Raycasts start this far above the route height, in m.</summary>
    [Export] public float RayHeight { get; set; } = 150.0f;
    [Export] public float RayDepth { get; set; } = 300.0f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    // Gives supplies their own random stream, independent of terrain's for the same seed.
    private const ulong SupplySalt = 0x5A99;
    private const ulong RowSalt = 0x7E1100000000UL;

    /// <summary>A planned item: which table entry, where on the ground plane, and its slot key.</summary>
    public readonly record struct Slot(int Entry, float X, float Z, string Key);

    public int SpawnedCount => _items.Count;
    public IEnumerable<WorldItem> SpawnedItems => _items.Values;

    private readonly Dictionary<Vector2I, List<string>> _cells = new();
    private readonly Dictionary<string, WorldItem> _items = new();
    private readonly HashSet<string> _taken = new();
    private readonly List<ItemDefinition?> _definitions = new();
    private Vector3 _focus;
    private bool _hasFocus;
    private RunState? _run;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        if (Table != null)
        {
            foreach (string path in Table.ItemPaths)
            {
                _definitions.Add(ResourceLoader.Load(path) as ItemDefinition);
            }
        }
        GameEvents.LocomotionUpdated += OnLocomotionUpdated;
        GameEvents.RespawnRequested += ResetRun;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotionUpdated;
        GameEvents.RespawnRequested -= ResetRun;
        _definitions.Clear();
        _items.Clear();
        _cells.Clear();
    }

    /// <summary>Forgets taken items and regenerates every cell (new run).</summary>
    public void ResetRun()
    {
        _taken.Clear();
        ReloadCells();
    }

    /// <summary>Frees every spawned item and regenerates cells; taken items stay taken.</summary>
    public void ReloadCells()
    {
        foreach (WorldItem item in _items.Values)
        {
            item.QueueFree();
        }
        _items.Clear();
        _cells.Clear();
    }

    public bool WasTaken(string slotKey)
    {
        return _taken.Contains(slotKey);
    }

    private void OnLocomotionUpdated(LocomotionSnapshot snapshot)
    {
        _focus = snapshot.Position;
        _hasFocus = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_hasFocus || Table == null || _run?.Route == null)
        {
            return;
        }

        Vector2I centre = CellAt(_focus.X, _focus.Z);
        List<Vector2I> far = new();
        foreach (Vector2I cell in _cells.Keys)
        {
            if (!InWindow(cell, centre, 1))
            {
                far.Add(cell);
            }
        }
        foreach (Vector2I cell in far)
        {
            UnloadCell(cell);
        }

        for (int dz = -CellsAhead; dz <= CellsBehind; dz++)
        {
            for (int dx = -CellsSide; dx <= CellsSide; dx++)
            {
                Vector2I cell = new(centre.X + dx, centre.Y + dz);
                if (!_cells.ContainsKey(cell))
                {
                    TryLoadCell(cell, _run.Route);
                }
            }
        }
    }

    /// <summary>
    /// The items a cell holds for a seed: pure and deterministic (also used by tests). Route items
    /// are planned per row of cells along Z (RoutePerKm) and kept by whichever cell they fall in;
    /// scattered items are planned per cell (Density).
    /// </summary>
    public List<Slot> PlanCell(Vector2I cell, ulong seed, IRouteQuery route, float difficultyMultiplier)
    {
        List<Slot> slots = new();
        if (Table == null || Table.ItemPaths.Length == 0)
        {
            return slots;
        }

        float minX = cell.X * CellSize;
        float minZ = cell.Y * CellSize;
        float startHeight = route.RoutePoint(route.StartZ).Y;
        float depth = Mathf.Max(0.0f, startHeight - route.RoutePoint(minZ + CellSize * 0.5f).Y);
        float scale = difficultyMultiplier * Mathf.Max(Table.MinDensityFactor, 1.0f - Table.DepthFalloffPer1000 * depth / 1000.0f);
        ulong salted = SplitMix64.Mix(seed, SupplySalt);

        SplitMix64 rowRandom = new(SplitMix64.Mix(salted, RowSalt ^ (uint)cell.Y));
        int routeCount = RandomCount(rowRandom, Table.RoutePerKm * scale * CellSize / 1000.0f);
        for (int i = 0; i < routeCount; i++)
        {
            float z = minZ + rowRandom.Range(0.0f, CellSize);
            float x = route.RoutePoint(z).X + rowRandom.Range(-Table.RouteBand, Table.RouteBand);
            int entry = PickEntry(rowRandom, depth);
            if (entry >= 0 && x >= minX && x < minX + CellSize)
            {
                slots.Add(new Slot(entry, x, z, $"r{cell.Y}:{i}"));
            }
        }

        SplitMix64 cellRandom = new(SplitMix64.Mix(salted, ((ulong)(uint)cell.X << 32) | (uint)cell.Y));
        int scatterCount = RandomCount(cellRandom, Table.Density * scale * CellSize * CellSize / 1000.0f);
        for (int i = 0; i < scatterCount; i++)
        {
            float x = minX + cellRandom.Range(0.0f, CellSize);
            float z = minZ + cellRandom.Range(0.0f, CellSize);
            int entry = PickEntry(cellRandom, depth);
            if (entry >= 0)
            {
                slots.Add(new Slot(entry, x, z, $"{cell.X}:{cell.Y}:{i}"));
            }
        }
        return slots;
    }

    /// <summary>Whole part of <paramref name="expected"/>, plus one more with the fractional part as the chance.</summary>
    private static int RandomCount(SplitMix64 random, float expected)
    {
        int count = Mathf.FloorToInt(expected);
        if (random.NextFloat() < expected - count)
        {
            count++;
        }
        return count;
    }

    private int PickEntry(SplitMix64 random, float depth)
    {
        float total = 0.0f;
        for (int i = 0; i < Table!.ItemPaths.Length; i++)
        {
            if (Available(i, depth))
            {
                total += Weight(i);
            }
        }
        float pick = random.NextFloat() * total;
        for (int i = 0; i < Table.ItemPaths.Length; i++)
        {
            if (!Available(i, depth))
            {
                continue;
            }
            pick -= Weight(i);
            if (pick <= 0.0f)
            {
                return i;
            }
        }
        return -1;
    }

    private bool Available(int entry, float depth)
    {
        float minDepth = 0.0f;
        if (entry < Table!.MinDepths.Length)
        {
            minDepth = Table.MinDepths[entry];
        }
        return depth >= minDepth && Weight(entry) > 0.0f;
    }

    private float Weight(int entry)
    {
        if (entry < Table!.Weights.Length)
        {
            return Table.Weights[entry];
        }
        return 1.0f;
    }

    private void TryLoadCell(Vector2I cell, IRouteQuery route)
    {
        ulong seed = _run!.Seed;
        List<Slot> slots = PlanCell(cell, seed, route, _run.DifficultySettings.SupplyMultiplier);
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        List<(Slot Slot, Vector3 Ground)> placed = new();

        foreach (Slot slot in slots)
        {
            float top = route.RoutePoint(slot.Z).Y + RayHeight;
            Godot.Collections.Dictionary hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(
                new Vector3(slot.X, top, slot.Z), new Vector3(slot.X, top - RayDepth, slot.Z), GroundMask));
            if (hit.Count == 0)
            {
                // Terrain isn't loaded here yet; try the whole cell again next frame.
                return;
            }
            placed.Add((slot, hit["position"].AsVector3()));
        }

        List<string> keys = new();
        foreach ((Slot slot, Vector3 ground) in placed)
        {
            keys.Add(slot.Key);
            ItemDefinition? definition = _definitions[slot.Entry];
            if (definition == null || _taken.Contains(slot.Key))
            {
                continue;
            }
            WorldItem item = new() { Definition = definition, SlotKey = slot.Key, Name = $"Supply_{slot.Key.Replace(':', '_')}" };
            item.Taken += OnTaken;
            AddChild(item);
            item.GlobalPosition = ground;
            _items[slot.Key] = item;
        }
        _cells[cell] = keys;
    }

    private void UnloadCell(Vector2I cell)
    {
        foreach (string key in _cells[cell])
        {
            if (_items.Remove(key, out WorldItem? item))
            {
                item.QueueFree();
            }
        }
        _cells.Remove(cell);
    }

    private void OnTaken(WorldItem item)
    {
        _taken.Add(item.SlotKey);
        _items.Remove(item.SlotKey);
    }

    private Vector2I CellAt(float x, float z)
    {
        return new Vector2I(Mathf.FloorToInt(x / CellSize), Mathf.FloorToInt(z / CellSize));
    }

    private bool InWindow(Vector2I cell, Vector2I centre, int margin)
    {
        Vector2I offset = cell - centre;
        return offset.X >= -CellsSide - margin && offset.X <= CellsSide + margin
            && offset.Y >= -CellsAhead - margin && offset.Y <= CellsBehind + margin;
    }
}
