using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Another player's carrier as this machine shows it (co-op): its peer, where its feet are, whether it's
/// down, its torch, steering and sprint, whether it crouches and roughly how heavy its load is.
/// </summary>
public readonly record struct NetCarrier(long Peer, Vector3 Feet, bool Down, bool TorchLit = false, Vector3 InputDirection = default, bool Sprinting = false,
    bool Crouching = false, float LoadMass = 0.0f);
