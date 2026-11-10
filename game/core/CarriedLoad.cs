namespace DownhillRoguelite.Core;

/// <summary>Burden carried by the player, excluding the carrier's base body mass.</summary>
public static class CarriedLoad
{
    public static float Mass => GameEvents.CurrentLoad.Mass + GameEvents.CurrentGear.WornMass + GameEvents.CurrentGear.PocketMass;
}
