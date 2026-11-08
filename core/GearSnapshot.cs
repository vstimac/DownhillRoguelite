using DownhillRoguelite.Inventory;
namespace DownhillRoguelite.Core;

/// <summary>One worn piece of gear (or the backpack): what it is and how worn.</summary>
/// <param name="ItemId">Item id (empty when nothing is worn there).</param>
/// <param name="Name">Display name.</param>
/// <param name="Durability">Hit points left.</param>
/// <param name="MaxDurability">Hit points when new.</param>
public readonly record struct WornPiece(string ItemId, string Name, float Durability, float MaxDurability,
    ItemDefinition? Definition = null, ItemAppearance Appearance = default)
{
    // default(WornPiece) has a null id: that's empty too.
    public bool Empty => string.IsNullOrEmpty(ItemId);
    public bool Broken => !Empty && Durability <= 0.0f;
    public float Fraction
    {
        get
        {
            if (MaxDurability > 0.0f)
            {
                return Durability / MaxDurability;
            }
            return 0.0f;
        }
    }
}

/// <summary>
/// What the carrier wears and what it does (M14), published by the equipment whenever it changes:
/// a piece per slot (index = EquipSlot), the backpack's wear, and the combined effects of everything
/// that isn't broken: warmth and rain protection (against cold and chill), extra grip, extra area
/// the wind catches, pocket space, and the mass worn or carried in pockets (including broken gear).
/// </summary>
public readonly record struct GearSnapshot(
    WornPiece[] Slots,
    WornPiece Backpack,
    float Warmth,
    float RainProof,
    float Grip,
    float WindCatch,
    int Pockets,
    int PocketsUsed,
    float WornMass = 0.0f,
    float PocketMass = 0.0f,
    float SpeedFactor = 1.0f,
    float StaminaFactor = 1.0f,
    float WindProof = 0.0f,
    float FallProtection = 0.0f,
    float PhysicalProtection = 0.0f)
{
    public static GearSnapshot None => new(new WornPiece[EquipmentSlots.Count], new WornPiece("", "", 0, 0), 0, 0, 0, 0, 0, 0);

    public WornPiece In(EquipSlot slot)
    {
        int index = (int)slot;
        if (Slots == null || index < 0 || index >= Slots.Length)
        {
            return default;
        }
        return Slots[index];
    }
}
