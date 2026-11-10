namespace DownhillRoguelite.Core;

/// <summary>
/// A rigid body that rests frozen (a parked hand cart) but may be woken when a character shoves it
/// (BodyPush). Returns true when it is now free to move.
/// </summary>
public interface IPushable
{
    bool WakeForPush();
}
