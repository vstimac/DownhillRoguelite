namespace DownhillRoguelite.Core;

/// <summary>Where a piece of gear is worn (M14). None for things that aren't worn.</summary>
public enum EquipSlot
{
    None = 0,
    Head = 1,
    OuterTorso = 2,
    Torso = OuterTorso,
    Legs = 3,
    Hands = 4,
    Feet = 5,
    Back = 6,
    Cape = Back,
    Face = 7,
    InnerTorso = 8,
    Waist = 9,
    Accessory = 10,
}
