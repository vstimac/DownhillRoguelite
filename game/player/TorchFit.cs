using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Hand-tuned place of the torch: lit in the left hand, and put away (snuffed, hanging at the belt).
/// Each is a shift (m) and a turn (degrees, X then Y then Z) in the torch's own frame, where Y runs
/// up the haft. In the hand the torch can stay upright as the arm swings (Upright, the flame never
/// tips) or turn with the hand. Set in the torch fitting scene (debug/TorchFitting.tscn) and saved
/// to player/torch_fit.tres, which CarrierTorch loads.
/// </summary>
[GlobalClass]
public partial class TorchFit : Resource
{
    public const string DefaultPath = "res://player/torch_fit.tres";

    [Export] public Vector3 HandPosition { get; set; } = new(0.0f, 0.08f, 0.02f);
    [Export] public Vector3 HandRotation { get; set; } = new(-10.0f, 0.0f, 8.0f);
    /// <summary>In the hand: stay upright (only the turn above, about the carrier's facing) or follow the hand's own turn.</summary>
    [Export] public bool Upright { get; set; } = true;
    /// <summary>
    /// Put away: tucked in the belt by the bone it hangs from (the hips), shifted and turned in the
    /// carrier's own frame (X to his right, Y up, Z behind him), so it sits the same whatever the rig.
    /// </summary>
    [Export] public string StowBone { get; set; } = "DEF-hips";
    [Export] public Vector3 StowPosition { get; set; } = new(0.2f, -0.3f, 0.08f);
    [Export] public Vector3 StowRotation { get; set; } = new(25.0f, 0.0f, -15.0f);
    [Export] public float Scale { get; set; } = 1.0f;
    /// <summary>
    /// The carry pose while it burns in the hand (degrees, eased in by player/TorchArm): the upper arm
    /// swings forward, tucks in toward the chest, and the elbow bends up, holding the torch out in front.
    /// </summary>
    [Export] public float ArmRaise { get; set; } = 40.0f;
    [Export] public float ArmTuck { get; set; } = -10.0f;
    [Export] public float ElbowBend { get; set; } = 60.0f;

    public static Basis Turn(Vector3 degrees)
    {
        return Basis.FromEuler(new Vector3(Mathf.DegToRad(degrees.X), Mathf.DegToRad(degrees.Y), Mathf.DegToRad(degrees.Z)));
    }

    /// <summary>The saved fit (or the defaults), as a copy the caller may change.</summary>
    public static TorchFit Load()
    {
        if (ResourceLoader.Exists(DefaultPath))
        {
            TorchFit? saved = ResourceLoader.Load<TorchFit>(DefaultPath, null, ResourceLoader.CacheMode.Ignore);
            if (saved != null)
            {
                return (TorchFit)saved.Duplicate();
            }
        }
        return new TorchFit();
    }
}
