using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The blade meets the world: a swing in the open goes through; a trunk in its path stops it with a
/// glance (he flinches, then is back in his guard); an enemy's body is boxes on its bones that follow
/// its real pose (its arms hang where they are, not out in a T-pose), and a blow lands when part of
/// that body is in its reach (or the blade goes through it); an animal is a target too: struck, it bolts.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/SwordReachTests.tscn
/// </summary>
public partial class SwordReachTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        MeleeCombat? combat = PlayerBody?.GetNodeOrNull<MeleeCombat>("MeleeCombat");
        if (PlayerBody == null || Streamer == null || combat == null)
        {
            GD.PushError("SwordReachTests: PlayerBody (with MeleeCombat) and Streamer are needed.");
            GetTree().Quit(1);
            return;
        }
        PlayerBody.Teleport(Streamer.SpawnPoint + Vector3.Up * 0.1f, 0.0f);
        PlayerBody.Health?.Refill();
        PlayerBody.Stamina?.Refill();
        await Wait(2.0f);
        combat.ToggleDraw();
        await Until(() => combat.State == MeleeCombat.Stance.Ready, 3.0f);
        await Wait(0.3f);
        Vector3 at = PlayerBody.GlobalPosition;
        Vector3 forward = Flat(-PlayerBody.GlobalBasis.Z);
        Vector3 side = Flat(PlayerBody.GlobalBasis.X);

        // In the open: every blow of the chain goes through.
        int glances = combat.Glances;
        // Where the blade's outer part passes in the middle of the first blow, clear of his body (for the trunk below).
        Vector2 path = Vector2.Zero;
        for (int blow = 0; blow < 3; blow++)
        {
            PlayerBody.Stamina?.Refill();
            combat.Attack();
            for (int i = 0; i < 240 && combat.State != MeleeCombat.Stance.Ready; i++)
            {
                await Wait(1.0f / 60.0f);
                Vector3 outer = combat.BladeBase.Lerp(combat.BladeTip, 0.7f) - PlayerBody.GlobalPosition;
                Vector2 flat = new(outer.Dot(forward), outer.Dot(side));
                if (blow == 0 && combat.State == MeleeCombat.Stance.Attacking && combat.Animator!.Progress > 0.42f && path == Vector2.Zero && flat.Length() > 0.6f)
                {
                    path = flat;
                }
            }
        }
        Check("swings in the open don't catch on the ground", combat.Glances == glances, $"glances {combat.Glances - glances}");

        // A trunk in the way.
        StaticBody3D trunk = new() { Name = "Trunk" };
        trunk.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.15f, Height = 6.0f } });
        AddChild(trunk);
        // Where the swing passes, clear of his body.
        trunk.GlobalPosition = PlayerBody.GlobalPosition + forward * path.X + side * path.Y + Vector3.Up * 2.0f;
        await Face(PlayerBody, forward);
        PlayerBody.Stamina?.Refill();
        combat.Attack();
        bool stopped = false;
        for (int i = 0; i < 120 && combat.Glances == glances; i++)
        {
            await Wait(1.0f / 60.0f);
        }
        stopped = combat.State == MeleeCombat.Stance.Staggered;
        float fromTrunk = new Vector2(combat.LastGlance.X - trunk.GlobalPosition.X, combat.LastGlance.Z - trunk.GlobalPosition.Z).Length();
        await Until(() => combat.State == MeleeCombat.Stance.Ready, 3.0f);
        Check("a trunk in the swing's path stops it with a glance, and he's back in his guard", combat.Glances == glances + 1 && stopped
            && fromTrunk < 0.3f && combat.State == MeleeCombat.Stance.Ready,
            $"trunk {path.Length():0.00} m out, glances {combat.Glances - glances}, flinched {stopped}, struck {fromTrunk:0.00} m from the trunk's middle, now {combat.State}");
        trunk.QueueFree();
        await Wait(0.1f);

        // An enemy: boxes on its bones, in its real pose.
        // An ogre that doesn't see him, guard or dodge: it just stands there to be measured and struck.
        EnemySpecies ogre = (EnemySpecies)GD.Load<EnemySpecies>("res://wildlife/enemies/ogre.tres").Duplicate();
        ogre.SightRange = 0.0f;
        ogre.GuardChance = 0.0f;
        ogre.DodgeChance = 0.0f;
        ogre.WalkSpeed = 0.0f;
        HumanoidEnemy enemy = HumanoidEnemy.Create(ogre);
        AddChild(enemy);
        enemy.GlobalPosition = PlayerBody.GlobalPosition + forward * 1.25f + Vector3.Up * 0.1f;
        enemy.Rotation = new Vector3(0.0f, Mathf.Atan2(forward.X, forward.Z), 0.0f);
        await Wait(1.0f);
        BoneHitboxes? boxes = BoneHitboxes.For(enemy.HitSkeleton);
        int updates = boxes?.Updates ?? 0;
        await Wait(0.2f);
        Vector3 feet = enemy.GlobalPosition;
        float height = ogre.Height;
        Vector3 enemySide = Flat(enemy.GlobalBasis.X);
        bool chest = boxes != null && boxes.Touches(feet + Vector3.Up * height * 0.6f - enemySide, feet + Vector3.Up * height * 0.6f + enemySide, 0.02f);
        bool overHead = boxes != null && boxes.Touches(feet + Vector3.Up * (height + 0.4f) - enemySide, feet + Vector3.Up * (height + 0.4f) + enemySide, 0.02f);
        Vector3 shoulder = feet + Vector3.Up * height * 0.8f;
        bool behind = boxes != null && boxes.InReach(PlayerBody.GlobalPosition, -forward, combat.Reach, Mathf.Cos(Mathf.DegToRad(combat.HitAngle)), combat.ReachBelow, combat.ReachAbove);
        bool inFront = boxes != null && boxes.InReach(PlayerBody.GlobalPosition, forward, combat.Reach, Mathf.Cos(Mathf.DegToRad(combat.HitAngle)), combat.ReachBelow, combat.ReachAbove);
        bool tPoseArm = boxes != null && (boxes.Touches(shoulder + enemySide * height * 0.36f, shoulder + enemySide * height * 0.5f, 0.02f)
            || boxes.Touches(shoulder - enemySide * height * 0.36f, shoulder - enemySide * height * 0.5f, 0.02f));
        Check("an enemy's body is boxes on its bones, following it as it moves", boxes != null && boxes.Count >= 8 && boxes.Updates > updates,
            $"{boxes?.Count} boxes, {(boxes?.Updates ?? 0) - updates} updates in 0.2 s");
        Check("the boxes are where its body is: through the chest yes, over the head no", chest && !overHead, $"chest {chest}, over the head {overHead}");
        Check("its body is in reach of a blow toward it, not of one the other way", inFront && !behind, $"toward it {inFront}, away {behind}");
        Check("they follow its real pose, not a T-pose (nothing where outstretched arms would be)", !tPoseArm, $"arm out at the shoulder {tPoseArm}");
        // Held still for the blow (its boxes keep its last pose), so the swing meets it where it stands.
        enemy.ProcessMode = ProcessModeEnum.Disabled;
        Vector3 toEnemy = Flat(enemy.GlobalPosition - PlayerBody.GlobalPosition);
        int blows = enemy.Wounds + enemy.Guarded;
        PlayerBody.Stamina?.Refill();
        await Face(PlayerBody, toEnemy);
        bool swung = combat.Attack();
        float facingDot = 0.0f;
        bool reachable = false;
        for (int i = 0; i < 40 && enemy.Wounds + enemy.Guarded == blows; i++)
        {
            await Wait(1.0f / 60.0f);
            facingDot = Flat(-PlayerBody.GlobalBasis.Z).Dot(Flat(enemy.GlobalPosition - PlayerBody.GlobalPosition));
            reachable = reachable || boxes!.InReach(PlayerBody.GlobalPosition, Flat(-PlayerBody.GlobalBasis.Z), combat.Reach, Mathf.Cos(Mathf.DegToRad(combat.HitAngle)), combat.ReachBelow, combat.ReachAbove);
        }
        await Until(() => enemy.Wounds + enemy.Guarded > blows, 2.5f);
        Check("a blow toward its body lands", enemy.Wounds + enemy.Guarded == blows + 1,
            $"wounds {enemy.Wounds}, guarded {enemy.Guarded}, swung {swung}, state {enemy.State}, can be hit {enemy.CanBeHit}, facing it {facingDot:0.00}, reachable {reachable}, {(enemy.GlobalPosition - PlayerBody.GlobalPosition).Length():0.00} m");
        await Until(() => combat.State == MeleeCombat.Stance.Ready, 3.0f);
        enemy.QueueFree();
        await Wait(0.3f);

        // An animal.
        WildlifeTable table = GD.Load<WildlifeTable>("res://wildlife/default_wildlife.tres");
        WildlifeSpecies? cow = null;
        foreach (WildlifeSpecies species in table.Species)
        {
            if (species.Model != null && cow == null)
            {
                cow = species;
            }
        }
        WildlifeAnimal animal = new();
        AddChild(animal);
        animal.Setup(cow!, table, 7);
        animal.GlobalPosition = PlayerBody.GlobalPosition + forward * 1.4f + Vector3.Up * 0.05f;
        animal.Rotation = new Vector3(0.0f, Mathf.Atan2(side.X, side.Z), 0.0f);
        await Wait(0.8f);
        BoneHitboxes? animalBoxes = BoneHitboxes.For(animal.HitSkeleton);
        await Face(PlayerBody, forward);
        PlayerBody.Stamina?.Refill();
        combat.Attack();
        await Until(() => animal.Struck > 0, 2.5f);
        Check($"an animal ({cow?.DisplayName}) is struck on its body and bolts", animalBoxes != null && animal.Struck == 1 && animal.State == WildlifeAnimal.Mode.Flee,
            $"{animalBoxes?.Count} boxes, struck {animal.Struck}, {animal.State}");
        Check("Tab's lock-on passes animals over", !((IMeleeTarget)animal).Lockable, "");
        animal.QueueFree();

        if (_failures.Count == 0)
        {
            GD.Print("SWORD REACH TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("SWORD REACH TESTS: FAIL (" + string.Join(", ", _failures) + ")");
            GetTree().Quit(1);
        }
    }

    /// <summary>Turns him (and so the camera, which snaps behind) to face <paramref name="direction"/>.</summary>
    private async Task Face(PlayerController player, Vector3 direction)
    {
        player.Teleport(player.GlobalPosition, Mathf.Atan2(-direction.X, -direction.Z));
        await Wait(0.1f);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.Y = 0.0f;
        if (v.LengthSquared() < 0.000001f)
        {
            return Vector3.Zero;
        }
        return v.Normalized();
    }

    private async Task Wait(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private async Task Until(System.Func<bool> done, float timeout)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < timeout && !done(); elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
