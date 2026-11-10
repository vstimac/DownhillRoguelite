using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Tab locks on to the enemy nearest the middle of the view (within Range); Tab again lets go.
/// The lock is published (GameEvents.LockTarget): the camera keeps it in view and, with the sword
/// drawn, the carrier keeps facing it (MeleeCombat). It lets go by itself once the enemy is beaten,
/// gone or too far. A small mark sits on the locked enemy's chest. With no enemy about, Tab is left
/// to the pack screen (the same key).
/// </summary>
public partial class LockOn : Node
{
    public static readonly StringName ActionLock = "combat_lock_on";

    [Export] public PlayerController? Body { get; set; }
    [Export] public Key LockKey { get; set; } = Key.Tab;
    /// <summary>How far an enemy can be to lock on to it, and how far before the lock breaks (m).</summary>
    [Export] public float Range { get; set; } = 18.0f;
    [Export] public float BreakRange { get; set; } = 28.0f;
    /// <summary>Enemies further than this from the view's middle are only picked if there's no one nearer it (degrees).</summary>
    [Export] public float ViewAngle { get; set; } = 70.0f;
    /// <summary>How much a degree off the view's middle counts against a metre of distance when choosing.</summary>
    [Export] public float AnglePerMetre { get; set; } = 4.0f;
    /// <summary>The mark: where on the enemy (share of its height), its size (m) and colour.</summary>
    [Export] public float MarkHeight { get; set; } = 0.6f;
    [Export] public float MarkSize { get; set; } = 0.14f;
    [Export] public Color MarkColour { get; set; } = new(0.95f, 0.85f, 0.3f, 0.95f);

    public Node3D? Target { get; private set; }
    public int Locks { get; private set; }

    private MeshInstance3D? _mark;

    public override void _Ready()
    {
        if (!InputMap.HasAction(ActionLock))
        {
            InputMap.AddAction(ActionLock);
            InputMap.ActionAddEvent(ActionLock, new InputEventKey { PhysicalKeycode = LockKey });
        }
        _mark = new MeshInstance3D
        {
            Name = "LockMark",
            Mesh = new QuadMesh { Size = new Vector2(MarkSize, MarkSize) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = MarkColour,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                RenderPriority = 2,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            TopLevel = true,
            Visible = false,
        };
        AddChild(_mark);
        GameEvents.NewMountain += OnNewMountain;
    }

    public override void _ExitTree()
    {
        GameEvents.NewMountain -= OnNewMountain;
        if (Target != null)
        {
            GameEvents.PublishLockTarget(null);
        }
    }

    private void OnNewMountain(ulong seed) => Release();

    public override void _Input(InputEvent @event)
    {
        if (!@event.IsActionPressed(ActionLock) || @event.IsEcho())
        {
            return;
        }
        if (GameEvents.IsPacking || GameEvents.IsScreenOpen || GameEvents.IsMapOpen || GameEvents.IsConversing || GetTree().Paused)
        {
            return;
        }
        // Handled only when it locks or lets go: with no one about, the key is the pack screen's.
        if (Toggle())
        {
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Locks on to the best enemy in view, or lets go if locked; false if there was nothing to do.</summary>
    public bool Toggle()
    {
        if (Target != null)
        {
            Release();
            return true;
        }
        Node3D? best = Choose();
        if (best == null)
        {
            return false;
        }
        Target = best;
        Locks++;
        GameEvents.PublishLockTarget(best);
        return true;
    }

    public void Release()
    {
        if (Target == null)
        {
            return;
        }
        Target = null;
        if (_mark != null)
        {
            _mark.Visible = false;
        }
        GameEvents.PublishLockTarget(null);
    }

    /// <summary>The enemy to lock on to: near and near the view's middle, preferring those in view.</summary>
    public Node3D? Choose()
    {
        if (Body == null)
        {
            return null;
        }
        Vector3 from = Body.GlobalPosition;
        Vector3 view = -Body.GlobalBasis.Z;
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera != null)
        {
            view = -camera.GlobalBasis.Z;
        }
        view.Y = 0.0f;
        if (view.LengthSquared() < 0.0001f)
        {
            view = Vector3.Forward;
        }
        view = view.Normalized();
        Node3D? best = null;
        float bestScore = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup(IMeleeTarget.Group))
        {
            if (node is not IMeleeTarget target || node is not Node3D body || !target.CanBeHit)
            {
                continue;
            }
            Vector3 to = target.HitBase - from;
            to.Y = 0.0f;
            float distance = to.Length();
            if (distance > Range)
            {
                continue;
            }
            float angle = 0.0f;
            if (distance > 0.01f)
            {
                angle = Mathf.RadToDeg(view.AngleTo(to / distance));
            }
            float score = distance + angle / Mathf.Max(AnglePerMetre, 0.01f);
            if (angle > ViewAngle)
            {
                score += 1000.0f;
            }
            if (score < bestScore)
            {
                bestScore = score;
                best = body;
            }
        }
        return best;
    }

    public override void _Process(double delta)
    {
        if (Target == null)
        {
            return;
        }
        if (!IsInstanceValid(Target) || !Target.IsInsideTree() || Target is not IMeleeTarget target || !target.CanBeHit || Body == null
            || Body.GlobalPosition.DistanceTo(target.HitBase) > BreakRange)
        {
            Release();
            return;
        }
        if (_mark != null)
        {
            _mark.Visible = true;
            _mark.GlobalPosition = target.HitBase + Vector3.Up * target.HitHeight * MarkHeight;
        }
    }
}
