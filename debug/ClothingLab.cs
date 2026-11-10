using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Interactive equipment workbench; operates through the same inventory/equipment APIs as gameplay.</summary>
public partial class ClothingLab : Node3D
{
    [Export] public string SavePath { get; set; } = "user://clothing-lab.json";
    [Export] public bool WearPrototypeShirtOnStart { get; set; } = true;
    [Export] public string[] ItemPaths { get; set; } = {
        "res://inventory/items/trail_shirt.tres", "res://inventory/items/trail_backpack.tres",
        "res://inventory/items/oilskin_coat.tres", "res://inventory/items/wool_trousers.tres",
        "res://inventory/items/leather_gloves.tres", "res://inventory/items/wool_hat.tres", "res://inventory/items/hobnail_boots.tres" };
    private CarrierInventory _inventory = null!;
    private CarrierModel _look = null!;
    private Label? _status;
    private string _message = "Prototype clothing • walk, sprint, jump and inspect the bones in Remote.";
    private float _refresh;
    protected VBoxContainer ControlList { get; private set; } = null!;

    public override void _Ready()
    {
        PlayerController player = GetNode<PlayerController>("World/Player");
        _look = player.GetNode<CarrierModel>("CarrierModel");
        _inventory = new CarrierInventory { Name = "Inventory", Backpack = GD.Load<BackpackDefinition>("res://inventory/default_backpack.tres"), StartingItemPaths = ItemPaths };
        player.AddChild(_inventory);
        if (WearPrototypeShirtOnStart) { _inventory.Equipment!.Wear("trail_shirt"); }
        _inventory.Equipment!.Wear("trail_backpack");
        CanvasLayer layer = new() { Name = "ClothingControls" };
        AddChild(layer);
        ParchmentPanel sheet = new() { Title = "Clothing workshop", Position = new Vector2(20, 70), Size = new Vector2(310, 520) };
        layer.AddChild(sheet);
        ScrollContainer scroll = new() { Position = new Vector2(20, 60), Size = new Vector2(270, 440) };
        sheet.AddChild(scroll);
        VBoxContainer buttons = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        ControlList = buttons;
        scroll.AddChild(buttons);
        foreach (string path in ItemPaths)
        {
            ItemDefinition item = GD.Load<ItemDefinition>(path);
            AddButton(buttons, item.DisplayName, () => Toggle(item));
        }
        AddButton(buttons, "Character: Carrier / Vučić", () => {
            if (_look.CharacterId == "vucic") { _look.ShowCharacter("default"); }
            else { _look.ShowCharacter("vucic"); }
        });
        AddButton(buttons, "Wet / dirty / change fabric", () => {
            ItemAppearance before = _inventory.Equipment!.AppearanceOf(EquipSlot.InnerTorso);
            _inventory.Equipment.SetAppearance(EquipSlot.InnerTorso, new ItemAppearance(1 - before.Wetness, 1 - before.Dirt, (before.Variant + 1) % 3));
        });
        AddButton(buttons, "Wear shirt down by 25", () => _inventory.Equipment!.WearDown(EquipSlot.InnerTorso, 25));
        AddButton(buttons, "Save carried load", () => {
            if (EquipmentPersistence.Save(_inventory, SavePath, out string problem)) { _message = "Saved to " + SavePath; }
            else { _message = problem; }
        });
        AddButton(buttons, "Load carried load", () => {
            if (EquipmentPersistence.Load(_inventory, SavePath, out string problem)) { _message = "Restored saved gear and storage."; }
            else { _message = problem; }
        });
        AddButton(buttons, "Drop shirt for pickup (E)", () => {
            if (_inventory.Equipment!.WornIn(EquipSlot.InnerTorso) != null && !_inventory.Equipment.TakeOff(EquipSlot.InnerTorso)) { _message = "No room to take it off."; return; }
            GameEvents.RequestDrop("trail_shirt");
        });
        _status = UiStyle.MakeLabel("", 15);
        _status.Position = new Vector2(350, 70); _status.Size = new Vector2(540, 110);
        layer.AddChild(_status);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private static void AddButton(Node parent, string text, System.Action action)
    {
        InkButton button = new() { Text = text, Paper = true, Focusable = true, FontSize = 15, CustomMinimumSize = new Vector2(260, 25) };
        button.Pressed += action;
        parent.AddChild(button);
    }

    private void Toggle(ItemDefinition item)
    {
        CarrierEquipment gear = _inventory.Equipment!;
        bool result;
        if (gear.WornIn(item.Slot)?.ItemId == item.ItemId) { result = gear.TakeOff(item.Slot); }
        else { result = gear.Wear(item.ItemId); }
        if (!result) { _message = "No room or item is not currently carried. Pick it up first."; }
        else { _message = "Changed " + item.DisplayName; }
    }

    public override void _Process(double delta)
    {
        _refresh -= (float)delta;
        if (_status == null || _refresh > 0) { return; }
        _refresh = 0.2f;
        GearSnapshot gear = GameEvents.CurrentGear;
        _status.Text = $"{_message}\nMass {CarriedLoad.Mass:0.00}kg • speed ×{gear.SpeedFactor:0.00} • warmth {gear.Warmth:P0}\nEsc releases mouse • click game to look • F9 toggles every-5-frame recording";
    }
}
