using Godot;

namespace DownhillRoguelite.Core;

/// <summary>Where a blade met a body: the zone, the spot (world space) and the bone's name ("" without bones).</summary>
public readonly record struct BodyHit(BodyZone Zone, Vector3 Point, string Bone);
