using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// One kind of humanoid enemy (HumanoidEnemy): its model (any Tripo-rigged humanoid, animated through
/// the mannequin's clips), size, speeds, blows and defence. Kept as .tres files in wildlife/enemies/.
/// </summary>
[GlobalClass]
public partial class EnemySpecies : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    /// <summary>The model (a rigged humanoid scene); its bones follow the mannequin driver (RigFollower).</summary>
    [Export(PropertyHint.File, "*.fbx,*.glb,*.gltf,*.tscn")] public string ModelPath { get; set; } = "";
    /// <summary>The model's own height (as imported), and the height it stands at in the world (m).</summary>
    [Export] public float ModelHeight { get; set; } = 0.9f;
    [Export] public float Height { get; set; } = 2.0f;
    /// <summary>Turn of the model about up, in degrees, so it faces the way it walks.</summary>
    [Export] public float ModelYaw { get; set; }
    [Export] public float BodyRadius { get; set; } = 0.4f;
    /// <summary>A plain health bar over its head.</summary>
    [Export] public bool HealthBar { get; set; } = true;

    [ExportGroup("Moving")]
    [Export] public float WalkSpeed { get; set; } = 1.4f;
    [Export] public float RunSpeed { get; set; } = 4.0f;
    [Export] public float SprintSpeed { get; set; } = 6.0f;
    /// <summary>Beyond this distance it sprints to close in; nearer it runs.</summary>
    [Export] public float SprintBeyond { get; set; } = 14.0f;
    [Export] public float Acceleration { get; set; } = 8.0f;
    [Export] public float TurnSpeed { get; set; } = 300.0f;
    /// <summary>It notices the carrier within this distance, and gives up beyond the second.</summary>
    [Export] public float SightRange { get; set; } = 28.0f;
    [Export] public float GiveUpRange { get; set; } = 70.0f;
    /// <summary>Wandering while it hasn't noticed the carrier (m from where it started, and its pace).</summary>
    [Export] public float WanderRadius { get; set; } = 8.0f;

    [ExportGroup("Blows")]
    /// <summary>Reach of its blows from its centre to the carrier's (m), and the cone they cover (degrees each side).</summary>
    [Export] public float Reach { get; set; } = 1.6f;
    [Export] public float ReachAngle { get; set; } = 55.0f;
    /// <summary>The blows it picks from (mannequin clip names), with their damage, push and speed (same order).</summary>
    [Export] public string[] Attacks { get; set; } = { "Punch_Jab", "Punch_Cross", "Sword_Attack" };
    [Export] public float[] AttackDamage { get; set; } = { 8.0f, 12.0f, 20.0f };
    /// <summary>Shove on the carrier (m/s); at or above KnockdownPush the blow knocks them down as a ragdoll.</summary>
    [Export] public float[] AttackPush { get; set; } = { 2.5f, 3.5f, 6.0f };
    [Export] public float[] AttackSpeed { get; set; } = { 1.0f, 1.0f, 0.9f };
    /// <summary>Share of each clip in which the blow lands (start, end).</summary>
    [Export] public Vector2[] AttackWindow { get; set; } = { new(0.25f, 0.55f), new(0.3f, 0.6f), new(0.35f, 0.6f) };
    [Export] public float KnockdownPush { get; set; } = 5.5f;
    [Export] public float AttackCooldown { get; set; } = 1.2f;
    /// <summary>Upward share of a knockdown blow's push.</summary>
    [Export] public float LiftShare { get; set; } = 0.3f;
    [Export] public float StaggerHeight { get; set; } = 0.25f;

    [ExportGroup("Defence")]
    [Export] public float Health { get; set; } = 120.0f;
    /// <summary>Chance that it raises its guard against a blow it sees coming, and that it jumps back instead.</summary>
    [Export(PropertyHint.Range, "0,1")] public float GuardChance { get; set; } = 0.4f;
    [Export(PropertyHint.Range, "0,1")] public float DodgeChance { get; set; } = 0.2f;
    /// <summary>How long the guard stays up, and the share of a guarded blow's damage it still takes.</summary>
    [Export] public float GuardSeconds { get; set; } = 1.1f;
    [Export] public float GuardedDamage { get; set; } = 0.1f;
    /// <summary>Half-angle in front that the guard covers (degrees).</summary>
    [Export] public float GuardAngle { get; set; } = 70.0f;
    [Export] public float DodgeSpeed { get; set; } = 5.0f;
    /// <summary>A blow it takes staggers it this long; a heavy one (at or above HeavyHit damage) longer.</summary>
    [Export] public float StaggerSeconds { get; set; } = 0.5f;
    [Export] public float HeavyHit { get; set; } = 30.0f;
    [Export] public float HeavyStaggerSeconds { get; set; } = 0.9f;
    /// <summary>How a shove it takes dies away (1/s).</summary>
    [Export] public float ShoveDrag { get; set; } = 6.0f;
    /// <summary>Recoil when the carrier's shield stops its blow (m/s back).</summary>
    [Export] public float BlockedRecoil { get; set; } = 3.0f;
    /// <summary>How long it lies after dying before it sinks away.</summary>
    [Export] public float CorpseSeconds { get; set; } = 6.0f;

    [ExportGroup("Sounds")]
    [Export] public string[] CallSounds { get; set; } = System.Array.Empty<string>();
    [Export] public string[] HurtSounds { get; set; } = System.Array.Empty<string>();
    [Export] public string[] SwingSounds { get; set; } = { "res://audio/combat/sword_swing_0.wav", "res://audio/combat/sword_swing_1.wav" };
    [Export] public string[] HitSounds { get; set; } = { "res://audio/combat/sword_kick_0.wav" };
    [Export] public string[] GuardSounds { get; set; } = { "res://audio/combat/sword_block_0.wav", "res://audio/combat/sword_block_1.wav" };
    [Export] public float VolumeDb { get; set; } = 2.0f;
    [Export] public float VoicePitch { get; set; } = 1.0f;

    /// <summary>Damage, push, speed and window of blow <paramref name="index"/> (the last value repeats if a list is short).</summary>
    public float Damage(int index) => Pick(AttackDamage, index, 10.0f);
    public float Push(int index) => Pick(AttackPush, index, 3.0f);
    public float Speed(int index) => Pick(AttackSpeed, index, 1.0f);

    public Vector2 Window(int index)
    {
        if (AttackWindow.Length == 0)
        {
            return new Vector2(0.3f, 0.6f);
        }
        return AttackWindow[Mathf.Clamp(index, 0, AttackWindow.Length - 1)];
    }

    private static float Pick(float[] values, int index, float fallback)
    {
        if (values.Length == 0)
        {
            return fallback;
        }
        return values[Mathf.Clamp(index, 0, values.Length - 1)];
    }
}
