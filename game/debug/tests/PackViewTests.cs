using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the 2.5D pack: the layout the pages draw matches the grid, the pack can't be
/// rearranged while it is on the back, a short press of the pack key only looks, holding it takes
/// time to set the pack down and to shoulder it again, pointing at the slanted view finds the right
/// item from all four sides, and moving things through the page moves the centre of mass.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/PackViewTests.tscn
/// </summary>
public partial class PackViewTests : Node3D
{
    private static readonly StringName ActionToggle = "inventory_toggle_packing";
    private const string TestOptionsPath = "user://options_packview_test.cfg";

    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public CarrierInventory? Inventory { get; set; }
    [Export] public PackingMode? Packing { get; set; }
    [Export] public Hud? Ui { get; set; }

    [ExportGroup("Scenario")]
    [Export] public float SettleTime { get; set; } = 1.0f;
    /// <summary>Slack allowed on the waits, in s.</summary>
    [Export] public float TimingSlack { get; set; } = 0.2f;
    /// <summary>Taking a thing out and putting it elsewhere must move the centre of mass at least this far, in m.</summary>
    [Export] public float MinComMove { get; set; } = 0.002f;

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        // The test runs on through the page's freeze of the world.
        ProcessMode = ProcessModeEnum.Always;
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(TestOptionsPath));
        GameOptions.UseFile(TestOptionsPath);
        if (PlayerBody == null || Inventory?.Grid == null || Packing == null || Ui?.Packing?.View == null || Ui.PackLook == null || Ui.PackRing == null)
        {
            GD.PushError("PackViewTests: PlayerBody, Inventory, Packing and Ui (with its pack pages) must be assigned.");
            GetTree().Quit(1);
            return;
        }
        await Wait(SettleTime);

        TestLayout(Inventory);
        TestLockedWhileCarried(Inventory);
        await TestQuickLook(Packing, Ui);
        await TestLetGoEarly(Packing);
        await TestSetDown(Packing, Ui);
        TestPointing(Ui.Packing.View);
        TestMoving(Inventory, Ui.Packing);
        await TestPutOn(Packing, Ui);

        if (_failures.Count == 0)
        {
            GD.Print("PACK VIEW TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"PACK VIEW TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void TestLayout(CarrierInventory inventory)
    {
        PackLayout layout = GameEvents.CurrentPackLayout;
        int packed = 0;
        int matching = 0;
        foreach (PackLayoutEntry entry in layout.Entries)
        {
            if (!entry.Packed)
            {
                continue;
            }
            packed++;
            foreach (PackedItem item in inventory.Grid!.Items)
            {
                if (item.InstanceId == entry.InstanceId && item.Origin == entry.Origin && item.Size == entry.Size
                    && item.Orientation == entry.Orientation && item.Definition.ItemId == entry.ItemId)
                {
                    matching++;
                }
            }
        }
        bool same = packed > 0 && packed == inventory.Grid!.Items.Count && matching == packed && layout.GridSize == inventory.Grid.Size;
        bool mass = Mathf.Abs(layout.Mass - GameEvents.CurrentLoad.Mass) < 0.01f;
        Check("the published layout is the grid: every packed copy, its cells and turn, and the load's mass", same && mass,
            $"{packed} packed in the layout, {inventory.Grid!.Items.Count} in the grid, {matching} matching; mass {layout.Mass:0.00} vs {GameEvents.CurrentLoad.Mass:0.00}");
    }

    private void TestLockedWhileCarried(CarrierInventory inventory)
    {
        PackLayoutEntry top = Topmost(GameEvents.CurrentPackLayout);
        GameEvents.RequestPackTakeOut(top.InstanceId);
        GameEvents.RequestPackArrange();
        bool still = GameEvents.CurrentPackLayout.TryFind(top.InstanceId, out PackLayoutEntry now) && now.Packed && now.Origin == top.Origin;
        Check("nothing can be rearranged while the pack is on the back", still && inventory.LooseItems.Count == 0,
            $"still packed in place {still}, {inventory.LooseItems.Count} loose");
    }

    private async Task TestQuickLook(PackingMode packing, Hud ui)
    {
        Key(true);
        await Wait(packing.HoldThreshold * 0.4f);
        Key(false);
        await Wait(0.6f);
        bool looked = ui.PackLook!.IsOpen && ui.PackLook.Visible;
        bool carried = packing.Phase == PackingPhase.Carried && !GameEvents.IsPacking && !GameEvents.IsPackOpen && !GetTree().Paused;
        Key(true);
        await Wait(packing.HoldThreshold * 0.4f);
        Key(false);
        await Wait(0.6f);
        bool gone = !ui.PackLook.IsOpen;
        Check("a short press of the pack key opens the quick look and nothing else; another closes it", looked && carried && gone,
            $"looked {looked}, pack still carried and world running {carried}, closed again {gone}");
    }

    private async Task TestLetGoEarly(PackingMode packing)
    {
        Key(true);
        await Wait(packing.HoldThreshold + packing.SetDownTime * 0.5f);
        bool started = packing.Phase == PackingPhase.SettingDown && GameEvents.IsPacking && !GameEvents.IsPackOpen;
        Key(false);
        await Wait(0.2f);
        bool back = packing.Phase == PackingPhase.Carried && !GameEvents.IsPacking && !GameEvents.IsPackOpen;
        Check("letting go before the pack is down leaves it on the back", started && back, $"was setting down {started}, carried again {back}");
    }

    private async Task TestSetDown(PackingMode packing, Hud ui)
    {
        Key(true);
        await Wait(packing.HoldThreshold + packing.SetDownTime - TimingSlack);
        bool waiting = packing.Phase == PackingPhase.SettingDown && !GameEvents.IsPackOpen;
        float ring = ui.PackRing!.Progress;
        await Wait(TimingSlack * 2.0f);
        Key(false);
        await Wait(0.6f);
        bool open = packing.Phase == PackingPhase.Open && GameEvents.IsPackOpen && GameEvents.IsPacking;
        bool page = ui.Packing!.IsOpen && ui.Packing.Visible && Input.MouseMode == Input.MouseModeEnum.Visible;
        bool frozen = GetTree().Paused && GameEvents.PagesFrozen;
        Check("holding the pack key sets the pack down, and that takes its time", waiting && ring > 0.5f && ring < 1.0f && open,
            $"still setting down just before the time is up {waiting} (ring {ring:0.00}), open after it {open}");
        Check("the packing page opens with the pack, takes the mouse and stops the world", page && frozen, $"page {page}, world stopped {frozen}");
    }

    private void TestPointing(PackGridView view)
    {
        PackLayout layout = GameEvents.CurrentPackLayout;
        PackLayoutEntry top = Topmost(layout);
        Aabb box = PackGeometry.CellBox(layout, top.Origin, top.Size, 0.0f);
        Vector3 topCentre = box.GetCenter() + Vector3.Up * box.Size.Y * 0.5f;
        int found = 0;
        int columns = 0;
        Vector2I wanted = new(1, 2);
        Vector3 columnPoint = PackGeometry.GridToPack(layout, new Vector3((wanted.X + 0.5f) * layout.CellSize, layout.SizeMeters.Y, (wanted.Y + 0.5f) * layout.CellSize));
        for (int turn = 0; turn < 4; turn++)
        {
            view.SetTurnNow(turn);
            if (view.EntryAt(view.Project(topCentre), false, out PackLayoutEntry hit) && hit.InstanceId == top.InstanceId)
            {
                found++;
            }
            if (view.ColumnAt(view.Project(columnPoint), Vector3I.One, out Vector2I column) && column == wanted)
            {
                columns++;
            }
        }
        view.SetTurnNow(0);
        bool outside = view.ColumnAt(new Vector2(2.0f, 2.0f), Vector3I.One, out Vector2I _);
        Check("pointing at the slanted pack finds the right thing and the right column from all four sides", found == 4 && columns == 4 && !outside,
            $"topmost thing found from {found} sides, column from {columns}; a corner of the view counts as off the pack {!outside}");
    }

    private void TestMoving(CarrierInventory inventory, PackingScreen page)
    {
        PackLayout before = GameEvents.CurrentPackLayout;
        PackLayoutEntry top = Topmost(before);
        int count = inventory.AllItems.Count;

        // Onto another thing: refused, it stays where it was.
        PackLayoutEntry other = default;
        foreach (PackLayoutEntry entry in before.Entries)
        {
            if (entry.Packed && entry.InstanceId != top.InstanceId)
            {
                other = entry;
                break;
            }
        }
        page.Hold(top.InstanceId, top.Orientation);
        bool refused = !page.PlaceHeld(other.Origin) && page.HeldId == top.InstanceId
            && GameEvents.CurrentPackLayout.TryFind(top.InstanceId, out PackLayoutEntry kept) && kept.Packed && kept.Origin == top.Origin;

        // Out onto the mat: the load gets lighter by exactly that thing.
        page.PutHeldOnMat();
        PackLayout without = GameEvents.CurrentPackLayout;
        bool takenOut = without.TryFind(top.InstanceId, out PackLayoutEntry loose) && !loose.Packed
            && Mathf.Abs(before.Mass - without.Mass - top.MassKg) < 0.01f && inventory.LooseItems.Count == 1;

        // Back where it was, then a small thing goes somewhere else: the centre of mass moves.
        page.Hold(top.InstanceId, top.Orientation);
        bool restored = page.PlaceHeld(top.Origin);
        PackLayout restoredLayout = GameEvents.CurrentPackLayout;
        PackLayoutEntry small = Smallest(restoredLayout);
        page.Hold(small.InstanceId, small.Orientation);
        BackpackGrid free = PackGeometry.Occupancy(restoredLayout, small.InstanceId);
        bool placed = false;
        Vector3I spot = Vector3I.Zero;
        for (int x = restoredLayout.GridSize.X - small.Size.X; x >= 0 && !placed; x--)
        {
            for (int z = restoredLayout.GridSize.Z - small.Size.Z; z >= 0 && !placed; z--)
            {
                foreach (int rest in free.RestingHeights(small.Size, x, z))
                {
                    Vector3I at = new(x, rest, z);
                    if (!placed && at != small.Origin)
                    {
                        spot = at;
                        placed = page.PlaceHeld(at);
                    }
                }
            }
        }
        PackLayout after = GameEvents.CurrentPackLayout;
        bool moved = restored && placed && page.HeldId.Length == 0 && after.TryFind(small.InstanceId, out PackLayoutEntry now) && now.Packed && now.Origin == spot;
        float shift = (after.CenterOfMass - before.CenterOfMass).Length();
        bool whole = inventory.AllItems.Count == count && inventory.LooseItems.Count == 0 && Mathf.Abs(after.Mass - before.Mass) < 0.01f;
        Check("a thing set down on another is refused and stays in the hand", refused, $"refused {refused}");
        Check("taking a thing out lightens the load by its weight", takenOut, $"out and lighter {takenOut}: {before.Mass:0.00} kg to {without.Mass:0.00} kg for a {top.MassKg:0.00} kg thing");
        Check("setting it down elsewhere through the page moves the centre of mass, and nothing is lost or copied", moved && whole && shift >= MinComMove,
            $"first thing put back {restored}; {small.ItemId} {small.Size} from {small.Origin}, {free.FreeCells} cells free, moved to {spot} {moved}, centre of mass moved {shift * 100.0f:0.0} cm, still {inventory.AllItems.Count} things {whole}");
    }

    private async Task TestPutOn(PackingMode packing, Hud ui)
    {
        GameEvents.RequestPackClose();
        await Wait(Mathf.Max(0.05f, packing.PutOnTime - TimingSlack));
        bool shouldering = packing.Phase == PackingPhase.PuttingOn && GameEvents.IsPacking && !GameEvents.IsPackOpen && !GetTree().Paused;
        PackLayoutEntry top = Topmost(GameEvents.CurrentPackLayout);
        GameEvents.RequestPackTakeOut(top.InstanceId);
        bool locked = GameEvents.CurrentPackLayout.TryFind(top.InstanceId, out PackLayoutEntry now) && now.Packed;
        await Wait(TimingSlack * 2.0f + 0.4f);
        bool carried = packing.Phase == PackingPhase.Carried && !GameEvents.IsPacking && !ui.Packing!.IsOpen;
        Check("shouldering the pack takes its time too, in the running world, and the pack is closed meanwhile", shouldering && locked,
            $"still shouldering just before the time is up {shouldering}, closed to changes {locked}");
        Check("then the pack is on the back again and the page is gone", carried, $"carried {carried}, phase {packing.Phase}");
    }

    /// <summary>The packed copy that reaches highest: nothing lies on it, so it is in plain sight from above.</summary>
    private static PackLayoutEntry Topmost(PackLayout layout)
    {
        PackLayoutEntry best = default;
        int height = -1;
        foreach (PackLayoutEntry entry in layout.Entries)
        {
            if (entry.Packed && entry.Origin.Y + entry.Size.Y > height)
            {
                height = entry.Origin.Y + entry.Size.Y;
                best = entry;
            }
        }
        return best;
    }

    /// <summary>The packed copy that takes the fewest cells: there is room for it somewhere else.</summary>
    private static PackLayoutEntry Smallest(PackLayout layout)
    {
        PackLayoutEntry best = default;
        int cells = int.MaxValue;
        foreach (PackLayoutEntry entry in layout.Entries)
        {
            int volume = entry.Size.X * entry.Size.Y * entry.Size.Z;
            if (entry.Packed && volume < cells)
            {
                cells = volume;
                best = entry;
            }
        }
        return best;
    }

    /// <summary>Presses or lets go of the pack key the way the keyboard does: as an event the game sees.</summary>
    private static void Key(bool down)
    {
        Input.ParseInputEvent(new InputEventAction { Action = ActionToggle, Pressed = down });
    }

    private async Task Wait(float seconds)
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
