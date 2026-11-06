using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Balance;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for M3 packing: grid rules, packing all starting items, centre of mass
/// responding to where things go, and A-vs-B by packing (the same items packed heavy-high-right
/// vs heavy-low-centred, then a 30° descent with no lean input).
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/PackingTests.tscn
/// </summary>
public partial class PackingTests : Node3D
{
    private static readonly StringName ActionForward = "player_move_forward";
    private static readonly StringName ActionBrake = "player_brake";

    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TestCourseBuilder? Course { get; set; }
    [Export] public CarrierInventory? Inventory { get; set; }

    [ExportGroup("Scenario")]
    [Export] public int DescentLane { get; set; } = 2;
    [Export] public float SettleTime { get; set; } = 1.0f;
    [Export] public float DescentStrideTime { get; set; } = 4.0f;
    [Export] public float DescentBrakeTime { get; set; } = 2.0f;

    [ExportGroup("Criteria")]
    /// <summary>Packing heavy items high must raise the load's centre of mass at least this much, in m.</summary>
    [Export] public float MinComRise { get; set; } = 0.08f;
    /// <summary>Packing heavy items to the right must shift the centre of mass right at least this much, in m.</summary>
    [Export] public float MinComShift { get; set; } = 0.02f;
    /// <summary>Moving the ore alone from bottom-centre to the top must raise the centre of mass this much, in m.</summary>
    [Export] public float MinOreRise { get; set; } = 0.04f;
    /// <summary>On the descent, the high-right packing's peak imbalance must exceed the low-centred one by this factor.</summary>
    [Export] public float MinDescentRatio { get; set; } = 1.5f;

    private readonly List<string> _failures = new();
    private readonly List<BalanceSnapshot> _samples = new();

