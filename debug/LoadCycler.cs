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
    // Loaded once and held: reloading every time let the GC finalize a profile's managed wrapper
    // on its thread while the main thread re-wrapped the same resource (fatal gchandle error).
    private readonly System.Collections.Generic.List<LoadProfile?> _profiles = new();

    public override void _Ready()
    {
        foreach (string path in LoadPaths)
        {
            LoadProfile? profile = ResourceLoader.Load(path) as LoadProfile;
            if (profile == null)
            {
                GD.PushWarning($"LoadCycler: {path} is not a LoadProfile.");
            }
            _profiles.Add(profile);
        }
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
        if (_index < 0 || _index >= _profiles.Count)
        {
            return;
        }

        LoadProfile? profile = _profiles[_index];
        if (profile != null)
        {
            GameEvents.PublishLoad(profile.ToSnapshot());
        }
    }

    public override void _ExitTree()
    {
        _profiles.Clear();
    }
}
