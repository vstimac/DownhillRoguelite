using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Another player's carrier as this machine shows it (co-op): its peer, where its feet are, whether it's
/// down (fallen, bleeding out or dead), its torch, steering and sprint, whether it crouches, roughly
/// how heavy its load is, and whether it is down for good or bleeding out (not just fallen: predators
/// close in on a fallen carrier, but leave one waiting for a friend's help alone).
/// </summary>
public readonly record struct NetCarrier(long Peer, Vector3 Feet, bool Down, bool TorchLit = false, Vector3 InputDirection = default, bool Sprinting = false,
    bool Crouching = false, float LoadMass = 0.0f, bool Bleeding = false);
