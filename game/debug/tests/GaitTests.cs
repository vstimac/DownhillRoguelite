using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Every animal whose walk and run clips have measured ground speeds walks at its walking pace and
/// flees at its flee pace on flat ground, its clip and rate chosen as the game does (CreatureGait);
/// a FootSlipMeter measures how much the planted foot slides while the body moves. A creature whose
/// clip plays at the wrong rate for its speed slides ("swims").
///
/// The stock clips aren't made to be planted exactly (their feet drift a little even at the best
/// rate, a cube animal's legs only swing), so each pace checks the game's rate slides no more than
/// the same clip played clearly slower or faster, and stays under a ceiling.
/// GAIT_SWEEP=1 prints each pace at seven rates instead, to find a clip's ground speed.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/GaitTests.tscn
/// </summary>
public partial class GaitTests : Node3D
{
    /// <summary>The planted foot slides at most this much of the body's speed at the game's rate.</summary>
    public const float MostSkate = 0.6f;
    /// <summary>The rates the game's is compared with (times it): slower and faster than any measurement's error.</summary>
    public const float Slower = 0.7f;
    public const float Faster = 1.4f;
    /// <summary>The game's rate may slide this much more than a compared one (the meter's noise).</summary>
    public const float Noise = 0.04f;

    private int _failures;

    public override async void _Ready()
    {
        WildlifeTable table = ResourceLoader.Load<WildlifeTable>("res://wildlife/default_wildlife.tres");
        int tested = 0;
        foreach (WildlifeSpecies species in table.Species)
        {
            if (species.Model == null || species.WalkGroundSpeed <= 0.0f || species.FleeGroundSpeed <= 0.0f)
            {
                continue;
            }
            tested++;
            if (OS.GetEnvironment("GAIT_SWEEP").Length > 0)
            {
                await Sweep(species);
                continue;
            }
            foreach ((string pace, float speed) in new[] { ("walks", species.WalkSpeed), ("flees", species.FleeSpeed) })
            {
                (FootSlipStats feet, string clip, float rate, _) = await Walk(species, speed);
                (FootSlipStats slow, _, _, _) = await Walk(species, speed, Slower, 2.5f);
                (FootSlipStats fast, _, _, _) = await Walk(species, speed, Faster, 2.5f);
                bool best = feet.Skate <= slow.Skate + Noise && feet.Skate <= fast.Skate + Noise;
                Check($"a {species.DisplayName}'s feet slide least at the rate it {pace} at ({speed:0.0} m/s)",
                    feet.Samples >= 20 && feet.Skate < MostSkate && best,
                    $"{clip} at {rate:0.00}x: skate {feet.Skate:0.00} (at {Slower:0.0}x the rate {slow.Skate:0.00}, at {Faster:0.0}x {fast.Skate:0.00}, most {MostSkate:0.00}); {feet}");
            }
        }
        Check("animals with measured clips were tested", tested >= 10, $"{tested} species");
        GD.Print(_failures == 0 ? "GAIT TESTS: PASS" : $"GAIT TESTS: FAIL ({_failures})");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>GAIT_SWEEP=1: each pace at the gait's rate times 0.5 to 2, to find the rate that slides least.</summary>
    private async Task Sweep(WildlifeSpecies species)
    {
        foreach ((string pace, float speed) in new[] { ("walk", species.WalkSpeed), ("flee", species.FleeSpeed) })
        {
            string line = $"SWEEP {species.DisplayName} {pace} {speed:0.0}:";
            float best = 99.0f;
            float bestScale = 0.0f;
            foreach (float scale in new[] { 0.5f, 0.63f, 0.8f, 1.0f, 1.25f, 1.6f, 2.0f })
            {
                (FootSlipStats feet, string clip, float rate, _) = await Walk(species, speed, scale, 2.5f);
                line += $" {scale:0.00}x({rate:0.00} {clip}) {feet.Skate:0.00}";
                if (feet.Skate < best) { best = feet.Skate; bestScale = scale; }
            }
            GD.Print($"{line} | best {bestScale:0.00} skate {best:0.00}");
        }
    }

    /// <summary>The species moving straight ahead at one pace for a few seconds; what its feet did.</summary>
    private async Task<(FootSlipStats feet, string clip, float rate, bool skeleton)> Walk(WildlifeSpecies species, float speed, float rateScale = 1.0f, float seconds = 3.0f)
    {
        Node3D body = new();
        AddChild(body);
        Node3D model = species.Model!.Instantiate<Node3D>();
        model.Scale = Vector3.One * species.ModelScale;
        model.RotationDegrees = species.ModelRotationDegrees;
        body.AddChild(model);
        AnimationPlayer? player = null;
        foreach (Node node in model.FindChildren("*", "AnimationPlayer", true, false))
        {
            player ??= node as AnimationPlayer;
        }
        CreatureGait.AddClips(player, species.ExtraAnimations);
        CreatureGait gait = CreatureGait.For(species.WalkClip, species.FleeClip, species.WalkGroundSpeed, species.FleeGroundSpeed, species.MaxClipRate)!;
        gait.TrimHolds(player);
        bool skeleton = model.FindChildren("*", "Skeleton3D", true, false).Count > 0;
        string[] paws = skeleton ? PawTips(model) : System.Array.Empty<string>();
        FootSlipStats feet = new();
        string clip = "";
        float rate = 0.0f;
        const float dt = 1.0f / 60.0f;
        for (int frame = 0; frame < (int)(60 * seconds); frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            // The body moves toward its -Z, as WildlifeAnimal faces where it goes.
            Vector3 moved = -body.GlobalBasis.Z * speed * dt;
            body.GlobalPosition += moved;
            gait.Measure(moved, dt);
            clip = gait.Pick();
            rate = gait.Rate() * rateScale;
            if (player != null && player.HasAnimation(clip))
            {
                player.SpeedScale = rate;
                if (player.CurrentAnimation != clip)
                {
                    player.Play(clip, 0.2);
                }
            }
            if (frame == 60)
            {
                // Once the measured speed has caught up with the body and the blend in is done.
                body.AddChild(new FootSlipMeter { Stats = feet, Feet = paws, MovingAbove = 0.3f });
            }
        }
        body.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        return (feet, clip, rate, skeleton);
    }

    /// <summary>A skeleton's foot bones ("…Foot…"), else their tips ("…Foot…_end").</summary>
    private static string[] PawTips(Node3D model)
    {
        List<string> ends = new();
        List<string> plain = new();
        foreach (Node node in model.FindChildren("*", "Skeleton3D", true, false))
        {
            if (node is not Skeleton3D skeleton)
            {
                continue;
            }
            for (int i = 0; i < skeleton.GetBoneCount(); i++)
            {
                string name = skeleton.GetBoneName(i);
                if (name.ToLowerInvariant().Contains("foot"))
                {
                    (name.EndsWith("_end") ? ends : plain).Add(name);
                }
            }
        }
        // The foot bone itself (the hoof's top): a hoof's tip curls under the ground as it swings
        // forward and would read as the planted foot.
        return (plain.Count >= 2 ? plain : ends).ToArray();
    }

    private void Check(string what, bool pass, string detail)
    {
        if (!pass)
        {
            _failures++;
        }
        GD.Print($"{(pass ? "PASS" : "FAIL")}  {what}: {detail}");
    }
}
