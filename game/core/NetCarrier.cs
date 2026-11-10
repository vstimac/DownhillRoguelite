using Godot;

namespace DownhillRoguelite.Core;

/// <summary>Another player's carrier as this machine shows it (co-op): its peer, where its feet are, and whether it's down.</summary>
public readonly record struct NetCarrier(long Peer, Vector3 Feet, bool Down);
