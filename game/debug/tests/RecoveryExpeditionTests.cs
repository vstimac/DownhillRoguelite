using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using DownhillRoguelite.Progression;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

public partial class RecoveryExpeditionTests : Node3D
{
    private int _failures;
    public override async void _Ready()
    {
        try { await Run(); }
        catch (Exception exception) { _failures++; GD.Print("FAIL  recovery test exception: " + exception); }
        if (_failures == 0) { GD.Print("RECOVERY EXPEDITION TESTS: PASS"); }
        else { GD.Print("RECOVERY EXPEDITION TESTS: FAIL"); }
        GetTree().Quit(Math.Min(_failures, 1));
    }

    private async Task Run()
    {
        Node3D world = GetNode<Node3D>("World");
        PlayerController player = world.GetNode<PlayerController>("Player");
        CarrierInventory inventory = player.GetNode<CarrierInventory>("Inventory");
        RecoveryExpedition expedition = world.GetNode<RecoveryExpedition>("RecoveryExpedition");
        ContractDirector director = world.GetNode<ContractDirector>("Contracts");
        BoonOfferings boons = world.GetNode<BoonOfferings>("Boons");
        RunState run = GetNode<RunState>("/root/RunState");
        await Wait(1);
        TestPlacement();
        Check("the expedition builds in the real world with a recoverable pump and a broken reserve",
            expedition.Contract != null && expedition.Workshop != null && expedition.Reserve != null
            && !expedition.Reserve.Restored && GameEvents.CurrentExpedition != null);
        ContractInstance job = expedition.Contract!;
        RecoveryWorkshop workshop = expedition.Workshop!;
        StationWaterReserve reserve = expedition.Reserve!;
        GameEvents.PublishStationReached(1);
        Check("empty station arrival earns no boon or water", boons.Stones.Count == 0 && !reserve.Interact(player));
        inventory.Clear();
        ItemDefinition filler = new() { ItemId = "recovery_test_filler", SizeCells = inventory.Grid!.Size, MassKg = 1 };
        Check("a full pack cannot recover or erase the pump", inventory.TryAdd(filler) && !workshop.Interact(player)
            && job.Status == ContractStatus.Offered && !inventory.OwnsCargo(job.InstanceId));
        inventory.Clear();
        float before = GameEvents.CurrentLoad.Mass;
        Check("recovery packs one real cargo copy with mass and cell occupancy", workshop.Interact(player)
            && inventory.OwnsCargo(job.InstanceId) && inventory.Grid.Items.Count == 1
            && Mathf.Abs(GameEvents.CurrentLoad.Mass - before - job.CargoMassKg) < 0.01f);
        Check("repeated recovery cannot duplicate cargo", !workshop.Interact(player) && inventory.Grid.Items.Count == 1);
        float condition = job.Condition;
        director.Knock(5);
        Check("existing fall and packing damage applies to recovered cargo", job.Condition < condition && job.Condition >= job.Definition.MinCondition);
        Check("cargo cannot be delivered at another station", director.Deliver(2) == ContractResult.WrongStation && !reserve.Restored);
        Check("delivery removes the cargo, restores water, and earns the destination boon",
            director.Deliver(1) == ContractResult.Ok && job.Status == ContractStatus.Delivered
            && !inventory.OwnsCargo(job.InstanceId) && reserve.Restored && reserve.Remaining == 3 && boons.Stones.Count == boons.OfferCount);
        Check("repeated delivery and restore cannot duplicate rewards", director.Deliver(1) != ContractResult.Ok
            && !reserve.Restore() && reserve.Remaining == 3 && boons.Stones.Count == boons.OfferCount);
        BoonStone chosen = boons.Stones[0];
        int boonCount = run.Boons.Count;
        Check("a removed boon reference cannot stack its effect twice", boons.Choose(chosen) && !boons.Choose(chosen) && run.Boons.Count == boonCount + 1);
        inventory.Clear();
        inventory.TryAdd(filler);
        Check("failed resupply leaves finite stock untouched", !reserve.Interact(player) && reserve.Remaining == 3);
        inventory.Clear();
        bool supplied = reserve.Interact(player) && reserve.Interact(player) && reserve.Interact(player);
        Check("resupply grants real storage items and stops when empty", supplied && reserve.Remaining == 0
            && inventory.AllItems.Count == 3 && !reserve.Interact(player));
        HashSet<string> ids = new();
        foreach (PackedItem copy in inventory.Grid.Items) { ids.Add(copy.InstanceId); }
        Check("each reserve flask retains independent physical ownership", ids.Count == 3 && !ids.Contains(""));
        run.StartNewRun(run.Seed);
        await Wait(0.3f);
        Check("new run resets the job, service, earned boons, and carried items", expedition.Contract != job
            && expedition.Contract?.Status == ContractStatus.Offered && expedition.Reserve?.Restored == false
            && boons.Stones.Count == 0 && run.Boons.Count == 0 && !inventory.OwnsCargo(job.InstanceId));
        inventory.Clear();
        RecoveryWorkshop second = expedition.Workshop!;
        second.Interact(player);
        director.Knock(1000);
        Check("destroyed recovery cargo grants no service or boon", expedition.Contract?.Status == ContractStatus.Failed
            && expedition.Reserve?.Restored == false && boons.Stones.Count == 0);
    }

    private void TestPlacement()
    {
        TerrainSettings settings = ResourceLoader.Load<TerrainSettings>("res://terrain/default_terrain.tres");
        bool all = true;
        HashSet<Vector3> locations = new();
        for (ulong seed = 1; seed <= 32; seed++)
        {
            TerrainRoute route = new(new TerrainGenerator(settings, seed));
            bool found = RecoveryPlacement.TryFind(route, seed, new Vector2(0.12f, 0.34f), new Vector2(10, 18), 30, 7, out Vector3 at);
            bool repeated = RecoveryPlacement.TryFind(route, seed, new Vector2(0.12f, 0.34f), new Vector2(10, 18), 30, 7, out Vector3 again);
            route.NearestPath(at, out float gap);
            all &= found && repeated && at == again && gap >= 7 && RecoveryPlacement.Walkable(route, route.RoutePoint(at.Z), at, 30);
            locations.Add(at);
        }
        Check("32 seeded mountains have repeatable off-trail sites with sampled walkable approaches", all && locations.Count == 32);
    }
    private async Task Wait(float seconds)
    {
        for (float t = 0; t < seconds; t += 1.0f / 60) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    }
    private void Check(string title, bool result)
    {
        if (result) { GD.Print("PASS  " + title); }
        else { _failures++; GD.Print("FAIL  " + title); }
    }
}
