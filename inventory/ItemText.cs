using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>Words about items for the UI: a short description and their real properties.</summary>
public static class ItemText
{
    /// <summary>How to use an item, in a line or two (the inventory page's legend).</summary>
    public static string HowToUse(ItemDefinition? item)
    {
        if (item == null)
        {
            return "";
        }
        string use = item.Category switch
        {
            ItemCategory.Food => "Eat: the Eat button, F, or V on the hotbar.",
            ItemCategory.Water => "Drink: the Drink button, Q, or V on the hotbar.",
            ItemCategory.Clothing => "Wear: double-click, right-click, the Wear button, V, or drag it onto the equipment sheet. Take it off: click it on the sheet.",
            ItemCategory.Material => "Mend worn gear or the pack: the Mend button, at a station's fire.",
            ItemCategory.Cargo => "Cargo: carry it down. Its weight and place in the pack shift your balance.",
            _ => "Kept in the pack: it helps by being carried.",
        };
        if (OpensMap(item))
        {
            use = "Right-click or V opens the map (M) to draw on: paper and pencil let you mark it.";
        }
        else if (item.UsableAlone)
        {
            use = "Use it up: the Use button, right-click, or V on the hotbar.";
        }
        return use + "\nDrop it: Shift+right-click, the Drop button, G on the hotbar, or drag it off the page to throw it.";
    }

    /// <summary>Paper and pencil: using them (right-click, V) opens the map to draw on.</summary>
    public static bool OpensMap(ItemDefinition? item)
    {
        return item != null && (item.ItemId == "paper" || item.ItemId == "pencil");
    }

    /// <summary>Opens (or closes) the map as if M were pressed.</summary>
    public static void ToggleMap()
    {
        Input.ParseInputEvent(new InputEventAction { Action = "map_toggle", Pressed = true });
        Input.ParseInputEvent(new InputEventAction { Action = "map_toggle", Pressed = false });
    }

    public static string Describe(ItemDefinition? item)
    {
        if (item == null)
        {
            return "";
        }
        if (item.Description.Length > 0) { return item.Description; }
        if (item.Category == ItemCategory.Food)
        {
            return "Something to eat on the way down.";
        }
        if (item.Category == ItemCategory.Water)
        {
            return "Water, worth its weight.";
        }
        if (item.Category == ItemCategory.Cargo)
        {
            return "Cargo. Heavy, and someone wants it.";
        }
        if (item.Category == ItemCategory.Clothing)
        {
            return $"Worn on the {item.Slot.ToString().ToLowerInvariant()}. It wears out; mend it at a station.";
        }
        if (item.Category == ItemCategory.Material)
        {
            return $"Mends {string.Join(" and ", item.MendsTypes)} at a station.";
        }
        return "Gear for the descent.";
    }

    public static string Properties(ItemDefinition? item)
    {
        if (item == null)
        {
            return "";
        }
        string text = $"Weight {item.MassKg:0.0} kg  ·  {item.SizeCells.X}×{item.SizeCells.Y}×{item.SizeCells.Z} cells";
        if (item.Slot != EquipSlot.None) { text += $"\nSlot: {item.Slot} · {item.Rarity}"; }
        if (item.SpeedMultiplier != 1) { text += $"\nMovement ×{item.SpeedMultiplier:0.00}"; }
        if (item.StaminaMultiplier != 1) { text += $"\nStamina cost ×{item.StaminaMultiplier:0.00}"; }
        if (item.WindProof > 0) { text += $"\nWind resistance {item.WindProof:P0}"; }
        if (item.FallProtection > 0) { text += $"\nFall protection {item.FallProtection:P0}"; }
        if (item.Nutrition > 0.0f)
        {
            text += $"\nFood +{item.Nutrition:0}";
        }
        if (item.Hydration > 0.0f)
        {
            text += $"\nWater +{item.Hydration:0}";
        }
        if (item.UseStamina > 0.0f)
        {
            text += $"\nStamina +{item.UseStamina:0}";
        }
        if (item.UseWakes.Length > 0 && item.UseWakeChance > 0.0f)
        {
            text += $"\n...or ({item.UseWakeChance * 100.0f:0} % chance) it wakes something";
        }
        if (item.Warmth > 0.0f)
        {
            text += $"\nWarmth {item.Warmth * 100.0f:0} %";
        }
        if (item.RainProof > 0.0f)
        {
            text += $"\nKeeps off rain {item.RainProof * 100.0f:0} %";
        }
        if (item.GripBonus > 0.0f)
        {
            text += $"\nGrip +{item.GripBonus * 100.0f:0} %";
        }
        if (item.WindCatch > 0.0f)
        {
            text += "\nCatches the wind";
        }
        if (item.Pockets > 0)
        {
            text += $"\nPockets {item.Pockets}";
        }
        if (item.MaxDurability > 0.0f)
        {
            text += $"\nDurability {item.MaxDurability:0}";
        }
        if (item.MendAmount > 0.0f)
        {
            text += $"\nMends {item.MendAmount:0}";
        }
        return text;
    }

    /// <summary>The item definition for an id (items live at res://inventory/items/&lt;id&gt;.tres).</summary>
    public static ItemDefinition? Find(string itemId)
    {
        if (Definitions.TryGetValue(itemId, out ItemDefinition? cached))
        {
            return cached;
        }
        string path = $"res://inventory/items/{itemId}.tres";
        ItemDefinition? item = null;
        if (ResourceLoader.Exists(path))
        {
            item = ResourceLoader.Load(path) as ItemDefinition;
        }
        Definitions[itemId] = item;
        return item;
    }

    /// <summary>
    /// Definitions stay loaded once read: loading them afresh each time let .NET finalize one on its own
    /// thread while the game loaded the same file again, a crash (Godot: "gchandle.is_released").
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<string, ItemDefinition?> Definitions = new();
}
