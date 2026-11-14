using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// A creature's blow on another player's carrier (co-op), as their machine applies it: the damage,
/// the direction it shoves them (horizontal, unit), how hard (m/s), whether it knocks them down (with
/// that share of the push upward) or only staggers them (that high), and what struck them.
/// </summary>
public readonly record struct NetStrike(long Peer, float Damage, Vector3 Away, float Push, bool Knockdown, float Lift, float StaggerHeight, string Cause);
