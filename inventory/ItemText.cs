using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>Words about items for the UI: a short description and their real properties.</summary>
public static class ItemText
{
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
