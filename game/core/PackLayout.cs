using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Where everything sits in the backpack right now, for the pages that draw it. Grid axes: x across
/// (to the carrier's right when worn), y up, z away from the back. Published by the inventory with
/// every change of the load; the pages never touch the inventory itself.
/// </summary>
public sealed class PackLayout
{
    public static PackLayout Empty { get; } = new("", Vector3I.Zero, 0.05f, 0.0f, Vector3.Zero, new List<PackLayoutEntry>());

    public PackLayout(string packName, Vector3I gridSize, float cellSize, float mass, Vector3 centerOfMass, IReadOnlyList<PackLayoutEntry> entries)
    {
        PackName = packName;
        GridSize = gridSize;
        CellSize = cellSize;
        Mass = mass;
        CenterOfMass = centerOfMass;
        Entries = entries;
    }

    public string PackName { get; }
    public Vector3I GridSize { get; }
    /// <summary>Edge length of one cell, in m.</summary>
    public float CellSize { get; }
    /// <summary>Mass of the pack and what is packed in it, in kg.</summary>
    public float Mass { get; }
    /// <summary>Centre of mass of the pack and its contents, in metres from the grid's lowest corner.</summary>
    public Vector3 CenterOfMass { get; }
    /// <summary>Packed copies first (in packing order), then the loose ones.</summary>
    public IReadOnlyList<PackLayoutEntry> Entries { get; }

    /// <summary>Grid extent in metres.</summary>
    public Vector3 SizeMeters => (Vector3)GridSize * CellSize;

    /// <summary>The copy with this identity, if the carrier has it in the pack or beside it.</summary>
    public bool TryFind(string instanceId, out PackLayoutEntry entry)
    {
        foreach (PackLayoutEntry candidate in Entries)
        {
            if (candidate.InstanceId == instanceId)
            {
                entry = candidate;
                return true;
            }
        }
        entry = default;
        return false;
    }
}
