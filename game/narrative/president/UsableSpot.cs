using System;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// Something at or near the restsite the carrier can use with E (the podium, a worker-automaton,
/// the banner's stand): what it is for the tooltip, and what E does (Use, set by whoever placed it;
/// false when nothing happened). Settles onto the ground once the terrain under it has loaded.
/// Its look is whatever children it is given.
/// </summary>
public partial class UsableSpot : Node3D, IInteractable, IDescribed
{
    [Export] public float FocusHeight { get; set; } = 1.0f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;
    /// <summary>Settle onto the ground (off for parts placed on another prop).</summary>
    [Export] public bool SettleOnGround { get; set; } = true;

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Details { get; set; } = "";
    public string Prompt { get; set; } = "";
    public Resource? IconSource => null;
    public Vector3 InteractionPoint => GlobalPosition + Vector3.Up * FocusHeight;
    public Func<bool>? Use { get; set; }
    public bool Grounded { get; private set; }

    public override void _Ready()
    {
        AddToGroup(IInteractable.Group);
    }

    public void SetFocused(bool focused)
    {
    }

    public bool Interact(Node3D interactor)
    {
        if (Use == null)
        {
            return false;
        }
        return Use();
    }

    public override void _Process(double delta)
    {
        if (Grounded || !SettleOnGround)
        {
            return;
        }
        PhysicsDirectSpaceState3D? space = GetWorld3D()?.DirectSpaceState;
        if (space == null)
        {
            return;
        }
        PhysicsRayQueryParameters3D ray = PhysicsRayQueryParameters3D.Create(GlobalPosition + Vector3.Up * 3.0f, GlobalPosition + Vector3.Down * 30.0f, GroundMask);
        Godot.Collections.Dictionary hit = space.IntersectRay(ray);
        if (hit.Count > 0)
        {
            GlobalPosition = (Vector3)hit["position"];
            Grounded = true;
        }
    }
}
