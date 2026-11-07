using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Map;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the journal UI, against real game data: the dials follow the carrier's
/// vitals (easing, warning when low); the descent note shows elevation, circle and weather; the
/// hotbar lists what the pack holds with counts and its use key really eats; the tooltip slides in
/// for what the carrier looks at; notes appear for events; the inventory page opens (taking the
/// mouse), reorders by drag and drop and drinks through its action; the map page shows the field
/// map's chart and a click on it sets a waypoint; the journal records events and turns pages;
/// everything sits inside the screen.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/UiTests.tscn
/// </summary>
public partial class UiTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public Hud? Ui { get; set; }
    [Export] public FieldMap? Map { get; set; }

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
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

        await TestDials(PlayerBody.Health);
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

    private async Task TestDials(CarrierHealth health)
    {
        health.TakeDamage(health.Max * 0.85f, "a test");
        await Wait(0.1f);
        float value = Ui!.HealthDial!.Value;
        float easing = Ui.HealthDial.Shown;
        await Wait(1.0f);
        bool settled = Mathf.Abs(Ui.HealthDial.Shown - value) < 1.0f;
        Check("survival dials follow the vitals, ease to them and warn when low",
            Mathf.IsEqualApprox(value, health.Current) && easing > value + 1.0f && settled && Ui.HealthDial.Warning,
            $"health {health.Current:0} shown as {value:0} (easing from {easing:0}, settled {settled}), warning {Ui.HealthDial.Warning}");
        health.Refill();
        await Wait(0.2f);
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
        Check("low stats are listed under the dials, and leave when they recover", hungry && cleared,
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
            Control?[] parts = { Ui.HealthDial, Ui.StaminaDial, Ui.Descent, Ui.Compass, Ui.Equipment, Ui.Hotbar, Ui.Inventory, Ui.MapPage, Ui.Journal };
            foreach (Control? control in parts)
            {
                if (control != null && !screen.Encloses(new Rect2(control.Position, control.Size)))
                {
                    outside.Add($"{control.GetType().Name} at {size}");
                }
            }
            // The play HUD's pieces must not cover each other.
            Control?[] hud = { Ui.StaminaDial, Ui.Descent, Ui.Equipment, Ui.Hotbar };
            for (int i = 0; i < hud.Length; i++)
            {
                for (int j = i + 1; j < hud.Length; j++)
                {
                    if (hud[i] != null && hud[j] != null && new Rect2(hud[i]!.Position, hud[i]!.Size).Intersects(new Rect2(hud[j]!.Position, hud[j]!.Size)))
                    {
                        overlaps.Add($"{hud[i]!.GetType().Name}/{hud[j]!.GetType().Name} at {size}");
                    }
                }
            }
        }
        Ui!.Layout();
        Check("the HUD and pages fit 720p to 1440p without overlapping", outside.Count == 0 && overlaps.Count == 0,
            $"outside: [{string.Join(", ", outside)}], overlapping: [{string.Join(", ", overlaps)}]");
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
