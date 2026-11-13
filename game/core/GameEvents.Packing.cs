using System;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// The pack pages' side of the hub: the layout the inventory publishes, what the pages ask of it,
/// and the progress of setting the pack down and putting it back on.
/// </summary>
public static partial class GameEvents
{
    /// <summary>Raised by the inventory whenever the pack's contents or their places change.</summary>
    public static event Action<PackLayout>? PackLayoutChanged;

    /// <summary>The most recent layout, so pages created later can catch up.</summary>
    public static PackLayout CurrentPackLayout { get; private set; } = PackLayout.Empty;

    public static void PublishPackLayout(PackLayout layout)
    {
        CurrentPackLayout = layout;
        PackLayoutChanged?.Invoke(layout);
    }

    /// <summary>True while the pack stands open on the ground and can be rearranged.</summary>
    public static bool IsPackOpen { get; private set; }

    /// <summary>Raised when the set-down pack opens for rearranging (true) or is closed to go back on (false).</summary>
    public static event Action<bool>? PackOpenChanged;

    public static void PublishPackOpen(bool open)
    {
        if (IsPackOpen == open)
        {
            return;
        }
        IsPackOpen = open;
        PackOpenChanged?.Invoke(open);
    }

    /// <summary>
    /// Raised every frame while the pack is being set down (settingDown true) or put back on, with
    /// how far along it is from 0 to 1; a negative value means it stopped.
    /// </summary>
    public static event Action<float, bool>? PackingProgress;

    public static void PublishPackingProgress(float progress, bool settingDown)
    {
        PackingProgress?.Invoke(progress, settingDown);
    }

    /// <summary>Raised by a short press of the pack key: show or hide the quick look into the pack.</summary>
    public static event Action? PackQuickViewRequested;

    public static void RequestPackQuickView()
    {
        PackQuickViewRequested?.Invoke();
    }

    /// <summary>Asks the inventory to put a copy at a cell in an orientation (from the mat or from elsewhere in the pack).</summary>
    public static event Action<string, Vector3I, int>? PackPlaceRequested;

    public static void RequestPackPlace(string instanceId, Vector3I origin, int orientation)
    {
        PackPlaceRequested?.Invoke(instanceId, origin, orientation);
    }

    /// <summary>Asks the inventory to take a copy out of the pack and lay it beside it.</summary>
    public static event Action<string>? PackTakeOutRequested;

    public static void RequestPackTakeOut(string instanceId)
    {
        PackTakeOutRequested?.Invoke(instanceId);
    }

    /// <summary>Asks the inventory to pack everything again, heaviest first, low and close to the back.</summary>
    public static event Action? PackArrangeRequested;

    public static void RequestPackArrange()
    {
        PackArrangeRequested?.Invoke();
    }

    /// <summary>Asks for the open pack to be closed and put back on (the page's Done button).</summary>
    public static event Action? PackCloseRequested;

    public static void RequestPackClose()
    {
        PackCloseRequested?.Invoke();
    }
}
