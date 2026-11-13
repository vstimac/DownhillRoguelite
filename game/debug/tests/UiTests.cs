using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Map;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Ui;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the journal UI, against real game data: the hearts follow the carrier's
/// health (easing, the last one beating when low) and the need icons ask for a look only when low;
/// the ring beside the carrier shows when stamina drops, hides when it is full, follows him on
/// screen and shows a burning torch's time; the descent note shows elevation, circle and weather; the
/// hotbar lists what the pack holds with counts and its use key really eats; the tooltip slides in
/// for what the carrier looks at; notes appear for events; the inventory page opens (taking the
/// mouse), reorders by drag and drop and drinks through its action; the map page shows the field
/// map's chart and a click on it sets a waypoint; the journal records events and turns pages;
/// everything sits inside the screen. The front end: options are saved and applied the moment they
/// change; the start screen waits paused and silent, is driven by keys alone through its pages and
/// starts the run; the menus and Options sheet fit unclipped at 720p and 1080p.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/UiTests.tscn
/// </summary>
public partial class UiTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public Hud? Ui { get; set; }
    [Export] public FieldMap? Map { get; set; }

    private readonly List<string> _failures = new();

    private const string TestOptionsPath = "user://options_uitests.cfg";

    public override async void _Ready()
    {
        // Tests change options: keep the player's own file untouched and start from defaults.
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(TestOptionsPath));
        GameOptions.UseFile(TestOptionsPath);
        CarrierInventory? inventory = PlayerBody?.GetNodeOrNull<CarrierInventory>("Inventory");
        CarrierNeeds? needs = PlayerBody?.GetNodeOrNull<CarrierNeeds>("Needs");
        if (PlayerBody?.Health == null || Streamer == null || Ui == null || Map == null || inventory == null || needs == null)
        {
            GD.PushError("UiTests: PlayerBody (Health, Inventory, Needs), Streamer, Ui and Map are needed.");
            GetTree().Quit(1);
            return;
        }
        PlayerBody.Teleport(Streamer.SpawnPoint + Vector3.Up * 0.1f, 0.0f);
        await Wait(1.0f);

        // GC_STRESS=1: collect garbage nonstop on another thread, which turns finalizer races (a resource
        // finalized while the game loads it again) from rare crashes into certain ones.
        if (OS.HasEnvironment("GC_STRESS")) { _ = System.Threading.Tasks.Task.Run(() => { while (true) { System.GC.Collect(); System.GC.WaitForPendingFinalizers(); System.Threading.Thread.Sleep(2); } }); }
        await TestHearts(PlayerBody.Health);
        await TestNeedIcons(needs);
        await TestRing(PlayerBody);
        TestDescent();
        await TestHotbar(needs);
        await TestTooltip();
        TestNotes();
        await TestAlerts(needs);
        await TestInventory(needs);
        await TestMap();
        await TestJournal();
        TestDragging();
        TestSubtitles();
        TestLayout();
        await TestOptionsSaved();
        TestMouseLook();
        await TestMenuLayout();
        await TestStartScreen();

        if (_failures.Count == 0)
        {
            GD.Print("UI TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"UI TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private async Task TestHearts(CarrierHealth health)
    {
        HeartRow hearts = Ui!.Hearts!;
        await Wait(0.2f);
        int expected = Mathf.CeilToInt(health.EffectiveMax / hearts.HealthPerHeart - 0.001f);
        bool full = hearts.HeartCount == expected && hearts.FilledHearts == expected && !hearts.Warning;
        health.TakeDamage(health.Max * 0.85f, "a test");
        await Wait(0.1f);
        float value = hearts.Value;
        float easing = hearts.Shown;
        await Wait(1.0f);
        bool settled = Mathf.Abs(hearts.Shown - value) < 1.0f;
        int left = Mathf.CeilToInt(health.Current / hearts.HealthPerHeart - 0.001f);
        Check("health is a row of hearts: one per share of health, emptying with it, easing, beating when low",
            full && Mathf.IsEqualApprox(value, health.Current) && easing > value + 1.0f && settled && hearts.Warning && hearts.FilledHearts == left && left < expected,
            $"{expected} hearts for {health.EffectiveMax:0} health (full at first {full}); health {health.Current:0} shown as {value:0} (easing from {easing:0}, settled {settled}), "
            + $"{hearts.FilledHearts} filled (want {left}), warning {hearts.Warning}");
        health.Refill();
        await Wait(0.2f);
    }

    /// <summary>Hunger, thirst and warmth stay small and faded while fine, and grow, turn solid and warn when low.</summary>
    private async Task TestNeedIcons(CarrierNeeds needs)
    {
        NeedIcon hunger = Ui!.HungerIcon!;
        NeedIcon thirst = Ui.ThirstIcon!;
        NeedIcon warmth = Ui.WarmthIcon!;
        needs.SetNeeds(90.0f, 90.0f);
        await Wait(1.0f);
        bool calm = hunger.Scale.X < 0.9f && hunger.Modulate.A < 0.8f && !hunger.Warning && hunger.Attention <= 0.0f;
        float calmScale = hunger.Scale.X;
        needs.SetNeeds(10.0f, 90.0f);
        await Wait(1.0f);
        bool urgent = hunger.Warning && hunger.Attention >= 1.0f && hunger.Scale.X > calmScale + 0.15f && hunger.Modulate.A > 0.95f;
        bool othersCalm = !thirst.Warning && thirst.Scale.X < 0.9f && Mathf.IsEqualApprox(warmth.Value, (1.0f - GameEvents.CarrierCold) * 100.0f);
        Check("the need icons stay small while fine and grow and warn only when low", calm && urgent && othersCalm,
            $"fed: scale {calmScale:0.00}, calm {calm}; starving: scale {hunger.Scale.X:0.00}, alpha {hunger.Modulate.A:0.00}, warning {hunger.Warning}; "
            + $"thirst scale {thirst.Scale.X:0.00}, warmth {warmth.Value:0} (cold {GameEvents.CarrierCold:0.00})");
        needs.SetNeeds(90.0f, 90.0f);
        await Wait(0.3f);
    }

    /// <summary>The ring beside the carrier: hidden while stamina is full, up when it drops, glued to him on screen, and a lit torch adds its arc.</summary>
    private async Task TestRing(PlayerController player)
    {
        ContextRing ring = Ui!.Ring!;
        CarrierStamina? stamina = player.Stamina;
        CarrierTorch? torch = player.GetNodeOrNull<CarrierTorch>("Torch");
        Camera3D? camera = GetViewport().GetCamera3D();
        if (stamina == null || camera == null || torch == null)
        {
            Check("the stamina ring has a carrier, a camera and a torch to read", false, $"stamina {stamina != null}, camera {camera != null}, torch {torch != null}");
            return;
        }
        stamina.Refill();
        await Wait(1.6f);
        bool hiddenFull = !ring.Visible && ring.StaminaAlpha < 0.01f && !ring.StaminaWanted;

        stamina.SetCurrent(stamina.EffectiveMax * 0.5f);
        await Wait(0.4f);
        bool shown = ring.Visible && ring.StaminaWanted && ring.StaminaAlpha > 0.9f && ring.Stamina < 0.75f;
        float shownAt = ring.Stamina;

        // It follows him: wherever the camera sees him, the ring's centre is that point plus its pixel offset.
        float worst = 0.0f;
        Vector2 first = ring.CarrierPoint;
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        // (The ring keeps inside the screen's edges; skip a comparison where that clamp is at work.)
        float keep = ring.RingSize * 0.5f + ring.EdgeMargin;
        bool clamps = screen.X > keep * 2.0f && screen.Y > keep * 2.0f;
        int compared = 0;
        for (int step = 0; step < 3; step++)
        {
            player.Teleport(player.GlobalPosition + player.GlobalBasis.X * 1.5f, 0.0f);
            await Wait(0.3f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Vector3 world = GameEvents.CarrierPosition + Vector3.Up * ring.AnchorHeight;
            if (camera.IsPositionBehind(world))
            {
                continue;
            }
            Vector2 want = camera.UnprojectPosition(world) + ring.ScreenOffset;
            if (clamps && !new Rect2(Vector2.Zero, screen).Grow(-keep).HasPoint(want))
            {
                continue;
            }
            worst = Mathf.Max(worst, (ring.Centre - want).Length());
            compared++;
        }
        bool follows = compared > 0 && worst < 3.0f && ring.OnScreen;
        Check("the ring shows when stamina drops and follows the carrier on screen", hiddenFull && shown && follows,
            $"hidden while full {hiddenFull}; at {shownAt * 100.0f:0}% visible {shown} (alpha {ring.StaminaAlpha:0.00}); "
            + $"anchor {first} → {ring.CarrierPoint}, centre off by {worst:0.0} px over {compared} moves");

        stamina.Refill();
        await Wait(1.6f);
        bool hiddenAgain = !ring.Visible && ring.StaminaAlpha < 0.01f;

        GameEvents.RequestTorch(ring.TorchFullSeconds * 0.5f);
        await Wait(0.4f);
        bool torchShown = ring.Visible && ring.TorchAlpha > 0.9f && Mathf.Abs(ring.Torch - 0.5f) < 0.02f && ring.StaminaAlpha < 0.01f;
        float torchAt = ring.Torch;
        torch.Left = 0.02f;
        await Wait(1.0f);
        bool torchGone = !ring.Visible && ring.TorchAlpha < 0.01f;
        Check("the ring hides when stamina is full, and shows a lit torch's time until it goes out", hiddenAgain && torchShown && torchGone,
            $"hidden when refilled {hiddenAgain}; torch arc at {torchAt * 100.0f:0}% shown {torchShown}; gone when out {torchGone}");
    }

    private void TestDescent()
    {
        string text = Ui!.Descent!.Text;
        Check("the descent note shows elevation, circle and weather", text.Contains("Elevation") && text.Contains("Limbo") && text.Contains("Weather"),
            text.Replace("\n", " | "));
    }

    private async Task TestHotbar(CarrierNeeds needs)
    {
        GameEvents.PublishItemsGranted(new[] { "res://inventory/items/ration.tres", "res://inventory/items/ration.tres", "res://inventory/items/water_flask.tres" });
        await Wait(0.3f);
        Hotbar bar = Ui!.Hotbar!;
        ItemSlot first = bar.SlotNodes[0];
        bool listed = first.Item?.ItemId == "ration" && first.Count == 2 && bar.SlotNodes[1].Item?.ItemId == "water_flask";
        needs.SetNeeds(40.0f, 40.0f);
        bar.Select(0);
        bool asked = bar.UseSelected();
        await Wait(0.3f);
        bool ate = needs.Hunger > 40.0f && bar.SlotNodes[0].Count == 1;
        Check("the hotbar lists the pack's items with counts, and its use key eats", listed && asked && ate,
            $"slot 1 {first.Item?.ItemId} ×{first.Count}, slot 2 {bar.SlotNodes[1].Item?.ItemId}; after using: hunger {needs.Hunger:0}, rations {bar.SlotNodes[0].Count}");
    }

    private async Task TestTooltip()
    {
        WorldItem item = new() { Definition = ItemText.Find("water_flask") };
        AddChild(item);
        GameEvents.PublishFocus(item);
        await Wait(0.4f);
        bool shown = Ui!.Tooltip!.Showing && Ui.Tooltip.Visible && Ui.Tooltip.TitleText == "Water flask";
        GameEvents.PublishFocus(null);
        await Wait(0.4f);
        bool hidden = !Ui.Tooltip.Showing && !Ui.Tooltip.Visible;
        item.QueueFree();
        Check("the tooltip slides in for what the carrier looks at, and away again", shown && hidden,
            $"shown \"{Ui.Tooltip.TitleText}\" {shown}, hidden after {hidden}");
    }

    private void TestNotes()
    {
        GameEvents.PublishItemPickedUp("rope", "Supply");
        GameEvents.PublishStationReached(3);
        IReadOnlyList<string> recent = Ui!.Notes!.Recent;
        bool noted = recent.Count >= 2 && recent[^2].Contains("Rope") && recent[^1].Contains("Station 3");
        Check("events pin paper notes", noted, $"recent notes: {string.Join(" / ", recent)}");
    }

    private async Task TestAlerts(CarrierNeeds needs)
    {
        needs.SetNeeds(10.0f, 90.0f);
        await Wait(0.5f);
        bool hungry = Ui!.Alerts!.Shown.Contains("Hungry") && !Ui.Alerts.Shown.Contains("Thirsty") && Ui.Alerts.Visible == GameOptions.LowStatAlerts;
        needs.SetNeeds(90.0f, 90.0f);
        await Wait(0.5f);
        bool cleared = !Ui.Alerts.Shown.Contains("Hungry");
        Check("low stats are listed under the need icons, and leave when they recover", hungry && cleared,
            $"listed [{string.Join(", ", Ui.Alerts.Shown)}] after eating; hungry {hungry}, cleared {cleared}");
    }

    private async Task TestInventory(CarrierNeeds needs)
    {
        InventoryScreen page = Ui!.Inventory!;
        Ui.Toggle(page);
        await Wait(0.6f);
        bool open = page.Visible && page.IsOpen && GameEvents.IsScreenOpen && Input.MouseMode == Input.MouseModeEnum.Visible;
        bool frozen = GetTree().Paused && GameEvents.PagesFrozen;
        string firstBefore = "";
        if (page.Order.Count > 0)
        {
            firstBefore = page.Order[0];
        }
        ItemSlot? slot = null;
        foreach (Node child in page.GetChildren())
        {
            if (child is ItemSlot s && s.Draggable && s.Index == 0)
            {
                slot = s;
            }
        }
        slot?.DroppedOn?.Invoke(0, 1);
        bool swapped = page.Order.Count > 1 && page.Order[1] == firstBefore;
        int flask = page.Order.ToList().IndexOf("water_flask");
        needs.SetNeeds(60.0f, 30.0f);
        page.Select(flask);
        page.Call("UseSelected");
        await Wait(0.3f);
        bool drank = needs.Thirst > 30.0f && !page.Order.Contains("water_flask");
        page.Close();
        await Wait(0.5f);
        bool running = !GetTree().Paused && !GameEvents.PagesFrozen;
        Check("the inventory page takes the mouse, stops the world while open, reorders by drag and drop, and drinks",
            open && frozen && running && swapped && drank && !page.Visible && !GameEvents.IsScreenOpen,
            $"open {open}, world stopped {frozen} then running again {running}, swapped {swapped}, drank {drank} (thirst {needs.Thirst:0}), closed {!page.Visible}");
    }

    private async Task TestMap()
    {
        Map!.SetOpen(true);
        await Wait(0.6f);
        MapScreen page = Ui!.MapPage!;
        bool open = page.Visible && page.IsOpen && !Map.ShowSheet;
        int before = Map.Chart.Waypoints.Count;
        Map.PressAt(new Vector2(Map.TextureSize * 0.5f, Map.TextureSize * 0.3f));
        Map.Release();
        await Wait(0.1f);
        bool waypoint = Map.Chart.Waypoints.Count == before + 1;
        // Dragging moves the map instead of setting a waypoint; C/Centre goes back; the page resizes.
        int marks = Map.Chart.Waypoints.Count;
        Map.DrawMode = false;
        Map.PressAt(new Vector2(300, 300));
        Map.DragTo(new Vector2(360, 340));
        Map.DragTo(new Vector2(420, 380));
        Map.Release();
        bool panned = Map.Pan.Length() > 1.0f && Map.Chart.Waypoints.Count == marks;
        Map.Recentre();
        bool centred = Map.Pan == Vector2.Zero;
        float side = page.ChartSide;
        page.ResizeTo(page.Size + new Vector2(200, 200));
        bool bigger = page.ChartSide > side;
        page.ResizeTo(new Vector2(860, 690));
        Check("the map drags like a sheet, recentres, and its page resizes", panned && centred && bigger,
            $"panned {panned} ({Map.Pan}), centred {centred}, chart {side} to bigger {bigger}");
        Map.SetOpen(false);
        await Wait(0.5f);
        Check("the map page unfolds with the field map's chart and a click sets a waypoint", open && waypoint && !page.Visible,
            $"open {open} (3D sheet off {!Map.ShowSheet}), waypoints {before} → {Map.Chart.Waypoints.Count}, closed {!page.Visible}");
    }

    private async Task TestJournal()
    {
        JournalLog log = Ui!.Log!;
        bool summit = log.Entries.Count > 0 && log.Entries[0].Text.Contains("summit");
        GameEvents.PublishBoonChosen("Steady Legs");
        for (int i = 0; i < JournalScreen.EntriesPerPage + 2; i++)
        {
            log.Write($"Test entry {i}");
        }
        bool boon = false;
        foreach (JournalLog.Entry entry in log.Entries)
        {
            boon = boon || entry.Text.Contains("Steady Legs");
        }
        JournalScreen page = Ui.Journal!;
        Ui.Toggle(page);
        await Wait(0.6f);
        int last = page.Page;
        bool turned = page.Turn(-1);
        await Wait(0.5f);
        bool earlier = page.Page == last - 1 && page.PageText.Length > 0;
        page.Close();
        await Wait(0.5f);
        Check("the journal records events and turns its pages", summit && boon && page.Pages > 1 && turned && earlier,
            $"first entry about the summit {summit}, boon noted {boon}, {page.Pages} pages, turned back to {page.Page + 1}");
    }

    private void TestLayout()
    {
        List<string> outside = new();
        List<string> overlaps = new();
        foreach (Vector2 size in new[] { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(2560, 1440) })
        {
            Ui!.LayoutFor(size);
            Rect2 screen = new(Vector2.Zero, size);
            Control?[] parts = { Ui.Hearts, Ui.HungerIcon, Ui.ThirstIcon, Ui.WarmthIcon, Ui.Alerts, Ui.Descent, Ui.Compass, Ui.Objectives, Ui.Equipment, Ui.Hotbar, Ui.Notes, Ui.Inventory, Ui.MapPage, Ui.Journal };
            foreach (Control? control in parts)
            {
                if (control != null && !screen.Encloses(new Rect2(control.Position, control.Size)))
                {
                    outside.Add($"{control.GetType().Name} at {size}");
                }
            }
            // The play HUD's pieces must not cover each other.
            Control?[] hud = { Ui.Hearts, Ui.HungerIcon, Ui.ThirstIcon, Ui.WarmthIcon, Ui.Alerts, Ui.Compass, Ui.Descent, Ui.Objectives, Ui.Equipment, Ui.Hotbar, Ui.Notes };
            for (int i = 0; i < hud.Length; i++)
            {
                for (int j = i + 1; j < hud.Length; j++)
                {
                    if (hud[i] != null && hud[j] != null && new Rect2(hud[i]!.Position, hud[i]!.Size).Intersects(new Rect2(hud[j]!.Position, hud[j]!.Size)))
                    {
                        overlaps.Add($"{hud[i]!.Name}/{hud[j]!.Name} at {size}");
                    }
                }
            }
        }
        Ui!.Layout();
        Check("the HUD and pages fit 720p to 1440p without overlapping", outside.Count == 0 && overlaps.Count == 0,
            $"outside: [{string.Join(", ", outside)}], overlapping: [{string.Join(", ", overlaps)}]");
    }

    /// <summary>Rows of the shared Options sheet save to the options file at once and are applied (buses, captions, camera speed).</summary>
    private async Task TestOptionsSaved()
    {
        CanvasLayer layer = new();
        AddChild(layer);
        OptionsPanel sheet = new() { ShowCharacter = true };
        layer.AddChild(sheet);
        sheet.FitInto(new Rect2(Vector2.Zero, new Vector2(1280, 720)));
        await Wait(0.1f);
        SliderRow? master = sheet.Row("Master volume") as SliderRow;
        ChoiceRow? subtitles = sheet.Row("Subtitles") as ChoiceRow;
        SliderRow? look = sheet.Row("Mouse sensitivity") as SliderRow;
        ChoiceRow? window = sheet.Row("Window") as ChoiceRow;
        if (master == null || subtitles == null || look == null || window == null)
        {
            Check("options are saved and applied the moment they change", false, "rows missing");
            layer.QueueFree();
            return;
        }
        for (int i = 0; i < 10; i++)
        {
            master.Step(-1);
        }
        subtitles.Step(1);
        look.Step(1);
        window.Step(1);
        await Wait(0.05f);
        ConfigFile file = new();
        bool read = file.Load(TestOptionsPath) == Error.Ok;
        float savedMaster = -1.0f;
        bool savedSubtitles = false;
        bool savedDisplay = false;
        if (read)
        {
            savedMaster = (float)file.GetValue("options", "volume_master", -1.0f);
            savedSubtitles = !(bool)file.GetValue("options", "subtitles", true);
            savedDisplay = (int)file.GetValue("options", "display", 0) == (int)DisplayMode.Borderless;
        }
        float busDb = AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Master"));
        float wantDb = Mathf.LinearToDb(Mathf.Max(GameOptions.MasterVolume * OptionsApplier.Hush, 0.0001f));
        bool bus = Mathf.Abs(busDb - wantDb) < 0.1f && Mathf.IsEqualApprox(GameOptions.MasterVolume, 0.5f);
        bool captions = Ui?.Captions != null && !Ui.Captions.Visible;
        bool sensitivity = GameOptions.MouseSensitivity > 1.0f;
        Check("options are saved and applied the moment they change",
            Mathf.IsEqualApprox(savedMaster, 0.5f) && savedSubtitles && savedDisplay && bus && captions && sensitivity,
            $"saved master {savedMaster:0.00}, subtitles off {savedSubtitles}, borderless {savedDisplay}, Master bus {busDb:0.0} dB (want {wantDb:0.0}), captions hidden {captions}, mouse x{GameOptions.MouseSensitivity:0.00}");

        // Reset all asks once more, then puts everything back (and the rows show it).
        InkButton? reset = null;
        foreach (Node child in sheet.GetChildren())
        {
            if (child is InkButton button && button.Text == "Reset all")
            {
                reset = button;
            }
        }
        reset?.EmitSignal(BaseButton.SignalName.Pressed);
        bool held = !Mathf.IsEqualApprox(GameOptions.MasterVolume, 1.0f);
        reset?.EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(0.05f);
        SliderRow? fresh = sheet.Row("Master volume") as SliderRow;
        bool restored = Mathf.IsEqualApprox(GameOptions.MasterVolume, 1.0f) && GameOptions.Subtitles && Ui?.Captions?.Visible == true
            && fresh != null && Mathf.IsEqualApprox(fresh.Value, 1.0);
        Check("Reset all asks twice, then restores every option", reset != null && held && restored,
            $"button {reset != null}, held after one press {held}, restored {restored}");

        // Q / E (and the shoulder buttons) change tab.
        sheet.SetOpenNow(true);
        int before = sheet.CurrentTab;
        await Key(Godot.Key.E);
        bool tabbed = sheet.CurrentTab == before + 1 && sheet.Page(sheet.CurrentTab).Visible && !sheet.Page(before).Visible;
        Check("E steps to the next Options tab", tabbed, $"tab {before} -> {sheet.CurrentTab}");
        layer.QueueFree();
        await Wait(0.05f);
    }

    /// <summary>The start screen, pause menu and every Options tab fit 720p and 1080p with nothing clipped.</summary>
    /// <summary>The mouse options: linear sensitivity, acceleration off at 0, smoothing keeps the total, all saved.</summary>
    private void TestMouseLook()
    {
        MouseLookFilter filter = new();
        float dt = 1.0f / 60.0f;
        filter.Add(new Vector2(10.0f, 0.0f));
        float plain = filter.Take(dt, 1.0f, 0.0f, false, true).X;
        filter.Add(new Vector2(10.0f, 0.0f));
        float doubled = filter.Take(dt, 2.0f, 0.0f, false, true).X;
        bool linear = Mathf.IsEqualApprox(plain, 10.0f) && Mathf.IsEqualApprox(doubled, 20.0f);

        // Acceleration: a slow and a fast movement turn the same per count at 0, the fast one further when on.
        filter.Add(new Vector2(2.0f, 0.0f));
        float slowOn = filter.Take(dt, 1.0f, 1.0f, false, true).X / 2.0f;
        filter.Add(new Vector2(60.0f, 0.0f));
        float fastOn = filter.Take(dt, 1.0f, 1.0f, false, true).X / 60.0f;
        filter.Add(new Vector2(60.0f, 0.0f));
        float fastOff = filter.Take(dt, 1.0f, 0.0f, false, true).X / 60.0f;
        // Raw input off must leave the in-game acceleration working (the desktop gain comes first).
        filter.Add(new Vector2(60.0f, 0.0f));
        float fastNotRaw = filter.Take(dt, 1.0f, 1.0f, false, false).X;
        filter.Add(new Vector2(60.0f, 0.0f));
        float fastNotRawOff = filter.Take(dt, 1.0f, 0.0f, false, false).X;
        bool acceleration = Mathf.IsEqualApprox(fastOff, 1.0f) && fastOn > slowOn * 1.5f && fastNotRaw > fastNotRawOff * 1.5f;

        // Smoothing: less at once, the same in total.
        filter.Clear();
        filter.Add(new Vector2(30.0f, 0.0f));
        float first = filter.Take(dt, 1.0f, 0.0f, true, true).X;
        float total = first;
        for (int i = 0; i < 120; i++)
        {
            total += filter.Take(dt, 1.0f, 0.0f, true, true).X;
        }
        bool smoothing = first < 30.0f * 0.8f && Mathf.Abs(total - 30.0f) < 0.01f;

        // Saved and read back.
        GameOptions.SetMouseSmoothing(true);
        GameOptions.SetMouseAcceleration(0.4f);
        GameOptions.SetRawInput(false);
        ConfigFile file = new();
        bool saved = file.Load(TestOptionsPath) == Error.Ok
            && (bool)file.GetValue("options", "mouse_smoothing", false)
            && Mathf.IsEqualApprox((float)file.GetValue("options", "mouse_acceleration", 0.0f), 0.4f)
            && !(bool)file.GetValue("options", "raw_input", true);
        GameOptions.UseFile(TestOptionsPath);
        bool loaded = GameOptions.MouseSmoothing && Mathf.IsEqualApprox(GameOptions.MouseAcceleration, 0.4f) && !GameOptions.RawInput;
        GameOptions.RestoreDefaults();
        bool defaults = !GameOptions.MouseSmoothing && GameOptions.MouseAcceleration == 0.0f && GameOptions.RawInput && GameOptions.MouseSensitivity == 1.0f;
        Check("mouse options combine as they should and are saved", linear && acceleration && smoothing && saved && loaded && defaults,
            $"linear {linear} ({plain:0.00}, {doubled:0.00}), acceleration {acceleration} (slow {slowOn:0.00}, fast {fastOn:0.00}, off {fastOff:0.00}, not raw {fastNotRaw:0.0} vs {fastNotRawOff:0.0}), " +
            $"smoothing {smoothing} (first {first:0.0}, total {total:0.00}), saved {saved}, loaded {loaded}, defaults {defaults}");
    }

    private async Task TestMenuLayout()
    {
        Camera3D? game = GetViewport().GetCamera3D();
        CanvasLayer layer = new();
        AddChild(layer);
        StartScreen start = new() { ForceShow = true, WarmupSeconds = 1000.0f };
        PauseMenu pause = new();
        layer.AddChild(start);
        layer.AddChild(pause);
        await Wait(0.05f);
        List<string> problems = new();
        foreach (Vector2 size in new[] { new Vector2(1280, 720), new Vector2(1920, 1080) })
        {
            Rect2 screen = new(Vector2.Zero, size);
            start.LayoutFor(size);
            pause.LayoutFor(size);
            start.Options!.SetOpenNow(true);
            start.Characters!.SetOpenNow(true);
            start.About!.SetOpenNow(true);
            pause.Visible = true;
            pause.Options!.SetOpenNow(true);
            await Wait(0.05f);
            foreach (Control? control in new Control?[] { start.TitleLabel, start.FooterHint, start.FooterKeys, start.VersionLabel, start.Options, start.Characters, start.About, pause.Options })
            {
                if (control != null && !screen.Encloses(control.GetGlobalRect()))
                {
                    problems.Add($"{control.Name} outside at {size}");
                }
            }
            foreach (MenuItem item in start.Items.Concat(pause.Items))
            {
                if (!screen.Encloses(item.GetGlobalRect()) || item.TextWidth() > item.Size.X + 0.5f)
                {
                    problems.Add($"menu entry {item.Text} clipped at {size}");
                }
                if (item.Hint.Length > 0 && UiStyle.Hand.GetStringSize(item.Hint, HorizontalAlignment.Left, -1, UiStyle.TextSize - 2).X > start.FooterHint!.Size.X)
                {
                    problems.Add($"hint of {item.Text} too long at {size}");
                }
            }
            if (start.MenuBottom() > start.FooterHint!.Position.Y)
            {
                problems.Add($"menu runs into the footer at {size} ({start.MenuBottom():0} > {start.FooterHint.Position.Y:0})");
            }
            if (UiStyle.Wrapped(start.FooterKeys!.Text, UiStyle.SmallSize, 100000.0f).X > start.FooterKeys.Size.X)
            {
                problems.Add($"footer keys clipped at {size}");
            }
            foreach (OptionsPanel sheet in new[] { start.Options, pause.Options })
            {
                if (sheet.Tabs == null || !sheet.Tabs.FitsWidth())
                {
                    problems.Add($"tabs clipped at {size}");
                }
                for (int tab = 0; tab < OptionsPanel.TabNames.Length; tab++)
                {
                    sheet.ShowTab(tab, false);
                    await Wait(0.03f);
                    foreach (OptionRow row in sheet.Rows(tab))
                    {
                        if (!row.Fits())
                        {
                            problems.Add($"{OptionsPanel.TabNames[tab]}: '{row.Label}' clipped at {size} (row {row.Size.X:0} px)");
                        }
                        if (UiStyle.Wrapped(row.Hint, UiStyle.SmallSize, sheet.HintLabel!.Size.X).Y > sheet.HintLabel.Size.Y)
                        {
                            problems.Add($"hint for '{row.Label}' too long at {size}");
                        }
                    }
                }
                if (sheet.BackButton == null || !sheet.GetGlobalRect().Grow(1.0f).Encloses(sheet.BackButton.GetGlobalRect()))
                {
                    problems.Add($"Options Back button off the sheet at {size}");
                }
            }
            CharacterCarousel? carousel = start.Characters.Carousel;
            if (carousel != null)
            {
                for (int i = 0; i < carousel.Characters.Count; i++)
                {
                    if (carousel.NeededHeight() > carousel.Size.Y || carousel.NameWidth() > carousel.Size.X - 112.0f)
                    {
                        problems.Add($"character {carousel.Character?.DisplayName} clipped at {size} (needs {carousel.NeededHeight():0} of {carousel.Size.Y:0}, name {carousel.NameWidth():0} of {carousel.Size.X - 112.0f:0})");
                    }
                    carousel.Step(1);
                }
            }
        }
        Check("the start screen, pause menu and every Options tab fit 720p and 1080p unclipped", problems.Count == 0,
            $"[{string.Join("; ", problems)}]");
        pause.Visible = false;
        layer.QueueFree();
        await Wait(0.05f);
        GetTree().Paused = false;
        OptionsApplier.SetHush(1.0f);
        game?.MakeCurrent();
        GameOptions.SetCharacter("default");
        await Wait(0.1f);
    }

    /// <summary>The start screen waits paused and silent, is driven by keys alone through its pages, and starts the run.</summary>
    private async Task TestStartScreen()
    {
        Camera3D? game = GetViewport().GetCamera3D();
        StartScreen start = new() { ForceShow = true, WarmupSeconds = 0.1f, CurtainSeconds = 0.1f, FadeInSeconds = 0.5f };
        AddChild(start);
        await Wait(0.4f);
        bool waiting = start.Waiting && GetTree().Paused && OptionsApplier.Hush < 0.01f && GetViewport().GetCamera3D() == start.Camera;
        MenuItem? first = GetViewport().GuiGetFocusOwner() as MenuItem;

        await Press("ui_down");
        MenuItem? second = GetViewport().GuiGetFocusOwner() as MenuItem;
        // With a saved run on this PC the menu opens on Continue, otherwise on New Run.
        bool moved = (first?.Text == "New Run" || first?.Text == "Continue") && second != null && second != first;
        // New Run and Saved runs sit between the first entry and Character.
        for (int i = 0; i < 3 && (second?.Text == "New Run" || second?.Text == "Saved runs"); i++)
        {
            await Press("ui_down");
            second = GetViewport().GuiGetFocusOwner() as MenuItem;
        }

        // Character page (if there is more than one character), then back.
        bool character = true;
        if (start.Item("Character") is MenuItem characterItem && second == characterItem)
        {
            await Press("ui_accept");
            await Wait(0.3f);
            bool open = start.Current == StartScreen.Page.Character && start.Camera?.CurrentShot == MenuCamera.Shot.Portrait
                && GetViewport().GuiGetFocusOwner() is CharacterCarousel;
            int index = start.Characters?.Carousel?.Index ?? -1;
            await Press("ui_right");
            bool stepped = start.Characters?.Carousel?.Index != index;
            await Press("ui_cancel");
            await Wait(0.3f);
            character = open && stepped && start.Current == StartScreen.Page.Main;
            await Press("ui_down");
        }

        // Options, tab across with E, back out.
        bool options = false;
        if (GetViewport().GuiGetFocusOwner() is MenuItem item && item.Text == "Options")
        {
            await Press("ui_accept");
            await Wait(0.3f);
            bool open = start.Current == StartScreen.Page.Options && GetViewport().GuiGetFocusOwner() is OptionRow;
            await Key(Godot.Key.E);
            bool tabbed = start.Options?.CurrentTab == 1;
            await Press("ui_cancel");
            await Wait(0.3f);
            options = open && tabbed && start.Current == StartScreen.Page.Main && (GetViewport().GuiGetFocusOwner() as MenuItem)?.Text == "Options";
        }

        start.StartRun();
        bool woke = !GetTree().Paused;
        await Wait(2.4f);
        bool gone = !IsInstanceValid(start) || !start.IsInsideTree();
        bool handedOver = game == null || GetViewport().GetCamera3D() == game;
        bool loud = OptionsApplier.Hush > 0.99f;
        Check("the start screen waits paused and silent, keys alone reach Character and Options and back, and New Run starts the run",
            waiting && moved && character && options && woke && gone && handedOver && loud,
            $"waiting {waiting}, focus moved {moved} ({first?.Text} -> {second?.Text}), character page {character}, options page {options}, " +
            $"world woke {woke}, screen gone {gone}, run camera back {handedOver}, sound up {loud}");
        GameOptions.SetCharacter("default");
    }

    private async Task Press(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        await Wait(0.05f);
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
        await Wait(0.05f);
    }

    private async Task Key(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
        await Wait(0.05f);
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
        await Wait(0.05f);
    }

    private async Task Wait(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    /// <summary>Moved pieces and pages stay where the player put them through a re-layout; reset restores them.</summary>
    private void TestDragging()
    {
        UiLayoutEditor? editor = Ui?.LayoutEditor;
        if (Ui == null || editor == null || Ui.Hotbar == null || Ui.Inventory == null)
        {
            Check("the UI can be dragged", false, "no layout editor");
            return;
        }
        Vector2 screen = new(1280, 720);
        Ui.LayoutFor(screen);
        Vector2 home = Ui.Hotbar.Position;
        bool moved = editor.MoveTo("hotbar", new Vector2(40, 300)) && editor.MoveTo("inventory", new Vector2(10, 20));
        Ui.LayoutFor(screen);
        bool kept = Ui.Hotbar.Position.IsEqualApprox(new Vector2(40, 300)) && Ui.Inventory.Position.IsEqualApprox(new Vector2(10, 20));
        editor.MoveTo("hotbar", new Vector2(5000, 5000));
        Ui.LayoutFor(screen);
        bool onScreen = Ui.Hotbar.Position.X < screen.X && Ui.Hotbar.Position.Y < screen.Y;
        editor.ResetLayout();
        Ui.LayoutFor(screen);
        bool reset = Ui.Hotbar.Position.IsEqualApprox(home);
        Check("HUD pieces and pages can be moved, stay put through re-layout, stay on screen, and reset", moved && kept && onScreen && reset,
            $"moved {moved}, kept {kept}, on screen {onScreen}, reset {reset}");
    }

    /// <summary>Speech is captioned on the side it comes from, with the speaker's words.</summary>
    private void TestSubtitles()
    {
        Subtitles? captions = Ui?.Captions;
        Camera3D? camera = GetViewport().GetCamera3D();
        if (captions == null || camera == null)
        {
            Check("speech is subtitled from its direction", false, "no subtitles or camera");
            return;
        }
        Vector3 forward = -camera.GlobalBasis.Z;
        forward.Y = 0.0f;
        forward = forward.Normalized();
        Vector3 right = forward.Cross(Vector3.Up);
        Vector2 screen = new(1280, 720);
        float leftAngle = captions.AngleTo(camera.GlobalPosition - right * 3.0f);
        float rightAngle = captions.AngleTo(camera.GlobalPosition + right * 3.0f);
        float aheadAngle = captions.AngleTo(camera.GlobalPosition + forward * 5.0f);
        bool left = captions.AnchorFor(leftAngle, screen).X < screen.X * 0.3f;
        bool rightSide = captions.AnchorFor(rightAngle, screen).X > screen.X * 0.7f;
        bool ahead = Mathf.Abs(captions.AnchorFor(aheadAngle, screen).X - screen.X * 0.5f) < 2.0f;
        GameEvents.PublishSpeech(new SpeechLine("Companion", "Mind the scree.", camera.GlobalPosition - right * 2.0f, 2.0f));
        bool shown = captions.Count > 0 && captions.NewestText == "Mind the scree.";
        Check("speech is subtitled with its words, on the side the voice comes from", left && rightSide && ahead && shown,
            $"left {left}, right {rightSide}, ahead {ahead}, shown {shown}");
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
