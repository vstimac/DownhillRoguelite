using System;

namespace DownhillRoguelite.Core;

public static partial class GameEvents
{
    public static ExpeditionNote? CurrentExpedition { get; private set; }
    public static event Action<ExpeditionNote?>? ExpeditionChanged;

    public static void PublishExpedition(ExpeditionNote? note)
    {
        CurrentExpedition = note;
        ExpeditionChanged?.Invoke(note);
    }

    /// <summary>Finite rewards use acknowledgement; a full pack must not spend the reward.</summary>
    public static event Func<string, bool>? ItemGrantRequested;

    public static bool TryGrantItem(string resourcePath)
    {
        if (ItemGrantRequested == null) { return false; }
        foreach (Func<string, bool> grant in ItemGrantRequested.GetInvocationList())
        {
            if (grant(resourcePath)) { return true; }
        }
        return false;
    }
}
