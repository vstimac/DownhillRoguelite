using System;
using Godot;
using DownhillRoguelite.Player;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Core;

namespace DownhillRoguelite.Debug;

/// <summary>Character swaps must stay dressed and posed even while the world is paused by the menu.</summary>
public partial class CharacterPreviewTests : Node3D
{
    private int _failures;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        PlayerController player = GetNode<PlayerController>("World/Player");
        CarrierModel look = player.GetNode<CarrierModel>("CarrierModel");
        for (int i = 0; i < 30; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        GetTree().Paused = true;
        // One carrier; his outfit presets swap the same model.
        foreach (string id in new[] { "modular", "carrier_mountaineer", "modular", "carrier_foreman" })
        {
            bool shown = look.ShowCharacter(id);
            if (look.Follower != null) { look.Follower.RecordPose = true; }
            for (int i = 0; i < 12; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            Check(id + " is posed after a paused character swap", shown && look.Follower?.TargetPose.Length > 20);
            Skeleton3D? rig = look.Follower?.GetSkeleton();
            if (rig != null && look.Follower != null)
            {
                int arm = RigFollower.FindBone(rig, "LeftArm");
                int elbow = RigFollower.FindBone(rig, "LeftForeArm");
                Vector3 direction = rig.GlobalBasis * (look.Follower.TargetPose[elbow] - look.Follower.TargetPose[arm]);
                Check(id + " arms leave the T-pose while the menu is paused", Mathf.Abs(direction.Normalized().Dot(Vector3.Up)) > 0.65f);
            }
            Check(id + " has a base shirt and backpack", look.Clothing?.HasBaseOutfit == true);
        }
        if (look.CharacterId == "default")
        {
            CarrierInventory inventory = new() { Name = "Inventory", Backpack = GD.Load<BackpackDefinition>("res://inventory/default_backpack.tres"), StartingItemPaths = Array.Empty<string>() };
            player.AddChild(inventory);
            ItemDefinition shirt = GD.Load<ItemDefinition>("res://inventory/items/trail_shirt.tres");
            MeshInstance3D? native = look.Clothing?.Skeleton?.GetNodeOrNull<MeshInstance3D>("Ch17_Shirt");
            inventory.TryAdd(shirt, -1, false);
            Check("a successfully equipped shirt hides the covered native body partition", inventory.Equipment!.Wear(shirt.ItemId) && native?.Visible == false);
            Check("taking off the garment restores that partition", inventory.Equipment!.TakeOff(EquipSlot.InnerTorso) && native?.Visible == true);
        }
        string rosterPath = look.RosterPath;
        look.RosterPath = "res://debug/tests/clothing-fixtures/untextured_roster.tres";
        bool custom = look.ShowCharacter("untextured_fixture");
        for (int i = 0; i < 5; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        Check("a character without albedo textures falls back to the dressed default rig", !custom && look.Model == null && look.Clothing?.HasBaseOutfit == true);
        look.RosterPath = rosterPath;
        look.ShowCharacter("modular");
        GetTree().Paused = false;
        GetTree().Quit(Math.Min(_failures, 1));
    }

    private void Check(string name, bool passed)
    {
        if (passed) { GD.Print("PASS  " + name); }
        else { GD.Print("FAIL  " + name); _failures++; }
    }
}
