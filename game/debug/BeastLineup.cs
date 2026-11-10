using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Night beasts debug scene: every beast of the roster in a row beside a 1.8 m pole, in the dark,
/// facing the camera, to check size, silhouette and where the glowing eyes sit. They stand still
/// (nothing ticks them); the full hunt is in TestTerrain with `-- --hour=23 --beasts-now`.
/// </summary>
public partial class BeastLineup : Node3D
{
    [Export] public BeastRoster? Roster { get; set; }
    [Export] public float Spacing { get; set; } = 2.5f;

    public override void _Ready()
    {
        if (Roster == null)
        {
            GD.PushError("BeastLineup: Roster is needed.");
            return;
        }
        int count = Roster.Beasts.Length;
        for (int i = 0; i < count; i++)
        {
            NightBeast beast = new();
            beast.Setup(Roster.Beasts[i], (ulong)i, 0, 0);
            AddChild(beast);
            beast.Position = new Vector3((i - (count - 1) * 0.5f) * Spacing, 0.0f, 0.0f);
            // Face the camera (+Z); the beast's front is -Z.
            beast.Rotation = new Vector3(0.0f, Mathf.Pi, 0.0f);
            GD.Print($"{Roster.Beasts[i].DisplayName}: model bounds {Bounds(beast)} (beast space, front is -Z)");
        }
    }

    /// <summary>The model's meshes' bounds in the beast's own space (prints help place the eyes).</summary>
    private static Aabb Bounds(NightBeast beast)
    {
        Aabb box = new();
        bool first = true;
        foreach (Node node in beast.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mesh || mesh.GetParent()?.Name == "Eyes" || mesh.Mesh == null)
            {
                continue;
            }
            Aabb local = (beast.GlobalTransform.AffineInverse() * mesh.GlobalTransform) * mesh.Mesh.GetAabb();
            if (first)
            {
                box = local;
                first = false;
            }
            else
            {
                box = box.Merge(local);
            }
        }
        return box;
    }
}
