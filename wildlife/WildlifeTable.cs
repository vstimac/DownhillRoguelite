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
    [Export] public int MaxAnimals { get; set; } = 36;
    /// <summary>Groups are placed this far from the carrier (min, max), in m.</summary>
    [Export] public Vector2 SpawnRing { get; set; } = new(30.0f, 140.0f);
    /// <summary>Chance a group is placed ahead (downhill) rather than anywhere around.</summary>
    [Export] public float AheadBias { get; set; } = 0.75f;
    /// <summary>Seconds between attempts to add a group.</summary>
    [Export] public float SpawnInterval { get; set; } = 0.5f;
    /// <summary>Animals farther than this from the carrier are removed, in m.</summary>
    [Export] public float DespawnDistance { get; set; } = 180.0f;
    /// <summary>Animals this far behind (uphill of) the carrier are removed, in m.</summary>
    [Export] public float BehindDistance { get; set; } = 80.0f;
    /// <summary>Calm animals (idle or walking) farther than this are updated less often, in m (saves physics time).</summary>
    [Export] public float FarDistance { get; set; } = 45.0f;
    /// <summary>A far, calm animal is updated once every this many physics steps (with the time of all of them).</summary>
    [Export] public int FarTickEvery { get; set; } = 4;

    [ExportGroup("Scatter")]
    /// <summary>A fall scatters every animal within this distance, in m.</summary>
    [Export] public float ScatterRadius { get; set; } = 20.0f;
    /// <summary>Seconds an animal keeps fleeing.</summary>
    [Export] public float FleeSeconds { get; set; } = 4.0f;
    /// <summary>Animals bolt when the carrier comes close (off: they stand their ground and can be pushed).</summary>
    [Export] public bool FleeFromCarrier { get; set; }

    [ExportGroup("Pushing and shoving")]
    /// <summary>The carrier's body radius, in m (touching = this plus the animal's radius).</summary>
    [Export] public float CarrierRadius { get; set; } = 0.35f;
    /// <summary>The carrier counts as pushing when this close to an animal's body (beyond touching), in m.</summary>
    [Export] public float PushContactMargin { get; set; } = 0.35f;
    /// <summary>...and steering into it: how closely the carrier's input points at the animal (cosine, 0-1).</summary>
    [Export] public float PushMinAlignment { get; set; } = 0.5f;
    /// <summary>A pushed animal gives way this slowly, in m/s (it's heavy; the carrier still mostly stops).</summary>
    [Export] public float PushYieldSpeed { get; set; } = 0.35f;
    /// <summary>Seconds of pushing an animal puts up with before it turns on the carrier.</summary>
    [Export] public float PushPatience { get; set; } = 3.0f;
    /// <summary>Pushing has to stop this long for the animal to calm down again, in s.</summary>
    [Export] public float PushForgetTime { get; set; } = 1.5f;
    /// <summary>Seconds it takes to turn round and brace before the shove (the warning).</summary>
    [Export] public float ShoveWindUp { get; set; } = 0.6f;
    /// <summary>How high the shove aims to throw the carrier, in m (air handling eats about a tenth: 4.5 reaches about 4 m).</summary>
    [Export] public float ShoveHeight { get; set; } = 4.5f;
    /// <summary>How fast the shove throws the carrier away from the animal, in m/s.</summary>
    [Export] public float ShoveSpeed { get; set; } = 4.0f;
    /// <summary>Animals smaller than this (body height, m) are too small to shove; they just get pushed.</summary>
    [Export] public float MinShoverHeight { get; set; } = 0.5f;
    /// <summary>After a shove, the animal won't shove again for this long, in s.</summary>
    [Export] public float ShoveCooldown { get; set; } = 6.0f;

    [ExportGroup("Reactions")]
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
