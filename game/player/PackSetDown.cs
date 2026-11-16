using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The backpack really comes off: while the carrier sets the pack down (GameEvents.PackingProgress)
/// the worn pack swings off the back and comes to stand on the ground in front of the feet, stays
/// there while it is open, and goes back the same way when it is shouldered. Only a picture: the
/// pack shown on the ground is a copy of the worn model, the worn one (and its straps) is hidden
/// meanwhile, and nothing about the load changes.
///
/// On another player's carrier (Remote) it doesn't listen to this machine's packing at all: the
/// owner's pose stream says whether their pack is off (Hold), and the swing is played here.
/// </summary>
public partial class PackSetDown : Node
{
    public ClothingVisualController? Clothing { get; set; }
    /// <summary>Another player's carrier: moved by Hold (from their pose stream), never by this machine's packing.</summary>
    public bool Remote { get; set; }
    /// <summary>How long the remote swing to the ground (or back) takes, in s.</summary>
    [Export] public float RemoteSwingSeconds { get; set; } = 0.6f;

    /// <summary>How far in front of the carrier the pack is stood, in m.</summary>
    [Export] public float Distance { get; set; } = 0.55f;
    /// <summary>How far to the carrier's right of straight ahead, in m (so the pack isn't hidden behind the body).</summary>
    [Export] public float Side { get; set; } = 0.35f;
    /// <summary>How high the pack is lifted over the straight path on its way, in m.</summary>
    [Export] public float ArcHeight { get; set; } = 0.25f;
    /// <summary>Ground is searched this far above and below the carrier's feet, in m.</summary>
    [Export] public float GroundSearch { get; set; } = 2.0f;
    /// <summary>The pack stands this far above the ground so it doesn't flicker against it, in m.</summary>
    [Export] public float GroundLift { get; set; } = 0.01f;

    /// <summary>True while the pack is off the back (on its way or on the ground).</summary>
    public bool Off => _copy != null;
    /// <summary>How far along the way to the ground the pack is: 0 on the back, 1 standing on the ground.</summary>
    public float Way { get; private set; }
    /// <summary>The pack shown on the ground (null while it is worn).</summary>
    public Node3D? Copy => _copy;
    /// <summary>Where the ground is under the set-down pack (world height), for checks.</summary>
    public float GroundHeight { get; private set; }

    private CharacterBody3D? _body;
    private BackpackAttachment? _mount;
    private Node3D? _copy;
    private Transform3D _onGround;

    public override void _Ready()
    {
        // The pack stays where it was put while the world is stopped for the packing page.
        ProcessMode = ProcessModeEnum.Always;
        Node? node = GetParent();
        while (node != null && node is not CharacterBody3D)
        {
            node = node.GetParent();
        }
        _body = node as CharacterBody3D;
        if (Remote)
        {
            return;
        }
        GameEvents.PackingModeChanged += OnPacking;
        GameEvents.PackingProgress += OnProgress;
        GameEvents.PackOpenChanged += OnOpen;
    }

    public override void _ExitTree()
    {
        if (!Remote)
        {
            GameEvents.PackingModeChanged -= OnPacking;
            GameEvents.PackingProgress -= OnProgress;
            GameEvents.PackOpenChanged -= OnOpen;
        }
        PutBack();
        _held = false;
    }

    private bool _held;

    /// <summary>
    /// A remote carrier's pack: off (swung to the ground and left standing there) or worn, as the
    /// owner's pose stream says. Called every frame; only a change starts a swing.
    /// </summary>
    public void Hold(bool off)
    {
        if (!Remote)
        {
            return;
        }
        if (off && _copy == null)
        {
            TakeOff();
        }
        _held = off && _copy != null;
    }

    private void OnPacking(bool packing)
    {
        if (packing)
        {
            TakeOff();
        }
        else
        {
            PutBack();
        }
    }

    private void OnProgress(float progress, bool settingDown)
    {
        if (_copy == null || progress < 0.0f)
        {
            return;
        }
        if (settingDown)
        {
            Way = Mathf.Clamp(progress, 0.0f, 1.0f);
        }
        else
        {
            Way = 1.0f - Mathf.Clamp(progress, 0.0f, 1.0f);
        }
    }

    private void OnOpen(bool open)
    {
        if (open && _copy != null)
        {
            Way = 1.0f;
            Place();
        }
    }

