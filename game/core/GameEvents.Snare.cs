using System;

namespace DownhillRoguelite.Core;

/// <summary>
/// Snares on the carrier: a spider web walked into (wildlife/SpiderWeb) clings to the legs and slows
/// every stride for a few seconds (player/PlayerController).
/// </summary>
public static partial class GameEvents
{
    /// <summary>The carrier is snared: strides slow to speedFactor for this many seconds.</summary>
    public static event Action<float, float>? Snared;

    public static void PublishSnared(float seconds, float speedFactor) => Snared?.Invoke(seconds, speedFactor);
}
