using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// All species and how the population around the carrier is kept: how many, in which ring, how
/// far ahead, when animals are removed, how far a fall or a sword swing scatters them, and the
/// behaviour every species shares (how long it grazes or lies down, how loud the carrier is, how
/// steep a step it refuses).
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
    /// <summary>A sword swing scatters every animal within this distance, in m.</summary>
    [Export] public float SwingScatterRadius { get; set; } = 12.0f;
    /// <summary>A landing this hard (m/s into the ground) startles the animals around; from PlayerRagdoll's hard landing (7.5) the carrier falls, which scatters them.</summary>
    [Export] public float HardLandingSpeed { get; set; } = 6.0f;
    /// <summary>A hard landing carries this far per m/s of impact, in m (at most ScatterRadius).</summary>
    [Export] public float LandingNoisePerSpeed { get; set; } = 2.2f;
    /// <summary>Seconds an animal flees at least, whatever the distance.</summary>
    [Export] public float FleeSeconds { get; set; } = 2.5f;
    /// <summary>Seconds an animal flees at most before it stops to look back.</summary>
    [Export] public float MaxFleeSeconds { get; set; } = 14.0f;

    [ExportGroup("Senses")]
    /// <summary>The carrier counts as moving above this speed, in m/s.</summary>
    [Export] public float MovingSpeed { get; set; } = 0.5f;
    /// <summary>...and as fast (like sprinting) above this one, in m/s: a run, a slide, a fall.</summary>
    [Export] public float FastSpeed { get; set; } = 4.5f;
    /// <summary>A carrier standing still is seen at this share of a species' sight.</summary>
    [Export] public float StillSight { get; set; } = 0.5f;
    /// <summary>Share of a species' hearing at which it hears a walking carrier (a sprinter is heard at all of it).</summary>
    [Export] public float WalkNoise { get; set; } = 0.45f;
    /// <summary>...and one standing still (breathing, the pack creaking).</summary>
    [Export] public float StillNoise { get; set; } = 0.15f;
    /// <summary>A full heavy load makes the carrier this much louder (0.5 = half again).</summary>
    [Export] public float LoadNoise { get; set; } = 0.5f;
    /// <summary>Load mass counted as a full heavy load, in kg.</summary>
    [Export] public float HeavyLoad { get; set; } = 60.0f;
    /// <summary>After fleeing an animal stays uneasy this long: it sees, hears and reacts this much farther.</summary>
    [Export] public float UneasySeconds { get; set; } = 25.0f;
    [Export] public float UneasyFactor { get; set; } = 1.3f;
    /// <summary>After an alert that came to nothing, no new one for this long (s, random in range) unless the carrier gets alarming.</summary>
    [Export] public Vector2 AlertCooldown { get; set; } = new(8.0f, 14.0f);
    /// <summary>Shortest alert beat before an animal acts on a close threat, in s.</summary>
    [Export] public float MinAlertBeat { get; set; } = 0.35f;
    /// <summary>Clip speed while frozen on alert (nearly still).</summary>
    [Export] public float AlertClipSpeed { get; set; } = 0.08f;
    /// <summary>After coming to look, a curious animal leaves the carrier alone this long (s, random in range).</summary>
    [Export] public Vector2 CuriousCooldown { get; set; } = new(40.0f, 70.0f);
    /// <summary>Seconds a curious animal stands sniffing before it loses interest (random in range).</summary>
    [Export] public Vector2 SniffSeconds { get; set; } = new(3.0f, 6.0f);
    /// <summary>A herd follows its members' alarm within this distance, in m.</summary>
    [Export] public float HerdAlarmRadius { get; set; } = 25.0f;

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

    [ExportGroup("Daily life")]
    /// <summary>Seconds an animal stands idle (random in this range); grazing lasts twice as long.</summary>
    [Export] public Vector2 WanderInterval { get; set; } = new(3.0f, 9.0f);
    /// <summary>Farthest an animal walks to its next grazing spot, in m (at least half of it).</summary>
    [Export] public float WanderDistance { get; set; } = 6.0f;
    /// <summary>Chances, when a grazing spell ends, to walk on, to stand and look round, or to lie down (the rest: graze on).</summary>
    [Export] public float WalkChance { get; set; } = 0.35f;
    [Export] public float IdleChance { get; set; } = 0.15f;
    [Export] public float RestChance { get; set; } = 0.08f;
    /// <summary>Seconds lying down (random in this range).</summary>
    [Export] public Vector2 RestSeconds { get; set; } = new(15.0f, 40.0f);
    /// <summary>Chance a spell starts with walking to water, when there is water near (WildlifeAnimal.WaterNear).</summary>
    [Export] public float DrinkChance { get; set; } = 0.1f;
    /// <summary>Animals go to water this far, in m.</summary>
    [Export] public float DrinkRange { get; set; } = 30.0f;

    [ExportGroup("Ground")]
    /// <summary>A step may be this much steeper than the species' MaxSlope, in degrees, before the animal refuses it.</summary>
    [Export] public float SlopeTolerance { get; set; } = 8.0f;
    /// <summary>Ground lower ahead than a walkable slope by more than this is a drop: the animal won't go there, in m.</summary>
    [Export] public float CliffDrop { get; set; } = 0.6f;
    /// <summary>A moving animal checks the ground ahead this often, in s (each check is two rays).</summary>
    [Export] public float ProbeInterval { get; set; } = 0.2f;
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
