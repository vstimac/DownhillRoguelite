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
            ItemCategory.Clothing => "Wear: the Wear button or V. Take it off: click it on the equipment sheet.",
            ItemCategory.Material => "Mend worn gear or the pack: the Mend button, at a station's fire.",
            ItemCategory.Cargo => "Cargo: carry it down. Its weight and place in the pack shift your balance.",
            _ => "Kept in the pack: it helps by being carried.",
        };
        if (OpensMap(item))
        {
            use = "Right-click or V opens the map (M) to draw on: paper and pencil let you mark it.";
        }
        return use + "\nDrop it: the Drop button, G on the hotbar, or drag it off the page to throw it.";
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
        if (item.Nutrition > 0.0f)
        {
            text += $"\nFood +{item.Nutrition:0}";
        }
        if (item.Hydration > 0.0f)
        {
            text += $"\nWater +{item.Hydration:0}";
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
        string path = $"res://inventory/items/{itemId}.tres";
        if (!ResourceLoader.Exists(path))
        {
            return null;
        }
        return ResourceLoader.Load(path) as ItemDefinition;
    }
}
