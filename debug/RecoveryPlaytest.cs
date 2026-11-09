using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>The expedition opens with gameplay UI. F3 still enables the developer overlays.</summary>
public partial class RecoveryPlaytest : Node3D
{
    public override void _Ready() { DebugDraw3D.SetEnabled(false); }
}
