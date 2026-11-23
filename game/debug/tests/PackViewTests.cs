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
    private static readonly StringName ActionOpen = "inventory_open";
    private static readonly StringName MoveForward = "player_move_forward";
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

    /// <summary>The set-down pack's lowest point may be this far from the ground, in m.</summary>
    [Export] public float MaxGroundGap { get; set; } = 0.03f;
    /// <summary>The set-down pack stands at least this far in front of the carrier, in m.</summary>
    [Export] public float MinAhead { get; set; } = 0.3f;
    [Export] public float MaxAhead { get; set; } = 1.2f;
    /// <summary>Walking off the pack must move the carrier 5 cm within this many physics frames (nothing locks him longer than it looks).</summary>
    [Export] public int MaxWalkFrames { get; set; } = 15;

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

        TestFill();
        TestLayout(Inventory);
        await TestOnePage(Packing, Ui);
        TestLockedWhileCarried(Inventory);
        await TestQuickLook(Packing, Ui);
        await TestLetGoEarly(Packing);
        await TestRingThenLetGo(Packing, Ui);
        await TestWalkCallsOff(Packing);
        TestGamepadOnOldBase();
        await TestGamepad(Packing, Ui);
        await TestSetDown(Packing, Ui);
        TestPointing(Ui.Packing.View);
        TestMoving(Inventory, Ui.Packing);
        await TestPressWhileShouldering(Packing, Ui);
        await TestPutOn(Packing, Ui);
        await TestWalkOffOpen(Packing, Ui);

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

    /// <summary>
    /// A small update on the 3.0 base keeps that base's input map, which has no gamepad Y on the pack
    /// key: PatchLoader adds it at startup. Taken off here as on that base, it comes back once (the
    /// gamepad test after this one then presses it), and a second start adds nothing.
    /// </summary>
    private void TestGamepadOnOldBase()
    {
        const string action = "inventory_toggle_packing";
        foreach (InputEvent existing in InputMap.ActionGetEvents(action))
        {
            if (existing is InputEventJoypadButton)
            {
                InputMap.ActionEraseEvent(action, existing);
            }
        }
        int first = PatchLoader.AddGamepadButtonsTheBaseLacks();
        int again = PatchLoader.AddGamepadButtonsTheBaseLacks();
        bool y = false;
        foreach (InputEvent existing in InputMap.ActionGetEvents(action))
        {
            y |= existing is InputEventJoypadButton joy && joy.ButtonIndex == JoyButton.Y;
        }
        Check("on a folder updated from the 3.0 base the pack key gets its gamepad Y at start, once", first == 1 && again == 0 && y,
            $"added {first}, then {again}, Y bound {y}");
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
        bool running = !GetTree().Paused && !GameEvents.PagesFrozen;
        Check("holding the pack key sets the pack down, and that takes its time", waiting && ring > 0.5f && ring < 1.0f && open,
            $"still setting down just before the time is up {waiting} (ring {ring:0.00}), open after it {open}");
        Check("the pack page opens with the pack, takes the mouse, and the world goes on", page && running, $"page {page}, world running {running}");
        TestOnTheGround(true);
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
        if (packing.Phase != PackingPhase.Open)
        {
            packing.TryOpen();
            await Wait(0.3f);
        }
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
        TestOnTheGround(false);
    }

    /// <summary>Models are laid and stretched to fill the cells they take, within limits.</summary>
    private void TestFill()
    {
        Vector3 longModel = new(0.1f, 0.4f, 0.1f);
        Vector3 lyingBlock = new(0.4f, 0.1f, 0.1f);
        float laid = PackGeometry.FillBlock(longModel, lyingBlock, false, 1.6f, 1.25f, out int turn, out Vector3 _);
        float standing = PackGeometry.FillBlock(longModel, lyingBlock, true, 1.6f, 1.25f, out int uprightTurn, out Vector3 _);
        float same = PackGeometry.FillBlock(lyingBlock, lyingBlock, false, 1.6f, 1.25f, out int sameTurn, out Vector3 sameScale);
        bool ok = laid > 0.95f && turn != GridOrientations.Identity && GridOrientations.IsUpright(uprightTurn) && standing < 0.2f
            && sameTurn == GridOrientations.Identity && same > 0.99f && sameScale.IsEqualApprox(Vector3.One);
        Check("a long model is laid along a long block and fills it; an upright-only one stays up; a fitting one is left alone", ok,
            $"laid fills {laid:0.00} (turn {turn}), kept upright fills {standing:0.00}, same shape fills {same:0.00} unturned {sameTurn == GridOrientations.Identity}");
    }

    /// <summary>
    /// I is the same pack without holding: it sets the pack down by itself (no key held) and opens the
    /// one page; choosing a kind marks its copies in the pack, nothing chosen shows the carrier; I again
    /// shoulders it.
    /// </summary>
    private async Task TestOnePage(PackingMode packing, Hud ui)
    {
        PackingScreen page = ui.Packing!;
        Press(ActionOpen, true);
        await Wait(0.05f);
        Press(ActionOpen, false);
        await Wait(packing.SetDownTime * 0.5f);
        bool byItself = packing.Phase == PackingPhase.SettingDown && ui.PackRing!.Key == "I" && ui.PackRing.HintText.Contains("I again");
        await Wait(packing.SetDownTime * 0.5f + 0.4f);
        bool open = packing.Phase == PackingPhase.Open && page.IsOpen && page.Visible;
        PackLayout layout = GameEvents.CurrentPackLayout;
        PackLayoutEntry top = Topmost(layout);
        page.SelectItem(top.ItemId);
        await Wait(0.05f);
        bool marked = page.View!.IsMarked(top.InstanceId) && page.DetailTitle.Length > 0 && page.DetailTitle != "The carrier";
        string described = page.DetailTitle;
        page.SelectItem("");
        await Wait(0.05f);
        bool cleared = !page.View.IsMarked(top.InstanceId) && page.DetailTitle == "The carrier";
        Rect2 onScreen = page.GetGlobalRect();
        Vector2 screen = page.GetViewportRect().Size;
        bool docked = onScreen.End.X <= screen.X + 1.0f && onScreen.Position.X > screen.X * 0.25f && onScreen.Position.Y >= 0.0f && onScreen.End.Y <= screen.Y + 1.0f;
        Press(ActionOpen, true);
        await Wait(0.05f);
        Press(ActionOpen, false);
        await Wait(packing.PutOnTime + 0.3f);
        bool back = packing.Phase == PackingPhase.Carried && !GameEvents.IsPacking && !page.IsOpen;
        Check("I sets the pack down by itself (no holding) and opens the one pack page", byItself && open,
            $"going down by itself with I in the ring {byItself}, open {open}");
        Check("the page is docked at the right; a chosen kind is marked in the pack and described, nothing chosen shows the carrier", docked && marked && cleared,
            $"docked {docked} ({onScreen.Position.X:0}..{onScreen.End.X:0} of {screen.X:0}), marked {marked} (\"{described}\"), cleared {cleared}");
        Check("I again shoulders it", back, $"carried {back}, phase {packing.Phase}");
    }

    /// <summary>The ring is filled almost to the end, then the key is let go: the pack stays on the back and nothing in it moves.</summary>
    private async Task TestRingThenLetGo(PackingMode packing, Hud ui)
    {
        string before = Signature(GameEvents.CurrentPackLayout);
        Key(true);
        await Wait(packing.HoldThreshold + packing.SetDownTime * 0.85f);
        float ring = ui.PackRing!.Progress;
        bool filling = packing.Phase == PackingPhase.SettingDown && ring > 0.6f && ring < 1.0f && ui.PackRing.Visible;
        Key(false);
        int frames = await FramesUntil(() => packing.Phase == PackingPhase.Carried && !GameEvents.IsPacking, 30);
        await Wait(0.1f);
        bool untouched = Signature(GameEvents.CurrentPackLayout) == before && !GameEvents.IsPackOpen && !ui.PackRing.Visible;
        Check("the ring filled almost to the end and let go: the pack stays on, at once, and nothing in it moved", filling && frames <= 2 && untouched,
            $"ring {ring:0.00} {filling}, back on in {frames} frames, layout untouched and ring gone {untouched}");
    }

    /// <summary>Walking (a move key) while the pack is going down calls it off at once, and the carrier walks in the same moment.</summary>
    private async Task TestWalkCallsOff(PackingMode packing)
    {
        int before = packing.Interrupts;
        Key(true);
        await Wait(packing.HoldThreshold + packing.SetDownTime * 0.4f);
        bool going = packing.Phase == PackingPhase.SettingDown;
        Vector3 start = PlayerBody!.GlobalPosition;
        Press(MoveForward, true);
        int frames = await FramesUntil(() => packing.Phase == PackingPhase.Carried && !GameEvents.IsPacking, 30);
        int walking = await FramesUntil(() => Flat(PlayerBody.GlobalPosition - start) > 0.05f, 60);
        Press(MoveForward, false);
        Key(false);
        await Wait(0.5f);
        bool counted = packing.Interrupts == before + 1;
        Check("walking while the pack goes down calls it off at once, and the carrier is walking at once", going && frames <= 1 && walking <= MaxWalkFrames && counted,
            $"was going down {going}, called off in {frames} frames, moved 5 cm in {walking} frames (max {MaxWalkFrames}), counted {counted}");
    }

    /// <summary>The controller's Y does what Tab does: a hold sets the pack down; the ring says Y.</summary>
    private async Task TestGamepad(PackingMode packing, Hud ui)
    {
        Pad(true);
        await Wait(packing.HoldThreshold + packing.SetDownTime * 0.5f);
        bool going = packing.Phase == PackingPhase.SettingDown && ui.PackRing!.Key == "Y";
        Pad(false);
        await Wait(0.2f);
        bool back = packing.Phase == PackingPhase.Carried;
        Pad(true);
        await Wait(0.1f);
        Pad(false);
        await Wait(0.4f);
        bool looked = ui.PackLook!.IsOpen;
        Pad(true);
        await Wait(0.1f);
        Pad(false);
        await Wait(0.4f);
        Check("Y on a controller is the pack key: hold sets the pack down, let go keeps it on, a tap looks", going && back && looked && !ui.PackLook.IsOpen,
            $"going down with Y in the ring {going}, let go {back}, tap looked {looked}");
    }

    /// <summary>
    /// A press while the pack is on its way is not lost: holding the key while it is being shouldered
    /// turns it back to the ground from where it is (the ring goes on from there), and a tap while
    /// shouldering is kept for a look once it is on.
    /// </summary>
    private async Task TestPressWhileShouldering(PackingMode packing, Hud ui)
    {
        GameEvents.RequestPackClose();
        await Wait(packing.PutOnTime * 0.5f);
        bool shouldering = packing.Phase == PackingPhase.PuttingOn;
        Key(true);
        await Wait(packing.HoldThreshold + 0.05f);
        float ring = ui.PackRing!.Progress;
        bool turned = packing.Phase == PackingPhase.SettingDown && ring > 0.2f && ring < 0.8f;
        await Wait(packing.SetDownTime * 0.6f + 0.2f);
        Key(false);
        await Wait(0.2f);
        bool open = packing.Phase == PackingPhase.Open && GameEvents.IsPackOpen;

        GameEvents.RequestPackClose();
        await Wait(0.2f);
        Key(true);
        await Wait(0.05f);
        Key(false);
        await Wait(0.1f);
        bool notTurned = packing.Phase == PackingPhase.PuttingOn;
        await Wait(packing.PutOnTime + 0.3f);
        bool looked = packing.Phase == PackingPhase.Carried && ui.PackLook!.IsOpen;
        GameEvents.RequestPackQuickView();
        await Wait(0.3f);
        Check("held while shouldering, the key turns the pack back from where it is", shouldering && turned && open,
            $"shouldering {shouldering}, turned back with the ring at {ring:0.00} {turned}, open again {open}");
        Check("a tap while shouldering is kept: the look comes once the pack is on", notTurned && looked,
            $"went on shouldering {notTurned}, looked after {looked}");
    }

    /// <summary>
    /// Walking off an open pack: nothing waits for the animation. The page goes, the carrier walks at
    /// once, the pack swings onto the back while walking and is worn again a moment later.
    /// </summary>
    private async Task TestWalkOffOpen(PackingMode packing, Hud ui)
    {
        packing.TryOpen();
        await Wait(0.5f);
        bool open = packing.Phase == PackingPhase.Open && ui.Packing!.IsOpen;
        Vector3 start = PlayerBody!.GlobalPosition;
        Press(MoveForward, true);
        int frames = await FramesUntil(() => packing.Phase == PackingPhase.Carried && !GameEvents.IsPacking && !GameEvents.IsPackOpen, 30);
        int walking = await FramesUntil(() => Flat(PlayerBody.GlobalPosition - start) > 0.05f, 60);
        PackSetDown? setDown = PlayerBody.FindChild("PackSetDown", true, false) as PackSetDown;
        bool swinging = setDown != null && setDown.Off;
        await Wait(0.6f);
        Press(MoveForward, false);
        bool worn = setDown != null && !setDown.Off;
        bool gone = !ui.Packing!.IsOpen;
        await Wait(0.5f);
        Check("walking off the open pack shoulders it at once and the carrier walks at once", open && frames <= 1 && walking <= MaxWalkFrames && gone,
            $"open {open}, carried in {frames} frames, moved 5 cm in {walking} frames (max {MaxWalkFrames}), page gone {gone}");
        Check("the pack swings onto the back while the carrier walks, then is worn", swinging && worn, $"swinging {swinging}, worn {worn}");
    }

    /// <summary>The flat distance of a move, in m.</summary>
    private static float Flat(Vector3 move) => new Vector2(move.X, move.Z).Length();

    /// <summary>Physics frames until the condition holds (max + 1 if it never does in max frames).</summary>
    private async Task<int> FramesUntil(System.Func<bool> done, int max)
    {
        for (int frame = 0; frame <= max; frame++)
        {
            if (done())
            {
                return frame;
            }
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        return max + 1;
    }

    private static string Signature(PackLayout layout)
    {
        List<string> parts = new();
        foreach (PackLayoutEntry entry in layout.Entries)
        {
            parts.Add($"{entry.InstanceId}:{entry.Packed}:{entry.Origin}:{entry.Orientation}");
        }
        return string.Join(";", parts);
    }

    /// <summary>The real pack stands on the ground in front of the carrier while it is off, and is worn again after.</summary>
    private void TestOnTheGround(bool down)
    {
        PackSetDown? setDown = PlayerBody!.FindChild("PackSetDown", true, false) as PackSetDown;
        BackpackAttachment? mount = null;
        if (PlayerBody.FindChild("ClothingVisuals", true, false) is ClothingVisualController clothing)
        {
            mount = clothing.BackpackMount;
        }
        if (setDown == null || mount == null)
        {
            Check("the carrier has a pack to set down", false, $"set-down {setDown != null}, worn pack {mount != null}");
            return;
        }
        if (!down)
        {
            Check("shouldered, the pack is worn again and nothing is left on the ground", !setDown.Off && mount.Visible, $"off {setDown.Off}, worn shown {mount.Visible}");
            return;
        }
        float lowest = float.MaxValue;
        Vector3 centre = Vector3.Zero;
        if (setDown.Copy != null)
        {
            Aabb bounds = ModelFit.Bounds(setDown.Copy);
            for (int corner = 0; corner < 8; corner++)
            {
                lowest = Mathf.Min(lowest, (setDown.Copy.GlobalTransform * bounds.GetEndpoint(corner)).Y);
            }
            centre = setDown.Copy.GlobalTransform * bounds.GetCenter();
        }
        Vector3 away = centre - PlayerBody.GlobalPosition;
        float ahead = away.Dot(-PlayerBody.GlobalBasis.Z);
        float gap = lowest - setDown.GroundHeight;
        bool stands = setDown.Off && setDown.Way > 0.99f && !mount.Visible && Mathf.Abs(gap) < MaxGroundGap && ahead > MinAhead && ahead < MaxAhead;
        Check("set down, the real pack leaves the back and stands on the ground in front of the carrier", stands,
            $"off {setDown.Off}, way {setDown.Way:0.00}, worn hidden {!mount.Visible}, {gap * 100.0f:0.0} cm over the ground, {ahead:0.00} m ahead");
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

    /// <summary>Presses or lets go of an action the way a key does.</summary>
    private static void Press(StringName action, bool down)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = down });
    }

    /// <summary>Presses or lets go of the controller's Y button, as the pad sends it.</summary>
    private static void Pad(bool down)
    {
        Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = JoyButton.Y, Pressed = down, Device = 0 });
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
