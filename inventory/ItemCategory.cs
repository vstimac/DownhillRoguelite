namespace DownhillRoguelite.Inventory;

/// <summary>What an item is for. See docs/survival-loop.md.</summary>
public enum ItemCategory
{
    /// <summary>Restores hunger when eaten.</summary>
    Food,
    /// <summary>Restores thirst when drunk.</summary>
    Water,
    /// <summary>Gear and tools.</summary>
    Supply,
    /// <summary>Carried for value, trade and score.</summary>
    Cargo,
}
