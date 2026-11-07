using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Footing of a walkable surface. Terrain tags its collision bodies with one through metadata
/// (MetaKey); locomotion reads it under the feet. Shared data, so it lives in core.
/// </summary>
[GlobalClass]
public partial class SurfaceType : Resource
{
    /// <summary>Metadata key on a CollisionObject3D holding its SurfaceType.</summary>
    public const string MetaKey = "surface_type";

    [Export] public string DisplayName { get; set; } = "Rock";

    /// <summary>1 = full traction (dry rock), 0 = none (ice). Scales acceleration, braking and slide limits.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Grip { get; set; } = 1.0f;

    /// <summary>How much an impact on this surface hurts: 1 = rock, lower = softer (grass, snow, mud).</summary>
    [Export(PropertyHint.Range, "0,2,0.01")] public float Hardness { get; set; } = 1.0f;

    /// <summary>Walking speed on it: 1 = firm ground, lower where feet sink (deep snow, mud).</summary>
    [Export(PropertyHint.Range, "0.3,1,0.01")] public float WalkSpeed { get; set; } = 1.0f;

    /// <summary>Tint used by debug geometry so surfaces are easy to tell apart.</summary>
    [Export] public Color DebugColor { get; set; } = new(0.55f, 0.55f, 0.58f);
}
