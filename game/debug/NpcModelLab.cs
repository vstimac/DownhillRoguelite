using DownhillRoguelite.Narrative;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug scene for travellers with their own model (TestNpcModels.tscn): seats every persona around a
/// fire on flat ground, so the model's size, the sitting pose and the log can be checked by eye.
/// E on a figure plays the talking clip. With -- --talk all of them talk at once (for renders).
/// </summary>
public partial class NpcModelLab : Node3D
{
    [Export] public NpcPersona[] Personas { get; set; } = System.Array.Empty<NpcPersona>();
    [Export] public float Radius { get; set; } = 2.4f;
    [Export] public Camera3D? View { get; set; }
    /// <summary>Where the camera looks (the first persona sits at -Z, facing the fire).</summary>
    [Export] public Vector3 LookAtPoint { get; set; } = new(0.0f, 0.8f, -2.4f);

    public override void _Ready()
    {
        View?.LookAt(LookAtPoint, Vector3.Up);
        bool talk = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--talk") >= 0;
        Vector3 fire = GlobalPosition;
        for (int i = 0; i < Personas.Length; i++)
        {
            float angle = Mathf.Tau * i / Mathf.Max(1, Personas.Length) + Mathf.Pi;
            NpcFigure figure = new();
            figure.Setup(Personas[i], 1, fire);
            AddChild(figure);
            figure.GlobalPosition = fire + new Vector3(Mathf.Sin(angle), 0.0f, Mathf.Cos(angle)) * Radius;
            figure.SetFocused(true);
            if (talk)
            {
                figure.Interact(this);
            }
        }
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--probe") >= 0)
        {
            GetTree().CreateTimer(1.0).Timeout += Probe;
        }
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--rest") >= 0)
        {
            // Models in their own rest pose, without following the rig (to tell model faults from rig faults).
            foreach (Node node in FindChildren("RigFollower", "", true, false))
            {
                node.QueueFree();
            }
        }
    }

    /// <summary>Prints where each followed model's spine, neck and head bones are (skeleton space), rest vs posed.</summary>
    private void Probe()
    {
        foreach (Node node in FindChildren("RigFollower", "", true, false))
        {
            if (node is not DownhillRoguelite.Player.RigFollower follower || follower.GetSkeleton() is not Skeleton3D body)
            {
                continue;
            }
            follower.RecordPose = true;
            GetTree().CreateTimer(0.2).Timeout += () =>
            {
                for (int i = 0; i < body.GetBoneCount(); i++)
                {
                    string name = body.GetBoneName(i);
                    if (name.Contains("Spine2") || name.Contains("Neck") || name.Contains("Head") || name.EndsWith("Hips"))
                    {
                        Vector3 posed = Vector3.Zero;
                        if (i < follower.TargetPose.Length)
                        {
                            posed = follower.TargetPose[i];
                        }
                        GD.Print($"probe {i} {name} parent {body.GetBoneParent(i)} rest {body.GetBoneGlobalRest(i).Origin} posed {posed} scale {body.GetBonePoseScale(i)}");
                    }
                }
                Skeleton3D? driver = follower.Driver;
                if (driver != null)
                {
                    for (int i = 0; i < driver.GetBoneCount(); i++)
                    {
                        if (driver.GetBonePoseScale(i).LengthSquared() < 0.5f)
                        {
                            GD.Print($"probe driver bone {driver.GetBoneName(i)} pose scale {driver.GetBonePoseScale(i)}");
                        }
                    }
                }
            };
        }
    }
}
