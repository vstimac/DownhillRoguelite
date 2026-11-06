using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Autoload holding state for the current run. Systems share run-wide data through
/// this node instead of referencing each other directly. Placeholder until the run loop exists.
/// </summary>
public partial class RunState : Node
{
    /// <summary>Seed for all procedural generation in this run.</summary>
    public ulong Seed { get; set; }

    /// <summary>How deep the player has descended in this run.</summary>
    public int Depth { get; set; }

    public override void _ExitTree()
    {
        // Autoloads leave the tree last. Collect now so managed wrappers that still reference Godot
        // resources are finalized before the engine reports "resources still in use at exit".
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
    }
}
