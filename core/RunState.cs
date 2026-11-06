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

    /// <summary>Deepest point reached this run, in metres below the start.</summary>
    public int Depth { get; set; }

    public override void _Ready()
    {
        GameEvents.DepthChanged += OnDepthChanged;
    }

    private void OnDepthChanged(float depth)
    {
        Depth = Mathf.FloorToInt(depth);
    }

    public override void _ExitTree()
    {
        GameEvents.DepthChanged -= OnDepthChanged;

        // Autoloads leave the tree last. Collect now so managed wrappers that still reference Godot
        // resources are finalized before the engine reports "resources still in use at exit".
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
    }
}
