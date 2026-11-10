using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Soft bumping between characters (the carrier, humanoid enemies, beasts, animals). Characters
/// don't hard-collide with each other (that snags, stops dead and pops); instead, each registered
/// character is eased apart from any other it overlaps, the lighter one giving way more. Called
/// after MoveAndSlide, the push moves the body with MoveAndCollide, so the world still blocks it.
/// The result: shouldering past a fiend shoves it aside, an ogre leaning in shoves you, and nobody
/// ends up inside anybody.
/// </summary>
public static class CharacterSpacing
{
    private sealed class Entry
    {
        public CharacterBody3D Body = null!;
        public float Radius;
        public float Mass;
    }

    private static readonly List<Entry> Entries = new();

    /// <summary>How fast overlap is pushed out (1/s): 8 clears most of an overlap in about a quarter second.</summary>
    public static float Stiffness { get; set; } = 8.0f;
    /// <summary>The fastest a character is pushed aside (m/s).</summary>
    public static float MaxPushSpeed { get; set; } = 3.0f;
    /// <summary>Characters further apart than this in height don't bump (one on a ledge above the other), in m.</summary>
    public static float MaxHeightApart { get; set; } = 1.6f;

    public static int Count => Entries.Count;

    public static void Register(CharacterBody3D body, float radius, float mass)
    {
        foreach (Entry entry in Entries)
        {
            if (entry.Body == body)
            {
                entry.Radius = radius;
                entry.Mass = mass;
                return;
            }
        }
        Entries.Add(new Entry { Body = body, Radius = radius, Mass = Mathf.Max(mass, 1.0f) });
    }

    public static void Unregister(CharacterBody3D body)
    {
        Entries.RemoveAll(entry => entry.Body == body);
    }

    /// <summary>Updates a character's weight (the carrier's changes with the pack).</summary>
    public static void SetMass(CharacterBody3D body, float mass)
    {
        foreach (Entry entry in Entries)
        {
            if (entry.Body == body)
            {
                entry.Mass = Mathf.Max(mass, 1.0f);
                return;
            }
        }
    }

    /// <summary>The sideways velocity that eases <paramref name="body"/> out of the others it overlaps (m/s).</summary>
    public static Vector3 PushFor(CharacterBody3D body)
    {
        Entry? self = null;
        foreach (Entry entry in Entries)
        {
            if (entry.Body == body)
            {
                self = entry;
                break;
            }
        }
        if (self == null)
        {
            return Vector3.Zero;
        }
        Vector3 here = body.GlobalPosition;
        Vector3 push = Vector3.Zero;
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            Entry other = Entries[i];
            if (!GodotObject.IsInstanceValid(other.Body) || !other.Body.IsInsideTree())
            {
                Entries.RemoveAt(i);
                continue;
            }
            if (other == self)
            {
                continue;
            }
            Vector3 there = other.Body.GlobalPosition;
            if (Mathf.Abs(there.Y - here.Y) > MaxHeightApart)
            {
                continue;
            }
            Vector3 apart = new(here.X - there.X, 0.0f, here.Z - there.Z);
            float reach = self.Radius + other.Radius;
            float distance = apart.Length();
            if (distance >= reach)
            {
                continue;
            }
            Vector3 away = Vector3.Right;
            if (distance > 0.001f)
            {
                away = apart / distance;
            }
            else if (body.GetInstanceId() < other.Body.GetInstanceId())
            {
                away = Vector3.Left;
            }
            float giving = other.Mass / (self.Mass + other.Mass);
            push += away * (reach - distance) * Stiffness * giving;
        }
        return push.LimitLength(MaxPushSpeed);
    }

    /// <summary>Moves <paramref name="body"/> out of the others it overlaps (call after MoveAndSlide).</summary>
    public static void Separate(CharacterBody3D body, float delta)
    {
        Vector3 push = PushFor(body);
        if (push.LengthSquared() < 0.0001f)
        {
            return;
        }
        body.MoveAndCollide(push * delta);
    }
}
