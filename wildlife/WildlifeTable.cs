using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// All species and how the population around the carrier is kept: how many, in which ring, how
/// far ahead, when animals are removed, and how far a fall scatters them.
/// </summary>
[GlobalClass]
public partial class WildlifeTable : Resource
{
    [Export] public WildlifeSpecies[] Species { get; set; } = System.Array.Empty<WildlifeSpecies>();

    [ExportGroup("Population")]
    /// <summary>Most animals alive at once.</summary>
    [Export] public int MaxAnimals { get; set; } = 22;
    /// <summary>Groups are placed this far from the carrier (min, max), in m.</summary>
    [Export] public Vector2 SpawnRing { get; set; } = new(25.0f, 70.0f);
    /// <summary>Chance a group is placed ahead (downhill) rather than anywhere around.</summary>
    [Export] public float AheadBias { get; set; } = 0.75f;
    /// <summary>Seconds between attempts to add a group.</summary>
    [Export] public float SpawnInterval { get; set; } = 0.5f;
    /// <summary>Animals farther than this from the carrier are removed, in m.</summary>
    [Export] public float DespawnDistance { get; set; } = 95.0f;
    /// <summary>Animals this far behind (uphill of) the carrier are removed, in m.</summary>
    [Export] public float BehindDistance { get; set; } = 45.0f;

    [ExportGroup("Reactions")]
    /// <summary>A fall scatters every animal within this distance, in m.</summary>
    [Export] public float ScatterRadius { get; set; } = 20.0f;
    /// <summary>Seconds an animal keeps fleeing.</summary>
    [Export] public float FleeSeconds { get; set; } = 4.0f;
    /// <summary>Seconds between wander decisions (random in this range).</summary>
    [Export] public Vector2 WanderInterval { get; set; } = new(3.0f, 9.0f);
    /// <summary>How far an animal wanders per decision, in m.</summary>
    [Export] public float WanderDistance { get; set; } = 4.0f;
    /// <summary>Physics layers the ground ray hits.</summary>
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;
    /// <summary>The layer animals' bodies are on (kept off the world layer so ground rays ignore them).</summary>
    [Export(PropertyHint.Layers3DPhysics)] public uint BodyLayer { get; set; } = 8;
    /// <summary>What animals' bodies collide with: the world and each other.</summary>
    [Export(PropertyHint.Layers3DPhysics)] public uint BodyMask { get; set; } = 9;
    /// <summary>Steepest ground an animal walks on, in degrees; steeper, it slides.</summary>
    [Export] public float MaxFloorAngle { get; set; } = 50.0f;
    /// <summary>How fast a model leans to the slope under it (per second).</summary>
    [Export] public float TiltSpeed { get; set; } = 6.0f;
}