    public override async void _Ready()
    {
        if (PlayerBody == null || Course == null || Inventory == null || Inventory.Grid == null)
        {
            GD.PushError("PackingTests: PlayerBody, Course and Inventory must be assigned.");
            GetTree().Quit(1);
            return;
        }

        GameEvents.BalanceUpdated += OnBalanceUpdated;
        await WaitSeconds(SettleTime);

        TestOrientations();
        TestGridRules();
        TestPackAll(Inventory);
        TestCenterOfMass(Inventory);
        await TestDescent(PlayerBody, Course, Inventory);

        GameEvents.BalanceUpdated -= OnBalanceUpdated;
        if (_failures.Count == 0)
        {
            GD.Print("PACKING TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"PACKING TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void OnBalanceUpdated(BalanceSnapshot snapshot)
    {
        _samples.Add(snapshot);
    }

    private void TestOrientations()
    {
        int upright = 0;
        for (int i = 0; i < GridOrientations.All.Count; i++)
        {
            if (GridOrientations.IsUpright(i))
            {
                upright++;
            }
        }
        Check("24 box orientations, 4 of them upright", GridOrientations.All.Count == 24 && upright == 4,
            $"{GridOrientations.All.Count} orientations, {upright} upright");

        Vector3I turned = GridOrientations.RotatedSize(new Vector3I(1, 2, 3), GridOrientations.YawQuarter);
        Vector3I tipped = GridOrientations.RotatedSize(new Vector3I(1, 2, 3), GridOrientations.TipSideQuarter);
        Check("rotation swaps the right axes", turned == new Vector3I(3, 2, 1) && tipped == new Vector3I(2, 1, 3),
            $"yaw {turned}, tip {tipped}");
    }

    private void TestGridRules()
    {
        BackpackGrid grid = new(new Vector3I(4, 4, 4));
        ItemDefinition cube = new() { SizeCells = new Vector3I(2, 2, 2) };
        ItemDefinition bottle = new() { SizeCells = new Vector3I(1, 3, 1), Orientation = OrientationRule.Upright };

        bool placed = grid.Place(cube, Vector3I.Zero, GridOrientations.Identity) != null;
        bool overlap = grid.Place(cube, Vector3I.One, GridOrientations.Identity) != null;
        bool outside = grid.Place(cube, new Vector3I(3, 0, 0), GridOrientations.Identity) != null;
        int stacked = grid.DropHeight(cube.SizeCells, 0, 0);
        bool tippedBottle = grid.Place(bottle, new Vector3I(3, 0, 3), GridOrientations.TipSideQuarter) != null;
        bool uprightBottle = grid.Place(bottle, new Vector3I(3, 0, 3), GridOrientations.Identity) != null;

        Check("grid rejects overlap and out-of-bounds", placed && !overlap && !outside, $"placed {placed}, overlap {overlap}, outside {outside}");
        Check("drop lands on top of the stack", stacked == 2, $"drop height {stacked}");
        Check("upright items can't be tipped", !tippedBottle && uprightBottle, $"tipped {tippedBottle}, upright {uprightBottle}");
        cube.Dispose();
        bottle.Dispose();
    }

    private void TestPackAll(CarrierInventory inventory)
    {
        BackpackGrid grid = inventory.Grid!;
        int volume = 0;
        foreach (ItemDefinition item in inventory.AllItems)
        {
            volume += item.Volume;
        }
        int capacity = grid.Size.X * grid.Size.Y * grid.Size.Z;

        List<ItemDefinition> left = inventory.Repack(ByMass(inventory, false), 0.5f);
        int used = capacity - grid.FreeCells;
        Check($"all {inventory.AllItems.Count} items pack without overlap (heaviest first)", left.Count == 0 && used == volume,
            $"{left.Count} left over, {used} of {capacity} cells used, items need {volume}");

        left = inventory.Repack(ByMass(inventory, true), 1.0f);
        Check("all items also pack lightest first", left.Count == 0, $"{left.Count} left over");
    }

    private void TestCenterOfMass(CarrierInventory inventory)
    {
        inventory.Repack(ByMass(inventory, false), 0.5f);
        LoadSnapshot low = GameEvents.CurrentLoad;

        inventory.Repack(ByMass(inventory, true), 1.0f);
        LoadSnapshot high = GameEvents.CurrentLoad;

        Check("heavy items high raise the centre of mass", high.CenterOfMass.Y >= low.CenterOfMass.Y + MinComRise,
            $"COM height {high.CenterOfMass.Y:0.000} m vs {low.CenterOfMass.Y:0.000} m");

        inventory.Repack(ByMass(inventory, false), 1.0f);
        float right = GameEvents.CurrentLoad.CenterOfMass.X;
        inventory.Repack(ByMass(inventory, false), 0.0f);
        float left = GameEvents.CurrentLoad.CenterOfMass.X;
        Check("packing toward one side shifts the centre of mass that way", right >= left + MinComShift,
            $"COM side packed right {right:+0.000;-0.000} m vs packed left {left:+0.000;-0.000} m");

        // Same order except the ore: packed first it ends up at the bottom, packed last on top.
        // (Moving it in place isn't possible here: the full-width sleeping bag closes the top.)
        List<ItemDefinition> oreFirst = ByMass(inventory, false);
        List<ItemDefinition> oreLast = new(oreFirst);
        ItemDefinition? ore = oreLast.Find(item => item.ItemId == "ore_sample");
        if (ore == null)
        {
            Check("ore on top raises the centre of mass", false, "no ore_sample among the items");
            return;
        }
        oreLast.Remove(ore);
        oreLast.Add(ore);

        inventory.Repack(oreFirst, 0.5f);
        float bottom = GameEvents.CurrentLoad.CenterOfMass.Y;
        int bottomRow = OreRow(inventory);
        inventory.Repack(oreLast, 0.5f);
        float top = GameEvents.CurrentLoad.CenterOfMass.Y;
        int topRow = OreRow(inventory);
        Check("ore on top raises the centre of mass", top >= bottom + MinOreRise,
            $"ore row {bottomRow} -> {topRow}, COM height {bottom:0.000} -> {top:0.000} m");
    }

    private static int OreRow(CarrierInventory inventory)
    {
        foreach (PackedItem item in inventory.Grid!.Items)
        {
            if (item.Definition.ItemId == "ore_sample")
            {
                return item.Origin.Y;
            }
        }
        return -1;
    }

    private async Task TestDescent(PlayerController player, TestCourseBuilder course, CarrierInventory inventory)
    {
        string lane = $"{course.LaneAngles[DescentLane]:0}°";
        inventory.Repack(ByMass(inventory, true), 1.0f);
        (float high, BalanceState highWorst) = await RunDescent(player, course);
        inventory.Repack(ByMass(inventory, false), 0.5f);
        (float low, BalanceState lowWorst) = await RunDescent(player, course);

        Check($"A-vs-B by packing on {lane}: heavy-high-right sways much more", high >= low * MinDescentRatio,
            $"peak imbalance {high:0.00} ({highWorst}) vs heavy-low-centred {low:0.00} ({lowWorst})");
        Check($"A-vs-B by packing on {lane}: heavy-low-centred keeps its balance", lowWorst <= BalanceState.Wobbling,
            $"worst state {lowWorst}");
    }

    /// <summary>Strides then brakes down the descent lane with no lean input; returns peak imbalance and worst state.</summary>
    private async Task<(float Peak, BalanceState Worst)> RunDescent(PlayerController player, TestCourseBuilder course)
    {
        player.Teleport(course.LanePoint(DescentLane, 3.0f) + Vector3.Up * 0.05f, 0.0f);
        player.Health?.Refill();
        player.GetNodeOrNull<BalanceController>("Balance")?.ResetBalance();
        await WaitSeconds(SettleTime);
        _samples.Clear();

        Input.ActionPress(ActionForward);
        await WaitSeconds(DescentStrideTime);
        Input.ActionPress(ActionBrake);
        await WaitSeconds(DescentBrakeTime);
        Input.ActionRelease(ActionForward);
        Input.ActionRelease(ActionBrake);

        float peak = 0.0f;
        BalanceState worst = BalanceState.Stable;
        foreach (BalanceSnapshot sample in _samples)
        {
            peak = Mathf.Max(peak, sample.ImbalanceMagnitude);
            if (sample.State > worst)
            {
                worst = sample.State;
            }
        }
        return (peak, worst);
    }

    private static List<ItemDefinition> ByMass(CarrierInventory inventory, bool lightestFirst)
    {
        List<ItemDefinition> items = new(inventory.AllItems);
        if (lightestFirst)
        {
            items.Sort((a, b) => a.MassKg.CompareTo(b.MassKg));
        }
        else
        {
            items.Sort((a, b) => b.MassKg.CompareTo(a.MassKg));
        }
        return items;
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
