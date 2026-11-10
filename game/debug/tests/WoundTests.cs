using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Wounds: bones are read as head, neck, torso, arms and legs; a blow to the head takes about twice
/// what one to the torso does and a limb less; a standing body without bones is read by height; a
/// wound sprays blood, stains the ground and keeps bleeding (health drains, no flinch) until it
/// closes; a body that bleeds out leaves a pool; only so many stains stay.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/WoundTests.tscn
/// </summary>
public partial class WoundTests : Node3D
{
    [Export] public BloodEffects? Blood { get; set; }

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        if (Blood == null)
        {
            GD.PushError("WoundTests: Blood is needed.");
            GetTree().Quit(1);
            return;
        }

        // Bones by name.
        string[] names = { "mixamorig:Head", "mixamorig:Neck", "mixamorig:Spine1", "mixamorig:Hips", "mixamorig:LeftForeArm", "mixamorig:RightHand", "mixamorig:LeftUpLeg", "mixamorig:RightFoot", "Tail_02" };
        BodyZone[] zones = { BodyZone.Head, BodyZone.Neck, BodyZone.Torso, BodyZone.Torso, BodyZone.Arm, BodyZone.Arm, BodyZone.Leg, BodyZone.Leg, BodyZone.Body };
        bool allRight = true;
        string read = "";
        for (int i = 0; i < names.Length; i++)
        {
            BodyZone zone = BoneHitboxes.ZoneOf(names[i]);
            read += $"{names[i]}={zone} ";
            if (zone != zones[i])
            {
                allRight = false;
            }
        }
        Check("bones are read as head, neck, torso, arms and legs", allRight, read);

        // Damage by zone.
        MeleeCombat combat = new();
        float head = 30.0f * combat.ZoneDamage(BodyZone.Head);
        float torso = 30.0f * combat.ZoneDamage(BodyZone.Torso);
        float leg = 30.0f * combat.ZoneDamage(BodyZone.Leg);
        float arm = 30.0f * combat.ZoneDamage(BodyZone.Arm);
        Check("a head blow takes about twice a torso blow, limbs less", head >= torso * 1.8f && leg < torso && arm < torso && combat.ZoneDamage(BodyZone.Neck) > 1.0f,
            $"head {head:0}, torso {torso:0}, arm {arm:0}, leg {leg:0}");

        // A standing body without bones, by the height of the blow.
        BleedDummy capsule = new() { Name = "Capsule" };
        AddChild(capsule);
        BodyZone high = combat.CapsuleHit(capsule, 1.65f, Vector3.Forward).Zone;
        BodyZone middle = combat.CapsuleHit(capsule, 1.1f, Vector3.Forward).Zone;
        BodyZone low = combat.CapsuleHit(capsule, 0.4f, Vector3.Forward).Zone;
        Check("a body without bones is read by the blow's height", high == BodyZone.Head && middle == BodyZone.Torso && low == BodyZone.Leg,
            $"1.65 m {high}, 1.1 m {middle}, 0.4 m {low}");
        combat.Free();

        // A wound: spray, stain, bleeding without a flinch.
        BleedDummy body = new() { Name = "Body" };
        AddChild(body);
        body.GlobalPosition = new Vector3(3.0f, 0.0f, 0.0f);
        int spraysBefore = Blood.Sprays;
        int stainsBefore = Blood.StainCount;
        GameEvents.PublishWounded(body, body.GlobalPosition + Vector3.Up * 1.5f, Vector3.Forward, BodyZone.Neck, 40.0f);
        await Wait(3.0f);
        Bleeding? wound = body.GetNodeOrNull<Bleeding>(Bleeding.NodeName);
        Check("a wound sprays blood and stains the ground", Blood.Sprays == spraysBefore + 1 && Blood.StainCount > stainsBefore,
            $"sprays {Blood.Sprays - spraysBefore}, stains {Blood.StainCount - stainsBefore}");
        Check("the wound keeps bleeding: health drains with no flinch, and it drips", wound != null && body.Bled > 3.0f && body.Health < 100.0f && body.Blows == 0 && wound.Drips > 0,
            $"bled {body.Bled:0.0}, health {body.Health:0.0}, flinches {body.Blows}, drips {wound?.Drips}");

        // A neck wound bleeds more than one to the arm.
        BleedDummy armBody = new() { Name = "ArmBody" };
        AddChild(armBody);
        armBody.GlobalPosition = new Vector3(-3.0f, 0.0f, 0.0f);
        float neckBled = body.Bled;
        GameEvents.PublishWounded(armBody, armBody.GlobalPosition + Vector3.Up * 1.2f, Vector3.Forward, BodyZone.Arm, 40.0f);
        await Wait(3.0f);
        Check("a neck wound bleeds more than an arm wound", armBody.Bled > 0.0f && armBody.Bled < neckBled * 0.6f,
            $"neck {neckBled:0.0} in 3 s, arm {armBody.Bled:0.0} in 3 s");

        // Bleeding out: the wound ends with a pool.
        BleedDummy weak = new() { Name = "Weak", Health = 2.0f };
        AddChild(weak);
        weak.GlobalPosition = new Vector3(0.0f, 0.0f, 3.0f);
        GameEvents.PublishWounded(weak, weak.GlobalPosition + Vector3.Up * 1.5f, Vector3.Forward, BodyZone.Neck, 40.0f);
        int stainsBeforeDeath = Blood.StainCount;
        await Until(() => weak.Health <= 0.0f, 5.0f);
        await Wait(0.2f);
        Check("a body that bleeds out stops bleeding and leaves a pool", weak.Health <= 0.0f && weak.GetNodeOrNull<Bleeding>(Bleeding.NodeName) == null && Blood.StainCount > stainsBeforeDeath,
            $"health {weak.Health:0.0}, wound {weak.GetNodeOrNull<Bleeding>(Bleeding.NodeName) != null}, new stains {Blood.StainCount - stainsBeforeDeath}");

        // Only so many stains stay.
        for (int i = 0; i < Blood.MaxStains + 40; i++)
        {
            Blood.Splat(new Vector3(i * 0.1f, 0.0f, -5.0f), 0.3f);
        }
        Check("only so many stains stay", Blood.StainCount == Blood.MaxStains, $"stains {Blood.StainCount} of at most {Blood.MaxStains}");

        if (_failures.Count == 0)
        {
            GD.Print("WoundTests: all passed");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"WoundTests: {_failures.Count} failed");
            GetTree().Quit(1);
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

    private async Task Wait(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private async Task Until(System.Func<bool> done, float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds && !done(); elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }
}
