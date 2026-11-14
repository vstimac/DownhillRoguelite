using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// A creature co-op shares (network/NetMobs): a humanoid enemy or a night beast. The machine whose
/// director brought it simulates it (its owner) and sends where it is and what it plays; the other
/// machines show it as a puppet that never thinks for itself. A blow on a puppet is passed to its
/// owner (GameEvents.PublishNetMobHit), who applies it to the real one.
/// </summary>
public interface INetMob
{
    /// <summary>0: humanoid enemy, 1: night beast, 2: wildlife, 3: Tuntun.</summary>
    int NetKind { get; }
    /// <summary>Its species' resource path (every machine loads the same one).</summary>
    string NetSpecies { get; }
    /// <summary>Shown for another machine: moved and animated from the network only.</summary>
    bool NetPuppet { get; }
    /// <summary>Health left, 0 to 1.</summary>
    float NetHealth { get; }
    /// <summary>Beaten: dead (an enemy) or fleeing for the night (a beast).</summary>
    bool NetDown { get; }
    /// <summary>What its animator plays now, how far in, and how fast.</summary>
    string NetClip { get; }
    float NetClipPosition { get; }
    float NetClipSpeed { get; }
    /// <summary>Additional model placement (slope tilt or a procedural prop swing).</summary>
    Transform3D NetVisual => Transform3D.Identity;
    void NetShowVisual(Transform3D visual) { }

    /// <summary>A puppet shows what its owner says: the clip (kept in step), its health, and whether it is down.</summary>
    void NetShow(string clip, float clipPosition, float clipSpeed, float health, bool down);
}
