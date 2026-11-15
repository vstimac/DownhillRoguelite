using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Something that knows where trees and low rocks stand (a terrain chunk, a lab): the ground fog
/// lies only among them. Such a node joins <see cref="FogCover.Group"/> and calls
/// <see cref="FogCover.Changed"/> when it enters or leaves the scene.
/// </summary>
public interface IFogCover
{
    /// <summary>Adds its trees (world X, Z) and its low rocks (world X, Z and a weight: 1 for a knee-high stone).</summary>
    void GatherFogCover(List<Vector2> trees, List<Vector3> rocks);
}

/// <summary>The names the ground fog and the things it reads agree on, so neither needs the other's code.</summary>
public static class FogCover
{
    /// <summary>Nodes that implement <see cref="IFogCover"/>.</summary>
    public const string Group = "fog_cover";
    /// <summary>Skeletons whose bones part the fog (the carriers' bodies).</summary>
    public const string BodyGroup = "fog_bodies";

    /// <summary>Goes up whenever the set of cover changes; the fog lays its cover out again.</summary>
    public static int Version { get; private set; }

    public static void Changed()
    {
        Version++;
    }
}
