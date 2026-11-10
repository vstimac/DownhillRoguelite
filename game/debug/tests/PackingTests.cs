using System.Collections.Generic;
using System.Linq;
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
    /// <summary>High-right packing's sustained RMS sway must exceed the low-centred one's by this factor.</summary>
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
        try { await TestDescent(PlayerBody, Course, Inventory); }
        catch (System.Exception error) { Check("descent fixture remains valid", false, error.Message); }

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

        // A shelf: a flat 4x1x4 board at height 2 leaves a gap underneath that a drop from above can't reach.
        BackpackGrid shelfGrid = new(new Vector3I(4, 4, 4));
        ItemDefinition board = new() { SizeCells = new Vector3I(4, 1, 2) };
        ItemDefinition small = new() { SizeCells = new Vector3I(1, 1, 1) };
        ItemDefinition post = new() { SizeCells = new Vector3I(1, 2, 1) };
        shelfGrid.Place(post, new Vector3I(0, 0, 0), GridOrientations.Identity);
        shelfGrid.Place(board, new Vector3I(0, 2, 0), GridOrientations.Identity);
        List<int> heights = shelfGrid.RestingHeights(small.SizeCells, 2, 1);
        Check("items can go into a gap under another (the wheel's lower layers), not just on top",
            heights.Count == 2 && heights[0] == 3 && heights[1] == 0 && shelfGrid.DropHeight(small.SizeCells, 2, 1) == 3,
            $"resting heights {string.Join(",", heights)}, drop {shelfGrid.DropHeight(small.SizeCells, 2, 1)}");
        board.Dispose();
        small.Dispose();
        post.Dispose();
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
        HashSet<string> copies = new(inventory.Grid!.Items.Select(item => item.InstanceId));
        ArrangeHighRight(inventory);
        LoadSnapshot highLoad = GameEvents.CurrentLoad;
        (float high, float highRms, BalanceState highWorst) = await RunDescent(player, course);
        inventory.Repack(ByMass(inventory, false), 0.5f);
        ArrangeCentred(inventory);
        LoadSnapshot lowLoad = GameEvents.CurrentLoad;
        (float low, float lowRms, BalanceState lowWorst) = await RunDescent(player, course);
        Check("descent fixtures really compare a high side load to a low centred load",
            highLoad.CenterOfMass.Y >= lowLoad.CenterOfMass.Y + MinComRise
            && highLoad.CenterOfMass.X >= Mathf.Abs(lowLoad.CenterOfMass.X) + MinComShift
            && Mathf.Abs(highLoad.Mass - lowLoad.Mass) < 0.001f
            && copies.SetEquals(inventory.Grid.Items.Select(item => item.InstanceId)),
            $"high COM {highLoad.CenterOfMass}, low COM {lowLoad.CenterOfMass}, equal mass {lowLoad.Mass:0.00} kg");

        // A single peak aliases the current lead foot during a gait transition. RMS compares the
        // whole descent, while the separate peak/state check still rejects a safer high pack.
        Check($"A-vs-B by packing on {lane}: heavy-high-right sways much more", highRms >= lowRms * MinDescentRatio,
            $"RMS sway {highRms:0.000} vs {lowRms:0.000}, ratio {highRms / lowRms:0.00}");
        Check($"A-vs-B by packing on {lane}: high packing also has the larger peak", high > low && highWorst >= lowWorst,
            $"peak imbalance {high:0.00} ({highWorst}) vs {low:0.00} ({lowWorst})");
        Check($"A-vs-B by packing on {lane}: heavy-low-centred keeps its balance", lowWorst <= BalanceState.Wobbling,
            $"worst state {lowWorst}");
    }

    /// <summary>A concrete, valid grid fixture: the same ten owned items, with heavy ore actually high/right.</summary>
    private static void ArrangeHighRight(CarrierInventory inventory)
    {
        BackpackGrid plan = new(inventory.Grid!.Size);
        (string Id, Vector3I At, Vector3I Size)[] layout =
        {
            ("sleeping_bag", new(0, 0, 0), new(3, 6, 4)),
            ("tent", new(2, 6, 0), new(2, 6, 4)),
            ("rope", new(3, 2, 0), new(3, 4, 2)),
            ("cookware", new(3, 0, 2), new(3, 4, 2)),
            ("food_bag", new(4, 9, 2), new(2, 3, 2)),
            ("stove", new(4, 4, 2), new(2, 3, 2)),
            ("water_bottle", new(4, 6, 0), new(2, 4, 2)),
            ("med_kit", new(4, 7, 2), new(2, 2, 2)),
            ("fuel_canister", new(4, 0, 0), new(2, 2, 2)),
            ("ore_sample", new(4, 10, 0), new(2, 2, 2)),
        };
        foreach ((string id, Vector3I at, Vector3I size) in layout)
        {
            ItemDefinition definition = inventory.AllItems.Single(item => item.ItemId == id);
            int orientation = -1;
            for (int i = 0; i < GridOrientations.All.Count; i++)
            {
                if (GridOrientations.Allows(definition.Orientation, i) && GridOrientations.RotatedSize(definition.SizeCells, i) == size)
                { orientation = i; break; }
            }
            if (orientation < 0 || plan.Place(definition, at, orientation) == null)
            { throw new System.InvalidOperationException("The high/right fixture no longer fits its item definitions."); }
        }
        foreach (PackedItem item in new List<PackedItem>(inventory.Grid.Items)) { inventory.Unpack(item); }
        foreach (PackedItem item in plan.Items)
        {
            if (inventory.Pack(item.Definition, item.Origin, item.Orientation) == null)
            { throw new System.InvalidOperationException("The descent fixture could not restore an owned item."); }
        }
    }

    /// <summary>Auto-pack ranks boxes, not total COM. Finish the fixture with legal horizontal moves of the same copies.</summary>
    private static void ArrangeCentred(CarrierInventory inventory)
    {
        BackpackGrid grid = inventory.Grid!;
        for (int pass = 0; pass < 2; pass++)
        {
            List<PackedItem> items = new(grid.Items);
            items.Sort((a, b) => b.Definition.MassKg.CompareTo(a.Definition.MassKg));
            foreach (PackedItem item in items)
            {
                LoadSnapshot load = GameEvents.CurrentLoad;
                Vector3I best = item.Origin;
                float score = -Mathf.Abs(load.CenterOfMass.X);
                inventory.Unpack(item);
                for (int x = 0; x + item.Size.X <= grid.Size.X; x++)
                {
                    for (int z = 0; z <= item.Origin.Z; z++)
                    {
                        Vector3I candidate = new(x, item.Origin.Y, z);
                        if (!grid.CanPlace(item.Size, candidate)) { continue; }
                        float com = load.CenterOfMass.X + (x - item.Origin.X) * inventory.Backpack!.CellSize * item.Definition.MassKg / load.Mass;
                        com = -Mathf.Abs(com);
                        if (com > score + 0.00001f) { score = com; best = candidate; }
                    }
                }
                if (inventory.Pack(item.Definition, best, item.Orientation) == null)
                { throw new System.InvalidOperationException("Packing fixture lost a valid original placement."); }
            }
        }
    }

    /// <summary>Strides then brakes with no lean; compares sustained sway as well as worst-case safety.</summary>
    private async Task<(float Peak, float Rms, BalanceState Worst)> RunDescent(PlayerController player, TestCourseBuilder course)
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
        float squared = 0;
        BalanceState worst = BalanceState.Stable;
        foreach (BalanceSnapshot sample in _samples)
        {
            peak = Mathf.Max(peak, sample.ImbalanceMagnitude);
            squared += sample.ImbalanceMagnitude * sample.ImbalanceMagnitude;
            if (sample.State > worst)
            {
                worst = sample.State;
            }
        }
        return (peak, Mathf.Sqrt(squared / Mathf.Max(1, _samples.Count)), worst);
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
