namespace DownhillRoguelite.Core;

/// <summary>Unique slots; legacy Torso/Cape numeric values remain compatible with existing resources.</summary>
public static class EquipmentSlots
{
    public const int Count = 11;
    public static readonly EquipSlot[] All = { EquipSlot.Head, EquipSlot.Face, EquipSlot.InnerTorso,
        EquipSlot.OuterTorso, EquipSlot.Hands, EquipSlot.Legs, EquipSlot.Feet, EquipSlot.Back,
        EquipSlot.Waist, EquipSlot.Accessory };
    public static bool Valid(EquipSlot slot) => (int)slot > 0 && (int)slot < Count;
}
