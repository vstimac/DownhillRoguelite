using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Lets a walking character (a CharacterBody3D, which on its own treats rigid bodies as walls) shove
/// the rigid bodies it walks into: the hand cart, crates, logs. After MoveAndSlide, every rigid body
/// it touched is sped up along the push toward the speed the character means to walk into it, scaled
/// by the character's weight against the body's (a crate goes nearly at walking pace, the loaded cart
/// slower). Topping it up every step beats its friction, so it slides along ahead of the character
/// instead of stopping them dead.
/// </summary>
public static class BodyPush
{
    /// <summary>
    /// Pushes what <paramref name="mover"/> touched in its last MoveAndSlide. Returns the share of its
    /// pace the character keeps while shoving (1 when it touched nothing it could shove): the body
    /// stopped it this step, and without this it would start again from standstill every step.
    /// </summary>
    /// <param name="moverMass">The character's weight with what it carries (kg).</param>
    /// <param name="velocity">The velocity the character means to walk at (before MoveAndSlide stopped it).</param>
    /// <param name="strength">Share of that speed (after the weights) the body is pushed up to (0 to 1).</param>
    /// <param name="maxSpeedChange">The most one step may change the body's speed (m/s), so a sprint can't fling it.</param>
    public static float Push(CharacterBody3D mover, float moverMass, Vector3 velocity, float strength, float maxSpeedChange)
    {
        float keep = 1.0f;
        for (int i = 0; i < mover.GetSlideCollisionCount(); i++)
        {
            KinematicCollision3D contact = mover.GetSlideCollision(i);
            if (contact.GetCollider() is not RigidBody3D body)
            {
                continue;
            }
            if (body.Freeze && (body is not IPushable pushable || !pushable.WakeForPush()))
            {
                continue;
            }
            Vector3 normal = contact.GetNormal();
            // Push sideways only: standing on a crate mustn't press it into the ground.
            Vector3 along = new(-normal.X, 0.0f, -normal.Z);
            if (along.LengthSquared() < 0.01f)
            {
                continue;
            }
            along = along.Normalized();
            float into = velocity.Dot(along);
            if (into <= 0.0f)
            {
                continue;
            }
            float share = moverMass / (moverMass + body.Mass);
            keep = Mathf.Min(keep, share * strength);
            float target = into * share * strength;
            float speedChange = Mathf.Min(target - body.LinearVelocity.Dot(along), maxSpeedChange);
            if (speedChange <= 0.0f)
            {
                continue;
            }
            body.ApplyImpulse(along * speedChange * body.Mass, contact.GetPosition() - body.GlobalPosition);
            body.Sleeping = false;
        }
        return keep;
    }
}