    public override void _Process(double delta)
    {
        if (Remote && _copy != null)
        {
            float step = (float)delta / Mathf.Max(RemoteSwingSeconds, 0.01f);
            if (_held)
            {
                Way = Mathf.Min(Way + step, 1.0f);
            }
            else
            {
                Way = Mathf.Max(Way - step, 0.0f);
                if (Way <= 0.0f)
                {
                    PutBack();
                    return;
                }
            }
        }
        if (_copy != null)
        {
            Place();
        }
    }

    /// <summary>The worn pack is hidden and its copy starts on the back, on its way to the ground.</summary>
    private void TakeOff()
    {
        if (_copy != null || _body == null || Clothing?.BackpackMount is not BackpackAttachment mount || !GodotObject.IsInstanceValid(mount.Model))
        {
            return;
        }
        // A carrier that isn't in the world (a preview, a model being swapped) has no ground to set a pack on.
        if (!IsInsideTree() || !_body.IsInsideTree() || !mount.Model.IsInsideTree())
        {
            return;
        }
        _mount = mount;
        _copy = mount.Model.Duplicate() as Node3D;
        if (_copy == null)
        {
            return;
        }
        _copy.Name = "SetDownPack";
        _copy.TopLevel = true;
        _body.AddChild(_copy);
        _copy.GlobalTransform = mount.Model.GlobalTransform;
        _onGround = GroundPlace(mount.Model.GlobalTransform);
        mount.Visible = false;
        Way = 0.0f;
    }

    /// <summary>The copy goes and the worn pack shows again.</summary>
    private void PutBack()
    {
        if (_copy != null && GodotObject.IsInstanceValid(_copy))
        {
            _copy.QueueFree();
        }
        _copy = null;
        if (_mount != null && GodotObject.IsInstanceValid(_mount))
        {
            _mount.Visible = true;
        }
        _mount = null;
        Way = 0.0f;
    }

    private void Place()
    {
        if (_copy == null || _mount == null || !GodotObject.IsInstanceValid(_mount) || !GodotObject.IsInstanceValid(_mount.Model))
        {
            return;
        }
        // Ease in and out, and lift it over the straight line: a pack is swung off, not slid.
        float eased = Mathf.SmoothStep(0.0f, 1.0f, Way);
        Transform3D worn = _mount.Model.GlobalTransform;
        Transform3D now = worn.InterpolateWith(_onGround, eased);
        now.Origin += Vector3.Up * ArcHeight * Mathf.Sin(Mathf.Pi * eased);
        _copy.GlobalTransform = now;
    }

    /// <summary>
    /// Where the pack stands on the ground: in front of the carrier and a little to the side, upright,
    /// turned half round so its back panel (the side that lay on the back) faces the carrier.
    /// </summary>
    private Transform3D GroundPlace(Transform3D worn)
    {
        Vector3 forward = new Vector3(-_body!.GlobalBasis.Z.X, 0.0f, -_body.GlobalBasis.Z.Z).Normalized();
        Vector3 right = forward.Cross(Vector3.Up).Normalized();
        Vector3 spot = _body.GlobalPosition + forward * Distance + right * Side;
        GroundHeight = Ground(spot, _body.GlobalPosition.Y);

        // Upright as it hangs on a carrier standing straight (the rest pose, without the spine's lean),
        // then turned half round about the vertical.
        Basis basis = worn.Basis;
        if (Clothing?.Skeleton is Skeleton3D rig && _mount != null)
        {
            Transform3D pivotToModel = _mount.Pivot.GlobalTransform.AffineInverse() * worn;
            basis = (rig.GlobalTransform * _mount.RestPivot(rig) * pivotToModel).Basis;
        }
        basis = new Basis(Vector3.Up, Mathf.Pi) * basis;

        // Its lowest corner rests on the ground.
        Aabb bounds = ModelFit.Bounds(_copy!);
        float lowest = float.MaxValue;
        for (int corner = 0; corner < 8; corner++)
        {
            lowest = Mathf.Min(lowest, (basis * bounds.GetEndpoint(corner)).Y);
        }
        Vector3 centre = basis * bounds.GetCenter();
        Vector3 origin = new(spot.X - centre.X, GroundHeight + GroundLift - lowest, spot.Z - centre.Z);
        return new Transform3D(basis, origin);
    }

    private float Ground(Vector3 point, float fallback)
    {
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(point + Vector3.Up * GroundSearch, point + Vector3.Down * GroundSearch);
        query.Exclude = new Godot.Collections.Array<Rid> { _body!.GetRid() };
        Godot.Collections.Dictionary hit = _body.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return fallback;
        }
        return hit["position"].AsVector3().Y;
    }
}
