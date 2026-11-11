using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for M6: the run starts with an empty pack; the clock runs at the difficulty's
/// speed; hunger and thirst drain, weaken stamina and eventually hurt; picking up, eating and
/// drinking work through the pack; stamina is endurance (walking drains it, load and slope cost
/// more, exhaustion stops the carrier, standing recovers it); supplies are deterministic, follow
/// the route, thin out with depth and don't come back once taken; death restarts the run.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/SurvivalTests.tscn
/// </summary>
public partial class SurvivalTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public SupplySpawner? Supplies { get; set; }
    [Export] public float SettleTime { get; set; } = 1.0f;
    /// <summary>Clock speed-up used to drain needs quickly (in-game hours per real second ≈ this / 90).</summary>
    [Export] public float FastClock { get; set; } = 90.0f;
    /// <summary>Supplies per m² near the route must be at least this multiple of elsewhere.</summary>
    [Export] public float MinRouteDensityRatio { get; set; } = 5.0f;

    private readonly List<string> _failures = new();
    private CarrierInventory? _inventory;
    private CarrierNeeds? _needs;
    private CarrierStamina? _stamina;
    private CarrierHealth? _health;
    private PickupInteractor? _interactor;
    private WorldClock? _clock;
    private RunState? _run;
    private int _consumeFailures;

    public override async void _Ready()
    {
        if (PlayerBody == null || Streamer == null || Supplies == null)
        {
            GD.PushError("SurvivalTests: PlayerBody, Streamer and Supplies must be assigned.");
            GetTree().Quit(1);
            return;
        }
        _inventory = PlayerBody.GetNodeOrNull<CarrierInventory>("Inventory");
        _needs = PlayerBody.GetNodeOrNull<CarrierNeeds>("Needs");
        _stamina = PlayerBody.Stamina;
        _health = PlayerBody.Health;
        _interactor = PlayerBody.GetNodeOrNull<PickupInteractor>("Interactor");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        _run = GetNodeOrNull<RunState>("/root/RunState");
        if (_inventory == null || _needs == null || _stamina == null || _health == null || _interactor == null || _clock == null || _run == null)
        {
            GD.PushError("SurvivalTests: player is missing Inventory, Needs, Stamina, Health or Interactor, or autoloads are missing.");
            GetTree().Quit(1);
            return;
        }

        GameEvents.ConsumeFailed += OnConsumeFailed;
        await Place(PlayerBody, Streamer.SpawnPoint);

        TestEmptyStart(_inventory);
        await TestClock(_clock);
        await TestNeeds(_needs, _stamina, _health, _clock);
        await TestPickupAndEat(PlayerBody, _inventory, _needs, _interactor);
        TestBestFit(_inventory);
        await TestNoRoom(PlayerBody, _inventory, _interactor);
        await TestStamina(PlayerBody, _stamina, _inventory);
        await TestSupplies(PlayerBody, Supplies, _run);
        await TestHighlight(PlayerBody);
        await TestDeathRestart(PlayerBody, _inventory, _needs, _health, _clock, Supplies);

        GameEvents.ConsumeFailed -= OnConsumeFailed;
        if (_failures.Count == 0)
        {
            GD.Print("SURVIVAL TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"SURVIVAL TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void OnConsumeFailed(SupplyNeed need)
    {
        _consumeFailures++;
    }

    private void TestEmptyStart(CarrierInventory inventory)
    {
        float empty = inventory.Backpack!.EmptyMass;
        Check("run starts with an empty pack", inventory.AllItems.Count == 0 && Mathf.IsEqualApprox(GameEvents.CurrentLoad.Mass, empty),
            $"{inventory.AllItems.Count} items, load {GameEvents.CurrentLoad.Mass:0.0} kg (empty pack {empty:0.0} kg)");
    }

    private async Task TestClock(WorldClock clock)
    {
        double before = clock.TotalHours;
        await WaitSeconds(2.0f);
        double passed = clock.TotalHours - before;
        double expected = 2.0 * 24.0 / (_run!.DifficultySettings.DayLengthMinutes * 60.0);
        Check("clock runs at the difficulty's day length", Mathf.Abs((float)(passed - expected)) <= expected * 0.05,
            $"{passed * 60.0:0.00} in-game minutes in 2 s (expected {expected * 60.0:0.00}, {_run.DifficultySettings.DisplayName})");
    }

    private async Task TestNeeds(CarrierNeeds needs, CarrierStamina stamina, CarrierHealth health, WorldClock clock)
    {
        needs.Refill();
        clock.DebugTimeScale = FastClock;
        await WaitSeconds(2.0f);
        clock.DebugTimeScale = 1.0f;
        float hungerLost = CarrierNeeds.Full - needs.Hunger;
        float thirstLost = CarrierNeeds.Full - needs.Thirst;
        Check("hunger and thirst drain, thirst faster", hungerLost > 0.5f && thirstLost > hungerLost,
            $"lost hunger {hungerLost:0.0}, thirst {thirstLost:0.0}");

        needs.SetNeeds(10.0f, 10.0f);
        await WaitSeconds(0.1f);
        Check("thirst slows stamina recovery, hunger lowers its maximum",
            Mathf.IsEqualApprox(stamina.RecoveryMultiplier, needs.ThirstyRecovery) && Mathf.IsEqualApprox(stamina.MaxMultiplier, needs.HungryMaxStamina),
            $"recovery ×{stamina.RecoveryMultiplier:0.00}, max ×{stamina.MaxMultiplier:0.00}");

        health.Refill();
        needs.SetNeeds(0.0f, 0.0f);
        clock.DebugTimeScale = FastClock;
        await WaitSeconds(2.0f);
        clock.DebugTimeScale = 1.0f;
        float lost = health.Max - health.Current;
        Check("starving and dehydrated carrier loses health", lost > 5.0f && !health.Dead, $"lost {lost:0.0} health");
        health.Refill();
        needs.Refill();
        await WaitSeconds(0.1f);
    }

    private async Task TestPickupAndEat(PlayerController player, CarrierInventory inventory, CarrierNeeds needs, PickupInteractor interactor)
    {
        ItemDefinition ration = Load("ration");
        WorldItem item = await DropInFront(player, ration);
        float before = GameEvents.CurrentLoad.Mass;
        bool focused = interactor.Focused == item;
        bool picked = interactor.InteractNow();
        await WaitSeconds(0.1f);
        Check("E picks up the item in front into the pack",
            focused && picked && inventory.AllItems.Contains(ration) && !IsInstanceValid(item) && Mathf.IsEqualApprox(GameEvents.CurrentLoad.Mass, before + ration.MassKg),
            $"focused {focused}, picked {picked}, load {before:0.0} -> {GameEvents.CurrentLoad.Mass:0.0} kg");

        needs.SetNeeds(50.0f, CarrierNeeds.Full);
        GameEvents.RequestConsume(SupplyNeed.Hunger, CarrierNeeds.Full - needs.Hunger);
        await WaitSeconds(0.05f);
        Check("eating consumes the food and restores hunger",
            Near(needs.Hunger, 50.0f + ration.Nutrition) && !inventory.AllItems.Contains(ration) && Mathf.IsEqualApprox(GameEvents.CurrentLoad.Mass, before),
            $"hunger 50 -> {needs.Hunger:0}, load {GameEvents.CurrentLoad.Mass:0.0} kg");

        int failuresBefore = _consumeFailures;
        GameEvents.RequestConsume(SupplyNeed.Thirst, 50.0f);
        await WaitSeconds(0.05f);
        Check("drinking with no water packed fails", _consumeFailures == failuresBefore + 1, $"{_consumeFailures - failuresBefore} failure events");
        needs.Refill();
    }

    private void TestBestFit(CarrierInventory inventory)
    {
        inventory.Clear();
        inventory.TryAdd(Load("dried_fruit"));
        inventory.TryAdd(Load("ration"));
        inventory.TryAdd(Load("food_bag"));
        string? small = inventory.BestConsumable(ItemCategory.Food, 30.0f)?.Definition.ItemId;
        string? large = inventory.BestConsumable(ItemCategory.Food, 90.0f)?.Definition.ItemId;
        Check("eats the smallest food that covers the need, else the biggest", small == "ration" && large == "food_bag",
            $"need 30 -> {small}, need 90 -> {large}");
        inventory.Clear();
    }

    private async Task TestNoRoom(PlayerController player, CarrierInventory inventory, PickupInteractor interactor)
    {
        ItemDefinition tent = Load("tent");
        int added = 0;
        while (inventory.TryAdd(tent) && added < 50)
        {
            added++;
        }
        WorldItem item = await DropInFront(player, Load("water_skin"));
        bool picked = interactor.InteractNow();
        await WaitSeconds(0.1f);
        Check("a full pack leaves the item on the ground", added > 0 && !picked && IsInstanceValid(item),
            $"{added} tents packed, picked {picked}, item still there {IsInstanceValid(item)}");
        if (IsInstanceValid(item))
        {
            item.QueueFree();
        }
        inventory.Clear();
        await WaitSeconds(0.1f);
    }

    private async Task TestStamina(PlayerController player, CarrierStamina stamina, CarrierInventory inventory)
    {
        float flat = stamina.WalkDrainPerSecond(0.0f, 0.0f, false, false);
        float downhill = stamina.WalkDrainPerSecond(0.0f, 0.3f, false, false);
        float uphill = stamina.WalkDrainPerSecond(0.0f, -0.3f, false, false);
        float sprint = stamina.WalkDrainPerSecond(0.0f, 0.0f, false, true);
        float loadedSprint = stamina.WalkDrainPerSecond(20.0f, 0.0f, false, true);
        Check("walking on the flat and downhill is free; climbing costs a little, sprinting much more, a load more still",
            flat == 0.0f && downhill == 0.0f && uphill > 0.0f && sprint > uphill && loadedSprint > sprint,
            $"per second: flat {flat:0.00}, downhill {downhill:0.00}, uphill {uphill:0.00}, sprint {sprint:0.0}, loaded sprint {loadedSprint:0.0}");

        await Place(player, _run!.Route!.RoutePoint(_run.Route.StartZ - 20.0f));
        stamina.SetCurrent(50.0f);
        await Walk(player, 3.0f);
        float afterWalk = stamina.Current;
        Check("stamina comes back while walking downhill", afterWalk > 60.0f, $"50 -> {afterWalk:0.0} after 3 s of walking");

        stamina.SetCurrent(0.0f);
        Vector3 before = player.GlobalPosition;
        await Walk(player, 1.5f);
        float moved = new Vector2(player.GlobalPosition.X - before.X, player.GlobalPosition.Z - before.Z).Length();
        Check("out of breath, the carrier still walks but can't sprint", moved > 0.6f && !stamina.CanSprint,
            $"moved {moved:0.00} m in 1.5 s while out of breath, can sprint {stamina.CanSprint}");

        float wait = (stamina.ExhaustedRecoverLevel / stamina.EffectiveRecovery) + stamina.RecoveryDelay + 0.5f;
        await WaitSeconds(wait);
        Check("a short rest and it can sprint again", !stamina.Exhausted && stamina.CanSprint, $"rested {wait:0.0} s: out of breath {stamina.Exhausted}");
        stamina.Refill();
    }

    private static readonly ulong[] DepthSeeds = { 1234, 999, 31337, 2024, 77, 4096, 51, 8675309 };

    private async Task TestSupplies(PlayerController player, SupplySpawner supplies, RunState run)
    {
        IRouteQuery route = run.Route!;
        float multiplier = run.DifficultySettings.SupplyMultiplier;
        bool same = true;
        int total = 0;
        int nearRoute = 0;
        int shallow = 0;
        int deep = 0;
        bool differs = false;
        for (int row = 0; row < 60; row++)
        {
            for (int column = -4; column <= 4; column++)
            {
                Vector2I cell = new(column, -row - 1);
                List<SupplySpawner.Slot> a = supplies.PlanCell(cell, 1234, route, multiplier);
                List<SupplySpawner.Slot> b = supplies.PlanCell(cell, 1234, route, multiplier);
                List<SupplySpawner.Slot> other = supplies.PlanCell(cell, 999, route, multiplier);
                same = same && a.Count == b.Count && a.TrueForAll(slot => b.Contains(slot));
                differs = differs || a.Count != other.Count || !a.TrueForAll(slot => other.Contains(slot));
                foreach (SupplySpawner.Slot slot in a)
                {
                    total++;
                    if (Mathf.Abs(slot.X - route.RoutePoint(slot.Z).X) <= supplies.Table!.RouteBand)
                    {
                        nearRoute++;
                    }
                }
                // Thinning is a trend, so it's counted over several mountains (one seed is too few items).
                // Side paths are left out: how many branch off depends on the stage, not the depth.
                foreach (ulong seed in DepthSeeds)
                {
                    int planned = supplies.PlanCell(cell, seed, route, multiplier).Count(slot => !slot.Key.StartsWith("b") && !slot.Key.StartsWith("c"));
                    if (row < 15)
                    {
                        shallow += planned;
                    }
                    else if (row >= 45)
                    {
                        deep += planned;
                    }
                }
            }
        }
        Check("supplies are the same for the same seed and differ for another", same && differs && total > 0, $"{total} planned in 540 cells");
        // Density per m² in the band along the route against everywhere else in the 9-cell-wide strip.
        float length = 60.0f * supplies.CellSize;
        float bandArea = 2.0f * supplies.Table!.RouteBand * length;
        float restArea = 9.0f * supplies.CellSize * length - bandArea;
        float nearDensity = nearRoute / bandArea;
        float farDensity = Mathf.Max(total - nearRoute, 1) / restArea;
        Check("supplies are much denser along the route", nearDensity >= farDensity * MinRouteDensityRatio,
            $"{nearRoute} of {total} within {supplies.Table.RouteBand:0} m: {nearDensity / farDensity:0.0}× denser than elsewhere");
        Check("supplies thin out with depth", deep < shallow, $"main path and scatter over {DepthSeeds.Length} mountains: first 15 rows {shallow}, last 15 rows {deep}");

        await Place(player, route.RoutePoint(route.StartZ - 5.0f));
        await WaitSeconds(1.0f);
        WorldItem? sample = null;
        float worstGap = 0.0f;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        foreach (WorldItem item in supplies.SpawnedItems)
        {
            sample ??= item;
            Vector3 p = item.GlobalPosition;
            Godot.Collections.Dictionary hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 5.0f, p + Vector3.Down * 5.0f, 1));
            if (hit.Count > 0)
            {
                worstGap = Mathf.Max(worstGap, Mathf.Abs(hit["position"].AsVector3().Y - p.Y));
            }
        }
        Check("spawned supplies sit on the ground", supplies.SpawnedCount > 0 && worstGap <= 0.05f,
            $"{supplies.SpawnedCount} items around the carrier, worst gap {worstGap * 100.0f:0.0} cm");

        if (sample == null)
        {
            return;
        }
        string key = sample.SlotKey;
        await Place(player, sample.GlobalPosition + Vector3.Back * 0.8f);
        bool picked = sample.Interact(player);
        supplies.ReloadCells();
        await WaitSeconds(0.5f);
        bool returned = false;
        foreach (WorldItem item in supplies.SpawnedItems)
        {
            returned = returned || item.SlotKey == key;
        }
        Check("a taken supply doesn't come back when its area reloads", picked && supplies.WasTaken(key) && !returned,
            $"picked {picked}, back after reload {returned}");
    }

    private async Task TestHighlight(PlayerController player)
    {
        ItemDefinition ration = Load("ration");
        WorldItem near = new() { Definition = ration };
        WorldItem far = new() { Definition = ration };
        AddChild(near);
        AddChild(far);
        near.GlobalPosition = player.GlobalPosition + new Vector3(6.0f, 0.0f, -6.0f);
        far.GlobalPosition = player.GlobalPosition + new Vector3(0.0f, 0.0f, -(near.IndicatorRadius + 20.0f));
        await WaitSeconds(0.2f);
        ArrayMesh grid = WorldItem.CellGrid(ration.SizeCells, 0.05f);
        int lines = grid.SurfaceGetArrayLen(0) / 2;
        // A 2 × 1 × 2 block: 12 outer edges plus the lines between cells on its faces.
        Check("items show their cell grid, and an arrow once the carrier is near", near.IndicatorVisible && !far.IndicatorVisible && lines >= 16,
            $"arrow at 8 m {near.IndicatorVisible}, at {near.IndicatorRadius + 20.0f:0} m {far.IndicatorVisible}, grid lines {lines}");
        near.QueueFree();
        far.QueueFree();
    }

    private async Task TestDeathRestart(PlayerController player, CarrierInventory inventory, CarrierNeeds needs, CarrierHealth health, WorldClock clock, SupplySpawner supplies)
    {
        inventory.TryAdd(Load("ration"));
        needs.SetNeeds(40.0f, 40.0f);
        clock.DebugTimeScale = FastClock;
        await WaitSeconds(0.5f);
        clock.DebugTimeScale = 1.0f;
        health.TakeDamage(health.Max * 2.0f, "test");
        await WaitSeconds(GetNode<RunState>("/root/RunState").SummaryDelay + 0.5f);

        bool emptied = inventory.AllItems.Count == 0;
        bool fed = Near(needs.Hunger, CarrierNeeds.Full) && Near(needs.Thirst, CarrierNeeds.Full);
        bool dawn = clock.Day == 1 && clock.TotalHours < 0.1;
        Check("death restarts the run: empty pack, full needs, dawn of day 1", emptied && fed && dawn && !health.Dead,
            $"items {inventory.AllItems.Count}, hunger {needs.Hunger:0}, thirst {needs.Thirst:0}, day {clock.Day} {clock.TimeOfDay:0.0}h");
    }

    private async Task<WorldItem> DropInFront(PlayerController player, ItemDefinition definition)
    {
        Vector3 forward = new Vector3(-player.GlobalBasis.Z.X, 0.0f, -player.GlobalBasis.Z.Z).Normalized();
        Vector3 spot = player.GlobalPosition + forward * 1.0f;
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(spot + Vector3.Up * 3.0f, spot + Vector3.Down * 3.0f, 1,
            new Godot.Collections.Array<Rid> { player.GetRid() }));
        if (hit.Count > 0)
        {
            spot = hit["position"].AsVector3();
        }
        WorldItem item = new() { Definition = definition };
        AddChild(item);
        item.GlobalPosition = spot;
        await WaitSeconds(0.1f);
        return item;
    }

    private async Task Walk(PlayerController player, float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        IRouteQuery route = _run!.Route!;
        for (float t = 0.0f; t < seconds; t += dt)
        {
            Vector3 p = player.GlobalPosition;
            Vector3 target = route.RoutePoint(p.Z - 6.0f);
            player.DirectionOverride = new Vector3(target.X - p.X, 0.0f, target.Z - p.Z);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        player.DirectionOverride = null;
    }

    private async Task Place(PlayerController player, Vector3 position)
    {
        player.Teleport(position + Vector3.Up * 0.1f, 0.0f);
        player.Health?.Refill();
        _needs?.Refill();
        await WaitSeconds(SettleTime);
    }

    /// <summary>Needs keep draining between frames, so compare with a small tolerance.</summary>
    private static bool Near(float value, float expected)
    {
        return Mathf.Abs(value - expected) <= 0.5f;
    }

    private static ItemDefinition Load(string id)
    {
        return ResourceLoader.Load<ItemDefinition>($"res://inventory/items/{id}.tres");
    }

    private async Task WaitSeconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
