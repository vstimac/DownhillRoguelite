using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the playable characters: the roster offers every character whose model is in
/// the project; each one loads, follows the mannequin rig (enough bones matched, whatever the Mixamo
/// prefix), holds its arms and legs where the mannequin holds them (so a model resting in an A-pose
/// isn't bent out of shape), and gets a drawn vest wide and deep enough to sit on its body.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/CharacterTests.tscn
/// </summary>
public partial class CharacterTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    /// <summary>Largest allowed angle between a character's limb and the mannequin's (degrees).</summary>
    [Export] public float LimbTolerance { get; set; } = 15.0f;
    [Export] public int MinFollowedBones { get; set; } = 20;

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        CarrierModel? look = PlayerBody?.GetNodeOrNull<CarrierModel>("CarrierModel");
        CarrierRoster? roster = CarrierRoster.Load();
        if (PlayerBody == null || Streamer == null || look == null || roster == null)
        {
            GD.PushError("CharacterTests: PlayerBody (with its CarrierModel), Streamer and the roster are needed.");
            GetTree().Quit(1);
            return;
        }
        PlayerBody.GlobalPosition = Streamer.SpawnPoint;
        await Frames(30);

        List<CarrierCharacter> characters = roster.Available();
        bool hasVucic = characters.Exists(c => c.Id == "vucic");
        Check(hasVucic, $"the roster offers {characters.Count} characters, Vučić among them",
            "Vučić's model is missing from the roster");
        foreach (CarrierCharacter character in characters)
        {
            await TestCharacter(look, character);
        }

        if (_failures.Count == 0)
        {
            GD.Print("CHARACTER TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            foreach (string failure in _failures)
            {
                GD.PrintErr("FAIL: " + failure);
            }
            GD.Print("CHARACTER TESTS: FAIL");
            GetTree().Quit(1);
        }
    }

    private async Task TestCharacter(CarrierModel look, CarrierCharacter character)
    {
        string name = character.DisplayName;
        bool shown = look.ShowCharacter(character.Id);
        await Frames(3);
        if (look.Follower != null)
        {
            look.Follower.RecordPose = true;
        }
        await Frames(10);
        Check(shown && look.CharacterId == character.Id, $"{name} loads", $"{name} didn't load");
        if (!shown || look.Follower == null || look.Driver == null || look.Model == null)
        {
            return;
        }
        GD.Print($"INFO: {name} rest turned over 10°: {look.Follower.AlignedBones(10.0f)}");
        Check(look.Follower.Followed >= MinFollowedBones, $"{name} follows {look.Follower.Followed} bones",
            $"{name} follows only {look.Follower.Followed} bones");
        Skeleton3D? skeleton = look.Follower.GetSkeleton();
        if (skeleton == null)
        {
            return;
        }
        foreach ((string from, string to, string driverFrom, string driverTo) in new[]
        {
            ("LeftArm", "LeftForeArm", "DEF-upper_arm.L", "DEF-forearm.L"),
            ("RightForeArm", "RightHand", "DEF-forearm.R", "DEF-hand.R"),
            ("LeftUpLeg", "LeftLeg", "DEF-thigh.L", "DEF-shin.L"),
            ("RightLeg", "RightFoot", "DEF-shin.R", "DEF-foot.R"),
        })
        {
            RigFollower follower = look.Follower;
            Vector3 own = World(skeleton, follower.TargetPose, RigFollower.FindBone(skeleton, "x_" + to)) - World(skeleton, follower.TargetPose, RigFollower.FindBone(skeleton, "x_" + from));
            Vector3 driver = World(look.Driver, follower.DriverPose, look.Driver.FindBone(driverTo)) - World(look.Driver, follower.DriverPose, look.Driver.FindBone(driverFrom));
            float angle = Mathf.RadToDeg(own.Normalized().AngleTo(driver.Normalized()));
            Check(angle <= LimbTolerance, $"{name}'s {from} lies along the mannequin's ({angle:0}°)",
                $"{name}'s {from} is {angle:0}° off the mannequin's");
        }
        CarrierWornGear? worn = look.Worn;
        if (worn != null)
        {
            Vector2 torso = worn.TorsoSize;
            Check(torso.X >= 0.15f && torso.Y >= 0.1f, $"{name}'s drawn vest is {torso.X * 2:0.00} x {torso.Y * 2:0.00} m",
                $"{name}'s drawn vest is too small ({torso.X * 2:0.00} x {torso.Y * 2:0.00} m)");
        }
    }

    /// <summary>A recorded bone origin (skeleton space) in world space.</summary>
    private static Vector3 World(Skeleton3D skeleton, Vector3[] pose, int bone)
    {
        if (bone < 0 || bone >= pose.Length)
        {
            return Vector3.Zero;
        }
        return skeleton.GlobalTransform * pose[bone];
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private void Check(bool ok, string pass, string fail)
    {
        if (ok)
        {
            GD.Print("PASS: " + pass);
        }
        else
        {
            _failures.Add(fail);
        }
    }
}
