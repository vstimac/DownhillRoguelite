using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Balance;

/// <summary>
/// A fixed test load: point masses on the carrier's back, in carrier space (origin at the feet,
/// X right, Y up, Z forward, so a backpack sits at negative Z). Stand-in for the inventory until
/// I1, which will publish the same LoadSnapshot. Items are parallel arrays: index i of each array
/// describes item i.
/// </summary>
[GlobalClass]
public partial class LoadProfile : Resource
{
    [Export] public string DisplayName { get; set; } = "Load";
    [Export] public float[] ItemMasses { get; set; } = System.Array.Empty<float>();
    [Export] public Vector3[] ItemPositions { get; set; } = System.Array.Empty<Vector3>();
    [Export] public Vector3[] ItemSizes { get; set; } = System.Array.Empty<Vector3>();

    /// <summary>Total mass, centre of mass and inertia about the feet.</summary>
    public LoadSnapshot ToSnapshot()
    {
        int count = Mathf.Min(ItemMasses.Length, ItemPositions.Length);
        float mass = 0.0f;
        Vector3 weighted = Vector3.Zero;
        float rollInertia = 0.0f;
        float pitchInertia = 0.0f;

        for (int i = 0; i < count; i++)
        {
            float m = ItemMasses[i];
            Vector3 p = ItemPositions[i];
            mass += m;
            weighted += p * m;
            rollInertia += m * (p.X * p.X + p.Y * p.Y);
            pitchInertia += m * (p.Y * p.Y + p.Z * p.Z);
        }

        Vector3 centerOfMass = Vector3.Zero;
        if (mass > 0.0f)
        {
            centerOfMass = weighted / mass;
        }

        Vector3[] sizes = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            if (i < ItemSizes.Length)
            {
                sizes[i] = ItemSizes[i];
            }
            else
            {
                sizes[i] = Vector3.One * 0.2f;
            }
        }

        return new LoadSnapshot(DisplayName, mass, centerOfMass, rollInertia, pitchInertia,
            ItemMasses[..count], ItemPositions[..count], sizes);
    }
}
