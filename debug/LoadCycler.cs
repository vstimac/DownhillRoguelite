using DownhillRoguelite.Balance;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Stand-in for the inventory until I1: publishes a test LoadProfile through GameEvents, and
/// cycles to the next one on L (debug_cycle_load).
/// </summary>
public partial class LoadCycler : Node
{
    private static readonly StringName ActionCycle = "debug_cycle_load";

    [Export] public string[] LoadPaths { get; set; } =
    {
        "res://balance/loads/empty.tres",
        "res://balance/loads/load_a_high_side.tres",
        "res://balance/loads/load_b_low_close.tres",
    };

    /// <summary>Index into LoadPaths published at startup.</summary>
    [Export] public int StartIndex { get; set; }

    private int _index;

    public override void _Ready()
    {
        _index = StartIndex;
        // Deferred so every system has subscribed before the first load goes out.
        CallDeferred(MethodName.PublishCurrent);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(ActionCycle) && LoadPaths.Length > 0)
        {
            _index = (_index + 1) % LoadPaths.Length;
            PublishCurrent();
        }
    }

    /// <summary>Publishes the load at LoadPaths[index]; used by tests too.</summary>
    public void Publish(int index)
    {
        _index = index;
        PublishCurrent();
    }

    private void PublishCurrent()
    {
        if (_index < 0 || _index >= LoadPaths.Length)
        {
            return;
        }

        if (ResourceLoader.Load(LoadPaths[_index]) is LoadProfile profile)
        {
            GameEvents.PublishLoad(profile.ToSnapshot());
        }
        else
        {
            GD.PushWarning($"LoadCycler: {LoadPaths[_index]} is not a LoadProfile.");
        }
    }
}
