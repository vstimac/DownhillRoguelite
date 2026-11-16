using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// What an animal knows of the carrier it watches (the nearest one standing, host-side): where it
/// is, how fast it goes, whether it steers into the animal, sprints or falls, how heavy its load is
/// (louder footsteps), whether it crouches (the brake key: quieter, harder to see), whether its
/// torch is lit, and how much daylight there is (1 day, 0 night). Velocity of a guest comes from
/// its streamed feet; a guest's crouch and load aren't streamed.
/// </summary>
public readonly record struct CarrierSense(
    Vector3 Position,
    Vector3 Velocity,
    Vector3 Input,
    bool Sprinting,
    bool Falling,
    float LoadMass,
    bool Crouching = false,
    bool TorchLit = false,
    float Daylight = 1.0f)
{
    public static CarrierSense Still(Vector3 position) => new(position, Vector3.Zero, Vector3.Zero, false, false, 0.0f);
}

/// <summary>
/// A herd or flock: animals placed together that graze, move and flee together. The first living
/// member leads; it picks the next grazing spot along the slope (Drift) and the others follow to
/// their own places around it (Slot), keeping their spacing. One member's alarm runs through the
/// herd: an alert lifts every head, a bolt sends them all the same way (FleeDirection).
/// </summary>
public sealed class WildlifeHerd
{
    private readonly List<WildlifeAnimal> _members = new();

    public IReadOnlyList<WildlifeAnimal> Members => _members;
    /// <summary>Horizontal direction the herd drifts in while grazing (along the slope, mostly).</summary>
    public Vector3 Drift { get; set; }
    /// <summary>Where the leader last set off to; followers walk to their slots around it.</summary>
    public Vector3 LeaderTarget { get; private set; }
    /// <summary>Counts the leader's walks, so each follower follows each walk once.</summary>
    public int MoveSerial { get; private set; }
    /// <summary>The way the herd is fleeing (zero when calm).</summary>
    public Vector3 FleeDirection { get; set; }

    public WildlifeAnimal? Leader
    {
        get
        {
            foreach (WildlifeAnimal member in _members)
            {
                if (Alive(member))
                {
                    return member;
                }
            }
            return null;
        }
    }

    public void Join(WildlifeAnimal animal)
    {
        WildlifeAnimal? leader = Leader;
        _members.Add(animal);
        animal.Herd = this;
        if (leader == null || animal.Species == null)
        {
            animal.HerdSlot = Vector3.Zero;
            return;
        }
        // Its place round the leader: where it stands now, kept between the spacing and the spread.
        Vector3 offset = animal.GlobalPosition - leader.GlobalPosition;
        offset.Y = 0.0f;
        float spacing = animal.Species.HerdSpacing;
        float most = Mathf.Max(spacing * 1.5f, animal.Species.GroupSpread * 0.7f);
        if (offset.Length() < 0.01f)
        {
            offset = Vector3.Right.Rotated(Vector3.Up, _members.Count * 2.4f);
        }
        animal.HerdSlot = offset.Normalized() * Mathf.Clamp(offset.Length(), spacing * 1.2f, most);
    }

    public void Leave(WildlifeAnimal animal)
    {
        _members.Remove(animal);
        if (animal.Herd == this)
        {
            animal.Herd = null;
        }
    }

    /// <summary>The leader sets off: followers will walk to their slots around this point.</summary>
    public void LeaderMoves(Vector3 target)
    {
        LeaderTarget = target;
        MoveSerial++;
    }

    public static bool Alive(WildlifeAnimal animal)
    {
        return GodotObject.IsInstanceValid(animal) && !animal.IsQueuedForDeletion() && animal.IsInsideTree();
    }
}
