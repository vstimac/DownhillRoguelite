using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// Mass properties of a packed backpack. Converts grid positions to carrier space (origin at the
/// feet, X right, Y up, Z forward) through the pack's mount, and sums items plus the empty pack.
/// </summary>
public static class LoadCalculator
{
    /// <summary>A point inside the grid, in metres from its lowest corner, to carrier space.</summary>
    public static Vector3 GridToCarrier(BackpackDefinition pack, Vector3 gridMeters)
    {
        Vector3 size = pack.SizeMeters;
        return new Vector3(
            pack.MountCenterX + gridMeters.X - size.X * 0.5f,
            pack.MountBottomHeight + gridMeters.Y,
            -pack.MountBackOffset - gridMeters.Z);
    }

    /// <summary>Item's centre of mass in grid metres.</summary>
    public static Vector3 ItemCenterOfMass(BackpackDefinition pack, PackedItem item)
    {
        Vector3 boxCentre = ((Vector3)item.Origin + (Vector3)item.Size * 0.5f) * pack.CellSize;
        return boxCentre + GridOrientations.All[item.Orientation] * item.Definition.CenterOfMassOffset;
    }

    /// <summary>Combined centre of mass of pack and contents in grid metres, and the total mass.</summary>
    public static Vector3 CenterOfMass(BackpackDefinition pack, BackpackGrid grid, out float totalMass)
    {
        totalMass = pack.EmptyMass;
        Vector3 weighted = pack.SizeMeters * 0.5f * pack.EmptyMass;
        foreach (PackedItem item in grid.Items)
        {
            totalMass += item.Definition.MassKg;
            weighted += ItemCenterOfMass(pack, item) * item.Definition.MassKg;
        }
        if (totalMass <= 0.0f)
        {
            return pack.SizeMeters * 0.5f;
        }
        return weighted / totalMass;
    }

    /// <summary>The load as balance sees it, with per-item boxes for visuals.</summary>
    public static LoadSnapshot Calculate(BackpackDefinition pack, BackpackGrid grid)
    {
        float mass = pack.EmptyMass;
        Vector3 shell = GridToCarrier(pack, pack.SizeMeters * 0.5f);
        Vector3 weighted = shell * pack.EmptyMass;
        float rollInertia = pack.EmptyMass * (shell.X * shell.X + shell.Y * shell.Y);
        float pitchInertia = pack.EmptyMass * (shell.Y * shell.Y + shell.Z * shell.Z);

        List<float> masses = new();
        List<Vector3> positions = new();
        List<Vector3> sizes = new();
        foreach (PackedItem item in grid.Items)
        {
            float m = item.Definition.MassKg;
            Vector3 p = GridToCarrier(pack, ItemCenterOfMass(pack, item));
            mass += m;
            weighted += p * m;
            rollInertia += m * (p.X * p.X + p.Y * p.Y);
            pitchInertia += m * (p.Y * p.Y + p.Z * p.Z);

            masses.Add(m);
            positions.Add(GridToCarrier(pack, ((Vector3)item.Origin + (Vector3)item.Size * 0.5f) * pack.CellSize));
            sizes.Add((Vector3)item.Size * pack.CellSize);
        }

        return new LoadSnapshot(
            $"{pack.DisplayName} ({grid.Items.Count} items)",
            mass,
            weighted / mass,
            rollInertia,
            pitchInertia,
            masses.ToArray(),
            positions.ToArray(),
            sizes.ToArray());
    }
}
