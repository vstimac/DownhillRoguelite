using System.Threading.Tasks;
using DownhillRoguelite.Narrative;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for travellers with their own model: a persona with a model sits by the fire as
/// that model (following the hidden mannequin rig, on a log), at a person's height, with every bone
/// intact (a broken export that collapses a bone, e.g. the head, fails here); E makes them talk for
/// a while, then they settle back; a persona without a model keeps the cloaked figure.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/NpcModelTests.tscn
/// </summary>
public partial class NpcModelTests : Node3D
{
    [Export] public NpcPersona? Modelled { get; set; }
    [Export] public NpcPersona? Cloaked { get; set; }
    [Export] public Vector2 HeightRange { get; set; } = new(1.6f, 2.0f);

    private readonly System.Collections.Generic.List<string> _failures = new();

    public override async void _Ready()
    {
        if (Modelled == null || Cloaked == null)
        {
            GD.PushError("NpcModelTests: Modelled and Cloaked personas are needed.");
            GetTree().Quit(1);
            return;
        }
        NpcFigure modelled = Seat(Modelled, new Vector3(0, 0, -2.4f));
        NpcFigure cloaked = Seat(Cloaked, new Vector3(0, 0, 2.4f));
        modelled.TalkSeconds = 0.5f;
        await Frames(30);

        Node3D? model = modelled.GetNodeOrNull<Node3D>("Model");
        Skeleton3D? body = FindFirst<Skeleton3D>(model);
        RigFollower? follower = FindFirst<RigFollower>(body);
        AnimationPlayer? animations = FindFirst<AnimationPlayer>(modelled.GetNodeOrNull("Rig"));
        Check("a persona with a model sits as that model, following the rig", model != null && body != null && follower != null && animations != null,
            $"model {model != null}, skeleton {body != null}, follower {follower != null}, rig animations {animations != null}");
        Check("a persona without a model keeps the cloaked figure", cloaked.GetNodeOrNull("Model") == null && cloaked.GetChildCount() > 2,
            $"model {cloaked.GetNodeOrNull("Model") != null}, children {cloaked.GetChildCount()}");
        if (model == null || body == null || follower == null || animations == null)
        {
            Finish();
            return;
        }

        float height = 0.0f;
        MeshInstance3D? mesh = FindFirst<MeshInstance3D>(model);
        if (mesh != null)
        {
            height = mesh.GetAabb().Size.Y * Modelled.ModelScale;
        }
        Check("the model is a person's height", height >= HeightRange.X && height <= HeightRange.Y,
            $"{height:0.00} m (expected {HeightRange.X:0.0}-{HeightRange.Y:0.0} m)");

        string collapsed = "";
        for (int i = 0; i < body.GetBoneCount(); i++)
        {
            if (Mathf.Abs(body.GetBoneRest(i).Basis.Determinant()) < 0.01f || body.GetBonePoseScale(i).LengthSquared() < 0.01f)
            {
                collapsed += body.GetBoneName(i) + " ";
            }
        }
        Check("no bone of the model is collapsed (head, hands, ...)", collapsed.Length == 0, $"collapsed: [{collapsed.Trim()}]");

        follower.RecordPose = true;
        await Frames(10);
        int hips = -1;
        for (int i = 0; i < body.GetBoneCount(); i++)
        {
            if (body.GetBoneName(i).EndsWith("Hips"))
            {
                hips = i;
                break;
            }
        }
        float restHips = 0.0f;
        float posedHips = 0.0f;
        if (hips >= 0 && hips < follower.TargetPose.Length)
        {
            restHips = body.GetBoneGlobalRest(hips).Origin.Y;
            posedHips = follower.TargetPose[hips].Y;
        }
        Check("they sit: the hips drop well below standing", hips >= 0 && posedHips < restHips * 0.8f,
            $"hips {posedHips:0.00} vs standing {restHips:0.00} (skeleton space)");

        string idle = animations.CurrentAnimation;
        modelled.Interact(this);
        await Frames(5);
        string talking = animations.CurrentAnimation;
        await Frames(60);
        string after = animations.CurrentAnimation;
        Check("E makes them talk, then they settle back", talking.Contains("Talking") && after == idle && idle.Contains("Idle"),
            $"idle '{idle}', on E '{talking}', after '{after}'");
        Finish();
    }

    private NpcFigure Seat(NpcPersona persona, Vector3 at)
    {
        NpcFigure figure = new();
        figure.Setup(persona, 1, Vector3.Zero);
        AddChild(figure);
        figure.GlobalPosition = at;
        return figure;
    }

    private static T? FindFirst<T>(Node? root) where T : Node
    {
        if (root == null)
        {
            return null;
        }
        foreach (Node node in root.FindChildren("*", "", true, false))
        {
            if (node is T found)
            {
                return found;
            }
        }
        return null;
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private void Check(string name, bool ok, string detail)
    {
        if (ok)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("NPC MODEL TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"NPC MODEL TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }
}
