using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Finds the nearest interactable (IInteractable in its group) within reach and in front of the
/// carrier, focuses it (its in-world prompt appears) and uses it when E is pressed.
/// </summary>
public partial class PickupInteractor : Node
{
    private static readonly StringName ActionInteract = "interact";
    private static readonly StringName InteractableGroup = IInteractable.Group;

    [Export] public PlayerController? Body { get; set; }
    /// <summary>Horizontal reach, in m.</summary>
    [Export] public float Reach { get; set; } = 2.2f;
    /// <summary>Things more than this far above or below the feet are out of reach, in m.</summary>
    [Export] public float VerticalReach { get; set; } = 2.0f;
    /// <summary>Widest angle from the carrier's facing that still counts as in front, in degrees.</summary>
    [Export] public float MaxAngle { get; set; } = 75.0f;

    public IInteractable? Focused { get; private set; }

    public override void _PhysicsProcess(double delta)
    {
        IInteractable? best = null;
        if (Body != null && CanAct())
        {
            best = FindBest(Body);
        }
        if (best != Focused)
        {
            // The old one may be gone already (picked up, unloaded).
            if (Focused is GodotObject old && IsInstanceValid(old))
            {
                Focused.SetFocused(false);
            }
            Focused = best;
            GameEvents.PublishFocus(best);
            Focused?.SetFocused(true);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(ActionInteract) && Focused != null && Body != null && CanAct())
        {
            Focused.Interact(Body);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Uses whatever is focused (tests and automation).</summary>
    public bool InteractNow()
    {
        if (Focused == null || Body == null)
        {
            return false;
        }
        return Focused.Interact(Body);
    }

    private IInteractable? FindBest(PlayerController body)
    {
        Vector3 feet = body.GlobalPosition;
        Vector3 forward = new Vector3(-body.GlobalBasis.Z.X, 0.0f, -body.GlobalBasis.Z.Z).Normalized();
        float cosLimit = Mathf.Cos(Mathf.DegToRad(MaxAngle));
        IInteractable? best = null;
        float bestDistance = float.MaxValue;

        Godot.Collections.Array<Node> candidates = GetTree().GetNodesInGroup(InteractableGroup);
        // The typed wrapper exposes the same owned array through this cast, without copying it.
        using Godot.Collections.Array ownedCandidates = (Godot.Collections.Array)candidates;
        foreach (Node node in candidates)
        {
            if (node is not IInteractable candidate || !IsInstanceValid(node))
            {
                continue;
            }
            Vector3 offset = candidate.InteractionPoint - feet;
            Vector3 flat = new(offset.X, 0.0f, offset.Z);
            float distance = flat.Length();
            if (distance > Reach || Mathf.Abs(offset.Y) > VerticalReach || distance >= bestDistance)
            {
                continue;
            }
            // Something right at the feet counts as in front, whatever the angle.
            if (distance > 0.3f && flat.Normalized().Dot(forward) < cosLimit)
            {
                continue;
            }
            best = candidate;
            bestDistance = distance;
        }
        return best;
    }

    private bool CanAct()
    {
        if (GameEvents.IsPacking || GameEvents.IsDowned)
        {
            return false;
        }
        return Body == null || Body.InputEnabled;
    }
}
