using System;

namespace DownhillRoguelite.Core;

/// <summary>
/// Snares on the carrier: a spider web walked into (wildlife/SpiderWeb) clings to the legs and slows
/// every stride for a few seconds (player/PlayerController). Torn webs are announced by key, so co-op
/// can take them down for everyone.
/// </summary>
public static partial class GameEvents
{
    /// <summary>The carrier is snared: strides slow to speedFactor for this many seconds.</summary>
    public static event Action<float, float>? Snared;

    public static void PublishSnared(float seconds, float speedFactor) => Snared?.Invoke(seconds, speedFactor);

    /// <summary>The carrier tore or burned the spider web hanging between this pair of trees (its key).</summary>
    public static event Action<string>? WebTorn;
    /// <summary>A web is gone because someone else went through it (co-op): it goes without a snare.</summary>
    public static event Action<string>? WebGoneRequested;

    public static void PublishWebTorn(string key) => WebTorn?.Invoke(key);

    public static void RequestWebGone(string key) => WebGoneRequested?.Invoke(key);
}
