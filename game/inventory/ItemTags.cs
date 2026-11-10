using System;

namespace DownhillRoguelite.Inventory;

/// <summary>Properties that give items special rules (damage, sloshing, value). Combine freely.</summary>
[Flags]
public enum ItemTags
{
    None = 0,
    Fragile = 1,
    Liquid = 2,
    Bulky = 4,
    Valuable = 8,
}
