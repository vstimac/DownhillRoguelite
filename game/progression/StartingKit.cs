using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// The one thing earned across deaths (M12): gear the carrier starts a run with, unlocked by the
/// best depth ever reached (RunState.Records). Entries are parallel arrays: ItemPaths[i] is given
/// once BestDepth reaches Depths[i]. At the start of every run the unlocked items are granted
/// (GameEvents.ItemsGranted, packed by the inventory) and listed in RunState.StartingKit.
/// </summary>
public partial class StartingKit : Node
{
    [Export] public float[] Depths { get; set; } = { 300.0f, 600.0f, 1200.0f, 1800.0f, 3000.0f };
    [Export] public string[] ItemPaths { get; set; } =
    {
        "res://inventory/items/water_flask.tres",
        "res://inventory/items/ration.tres",
        "res://inventory/items/dried_fruit.tres",
        "res://inventory/items/water_skin.tres",
        "res://inventory/items/med_kit.tres",
    };

    /// <summary>What every run starts with, whatever the best depth (a torch for the first night).</summary>
    [Export] public string[] AlwaysItemPaths { get; set; } = { "res://inventory/items/torch.tres" };

    /// <summary>Where the preset's clothes (CarrierCharacter.StartingWear, by item id) are found.</summary>
    [Export] public string ItemFolder { get; set; } = "res://inventory/items/";
    /// <summary>Clothes the chosen preset put on at the start of this run (tests).</summary>
    public int PresetWorn { get; private set; }

    private RunState? _run;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        GameEvents.RespawnRequested += GrantLater;
        GrantLater();
    }

    public override void _ExitTree()
    {
        GameEvents.RespawnRequested -= GrantLater;
    }

    /// <summary>The items unlocked by a best depth, in unlock order.</summary>
    public List<string> Unlocked(int bestDepth)
    {
        List<string> items = new();
        for (int i = 0; i < Depths.Length && i < ItemPaths.Length; i++)
        {
            if (bestDepth >= Depths[i])
            {
                items.Add(ItemPaths[i]);
            }
        }
        return items;
    }

    /// <summary>After every other system has reset for the new run (the inventory empties first).</summary>
    private void GrantLater()
    {
        CallDeferred(MethodName.Grant);
    }

    /// <summary>Gives the unlocked gear and the chosen preset's clothes (at every run's start; tests call it directly).</summary>
    public void Grant()
    {
        if (_run == null)
        {
            return;
        }
        List<string> items = Unlocked(_run.Records.BestDepth);
        foreach (string path in AlwaysItemPaths)
        {
            if (ResourceLoader.Exists(path))
            {
                items.Insert(0, path);
            }
        }
        List<string> wear = new();
        foreach (string id in _run.Character.Definition?.StartingWear ?? System.Array.Empty<string>())
        {
            string path = $"{ItemFolder}{id}.tres";
            if (ResourceLoader.Exists(path))
            {
                wear.Add(id);
            }
        }
        _run.StartingKit.Clear();
        foreach (string path in items)
        {
            if (Inventory.ItemText.AtPath(path) is Resource item && item.Get("DisplayName").AsString() is string name && name.Length > 0)
            {
                _run.StartingKit.Add(name);
            }
        }
        if (items.Count > 0)
        {
            GameEvents.PublishItemsGranted(items.ToArray());
        }
        // The preset's clothes, each put on as soon as it is given (so it never needs room in the pack).
        PresetWorn = 0;
        foreach (string id in wear)
        {
            string path = $"{ItemFolder}{id}.tres";
            GameEvents.PublishItemsGranted(new[] { path });
            GameEvents.RequestWear(id);
            PresetWorn++;
        }
    }
}
