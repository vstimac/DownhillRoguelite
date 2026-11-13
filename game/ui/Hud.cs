using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Map;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The journal UI (the director's hand-drawn parchment design, laid out like an action-adventure
/// HUD so the vitals are read at a glance): builds and lays out every piece and keeps them fed with
/// real game data. During play: health as a row of hearts top left with small hunger, thirst and
/// warmth icons under it (they grow and pulse only when they need a look), a ring beside the
/// carrier for stamina and the torch's time, the compass as a thin strip along the top, the descent
/// note top right, the equipment sheet on the left, a slim hotbar along the bottom, the look-at
/// tooltip on the right and paper notes for events. Pages: inventory (I), map (M, through the field map), journal (J), conversation
/// with the companion (Enter); Esc closes the
/// open page. Pages drag by their title, HUD pieces with Alt held (UiLayoutEditor).
/// While a page is open the mouse belongs to it (GameEvents.IsScreenOpen). Re-lays out
/// on any window size; hidden while packing and in clean screenshot mode.
/// </summary>
public partial class Hud : CanvasLayer
{
    public const string Group = "hud";
    [Export] public bool EnableCompanionChat { get; set; }
    private static readonly StringName ActionInventory = "inventory_open";
    private static readonly StringName ActionJournal = "journal_open";
    private static readonly StringName ActionTalk = "talk_open";
    private static readonly StringName ActionRadio = "radio_open";

    /// <summary>Screen margin of the corner pieces, in px.</summary>
    [Export] public float Margin { get; set; } = 18.0f;
    /// <summary>Gap between neighbouring pieces, in px.</summary>
    [Export] public float Gap { get; set; } = 10.0f;
    /// <summary>The compass strip and hotbar sit this far from the screen's top and bottom, in px.</summary>
    [Export] public float EdgeGap { get; set; } = 8.0f;
    /// <summary>Room kept under the need icons for the low-stat list, in px.</summary>
    [Export] public float AlertsHeight { get; set; } = 170.0f;

    public HeartRow? Hearts { get; private set; }
    public NeedIcon? HungerIcon { get; private set; }
    public NeedIcon? ThirstIcon { get; private set; }
    public NeedIcon? WarmthIcon { get; private set; }
    public ContextRing? Ring { get; private set; }
    public LowStatAlerts? Alerts { get; private set; }
    public DescentPanel? Descent { get; private set; }
    public ObjectivePanel? Objectives { get; private set; }
    public AchievementToast? Achievements { get; private set; }
    public ObjectiveBanner? Banner { get; private set; }
    public CompassStrip? Compass { get; private set; }
    public EquipmentPanel? Equipment { get; private set; }
    public Hotbar? Hotbar { get; private set; }
    public InteractionTooltip? Tooltip { get; private set; }
    public Notifications? Notes { get; private set; }
    public Subtitles? Captions { get; private set; }
    public InventoryScreen? Inventory { get; private set; }
    public PackQuickView? PackLook { get; private set; }
    public PackingRing? PackRing { get; private set; }
    public PackingScreen? Packing { get; private set; }
    public MapScreen? MapPage { get; private set; }
    public JournalScreen? Journal { get; private set; }
    public TalkScreen? Talk { get; private set; }
    public RadioScreen? RadioPage { get; private set; }
    public DialogueChoices? Choices { get; private set; }
    public ThrowZone? ThrowZone { get; private set; }
    public UiLayoutEditor? LayoutEditor { get; private set; }
    public JournalLog? Log { get; private set; }

    private Control? _root;
    private Control? _play;

    public override void _Ready()
    {
        Layer = 20;
        Name = "Hud";
        AddToGroup(Group);
        // The HUD keeps running while the world is stopped for the inventory or the pack (PageFreeze).
        ProcessMode = ProcessModeEnum.Always;
        GameOptions.Load();
        AddChild(new ItemIcons());
        Log = new JournalLog();
        AddChild(Log);

        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiStyle.MakeTheme() };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        _play = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _play.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_play);

        // Under everything else: the ring that follows the carrier on screen.
        Ring = new ContextRing { Name = "ContextRing" };
        _play.AddChild(Ring);
        Hearts = new HeartRow { Name = "Hearts" };
        _play.AddChild(Hearts);
        HungerIcon = Need("Hunger", InkIcon.Stomach, UiStyle.Hunger);
        ThirstIcon = Need("Thirst", InkIcon.Drop, UiStyle.Thirst);
        WarmthIcon = Need("Warmth", InkIcon.Snowflake, UiStyle.Cold);
        Alerts = new LowStatAlerts { Name = "LowStatAlerts", Size = new Vector2(300, AlertsHeight) };
        _play.AddChild(Alerts);
        Descent = new DescentPanel { Size = new Vector2(350, 172) };
        _play.AddChild(Descent);
        Compass = new CompassStrip { Name = "Compass" };
        _play.AddChild(Compass);
        Objectives = new ObjectivePanel { Name = "Objectives" };
        _play.AddChild(Objectives);
        Banner = new ObjectiveBanner { Name = "ObjectiveBanner" };
        _play.AddChild(Banner);
        Achievements = new AchievementToast { Name = "AchievementToast" };
        _play.AddChild(Achievements);
        Equipment = new EquipmentPanel { Size = new Vector2(184, 320) };
        _play.AddChild(Equipment);
        Hotbar = new Hotbar();
        _play.AddChild(Hotbar);
        Tooltip = new InteractionTooltip { Size = new Vector2(330, 170) };
        _play.AddChild(Tooltip);
        Notes = new Notifications { Size = new Vector2(330, 200) };
        _play.AddChild(Notes);
        Captions = new Subtitles { Name = "Subtitles" };
        _play.AddChild(Captions);
        _play.AddChild(new VaultPrompt { Name = "VaultPrompt" });
        Choices = new DialogueChoices { Name = "DialogueChoices" };
        _play.AddChild(Choices);

        // Behind the pages, over the world: an item dragged off the inventory page and dropped here is thrown away.
        ThrowZone = new ThrowZone { Name = "ThrowZone" };
        _root.AddChild(ThrowZone);
        Inventory = new InventoryScreen();
        Equipment.DraggedItem = Inventory.ItemIdAt;
        _root.AddChild(Inventory);
        ThrowZone.Inventory = Inventory;
        AddChild(new PageFreeze { Name = "PageFreeze", Inventory = Inventory });
        // The pack in 2.5D: the quick look (short Tab), the wait ring, and the packing page (held Tab).
        PackLook = new PackQuickView { Name = "PackQuickView" };
        _root.AddChild(PackLook);
        PackRing = new PackingRing { Name = "PackingRing" };
        _root.AddChild(PackRing);
        Packing = new PackingScreen { Name = "PackingScreen" };
        _root.AddChild(Packing);
        AddChild(new OptionsApplier { Name = "OptionsApplier" });
        MapPage = new MapScreen();
        foreach (Node node in GetTree().GetNodesInGroup(FieldMap.Group))
        {
            if (node is FieldMap map)
            {
                MapPage.Map = map;
                map.ShowSheet = false;
            }
        }
        _root.AddChild(MapPage);
        Journal = new JournalScreen { Log = Log };
        _root.AddChild(Journal);
        Talk = new TalkScreen();
        _root.AddChild(Talk);
        Inventory.Closed += OnPageClosed;
        Journal.Closed += OnPageClosed;
        Talk.Closed += OnPageClosed;
        GameEvents.TalkRequested += OnTalkRequested;
        RadioPage = new RadioScreen();
        _root.AddChild(RadioPage);
        RadioPage.Closed += OnPageClosed;
        GameEvents.RadioPageRequested += OnRadioPageRequested;
        GameEvents.RadioChanged += OnRadioChanged;

        // The player can drag everything: pages by their title, HUD pieces with Alt held.
        LayoutEditor = new UiLayoutEditor { Name = "LayoutEditor" };
        _root.AddChild(LayoutEditor);
        // New names for the pieces that changed shape, so a layout saved for the old dials isn't applied to them.
        LayoutEditor.AddPiece("hearts", Hearts);
        LayoutEditor.AddPiece("need_hunger", HungerIcon);
        LayoutEditor.AddPiece("need_thirst", ThirstIcon);
        LayoutEditor.AddPiece("need_warmth", WarmthIcon);
        LayoutEditor.AddPiece("descent", Descent);
        LayoutEditor.AddPiece("compass_strip", Compass);
        LayoutEditor.AddPiece("objectives", Objectives);
        LayoutEditor.AddPiece("equipment", Equipment);
        LayoutEditor.AddPiece("hotbar", Hotbar);
        LayoutEditor.AddPiece("tooltip", Tooltip);
        LayoutEditor.AddPiece("notes", Notes);
        LayoutEditor.AddPage("inventory", Inventory);
        LayoutEditor.AddPage("map", MapPage);
        LayoutEditor.AddPage("journal", Journal);
        LayoutEditor.AddPage("talk", Talk);
        LayoutEditor.AddPage("radio", RadioPage);
        LayoutEditor.Place = PlacePiece;
        LayoutEditor.EditingChanged += OnEditing;
        MapPage.Closed += OnPageClosed;

        GameEvents.VitalsUpdated += OnVitals;
        GetViewport().SizeChanged += Layout;
        Layout();
    }

    public override void _ExitTree()
    {
        GameEvents.VitalsUpdated -= OnVitals;
        GameEvents.TalkRequested -= OnTalkRequested;
        GameEvents.RadioPageRequested -= OnRadioPageRequested;
        GameEvents.RadioChanged -= OnRadioChanged;
        if (GameEvents.IsScreenOpen)
        {
            GameEvents.PublishScreenOpen(false);
        }
    }

    private NeedIcon Need(string name, InkIcon icon, Color colour)
    {
        NeedIcon need = new() { Name = name, Icon = icon, RingColour = colour };
        _play!.AddChild(need);
        return need;
    }

    private int _heartRows = 1;

    private void OnVitals(VitalsSnapshot vitals)
    {
        Hearts?.SetValue(vitals.Health, vitals.MaxHealth);
        HungerIcon?.SetValue(vitals.Hunger, 100.0f);
        ThirstIcon?.SetValue(vitals.Thirst, 100.0f);
        // Warmth is what the cold hasn't taken: full while warm or sheltered.
        WarmthIcon?.SetValue((1.0f - Mathf.Clamp(GameEvents.CarrierCold, 0.0f, 1.0f)) * 100.0f, 100.0f);
        if (Hearts != null && Hearts.RowsUsed != _heartRows)
        {
            // More (or fewer) rows of hearts: the icons under them move.
            _heartRows = Hearts.RowsUsed;
            Layout();
        }
    }

    /// <summary>Places everything for the current window size.</summary>
    public void Layout()
    {
        LayoutFor(GetViewport().GetVisibleRect().Size);
    }

    /// <summary>Places everything for a screen of <paramref name="screen"/> pixels (also used by tests).</summary>
    public void LayoutFor(Vector2 screen)
    {
        float cornerRight = Margin;
        float needsBottom = Margin;
        if (Hearts != null)
        {
            Hearts.Position = new Vector2(Margin, EdgeGap + 4.0f);
            cornerRight = Hearts.Position.X + Hearts.Size.X;
            needsBottom = Hearts.Position.Y + Hearts.Size.Y;
        }
        NeedIcon?[] needs = { HungerIcon, ThirstIcon, WarmthIcon };
        float needsTop = needsBottom + 2.0f;
        for (int i = 0; i < needs.Length; i++)
        {
            if (needs[i] != null)
            {
                needs[i]!.Position = new Vector2(Margin + i * (needs[i]!.Size.X + Gap), needsTop);
                needsBottom = needs[i]!.Position.Y + needs[i]!.Size.Y;
            }
        }
        if (Alerts != null)
        {
            Alerts.Position = new Vector2(Margin, needsBottom + Gap);
        }
        float stripBottom = EdgeGap;
        float stripRight = 0.0f;
        if (Compass != null)
        {
            // Top centre; in a narrow window it slides right, clear of the hearts.
            Compass.Position = new Vector2(Mathf.Max((screen.X - Compass.Size.X) * 0.5f, cornerRight + Gap), EdgeGap);
            stripBottom = Compass.Position.Y + Compass.Size.Y;
            stripRight = Compass.Position.X + Compass.Size.X;
        }
        float descentLeft = screen.X - Margin;
        float descentBottom = stripBottom;
        if (Descent != null)
        {
            descentLeft = screen.X - Margin - Descent.Size.X;
            float top = Margin - 4.0f;
            if (descentLeft < stripRight + Gap)
            {
                // No room beside the compass strip: the note goes under it.
                top = stripBottom + Gap * 0.6f;
            }
            Descent.Position = new Vector2(descentLeft, top);
            descentBottom = top + Descent.Size.Y;
        }
        float hotbarTop = screen.Y - EdgeGap;
        if (Hotbar != null)
        {
            Hotbar.Position = new Vector2((screen.X - Hotbar.Size.X) * 0.5f, screen.Y - Hotbar.Size.Y - EdgeGap);
            hotbarTop = Hotbar.Position.Y;
        }
        float equipmentRight = Margin;
        if (Equipment != null)
        {
            // Left side, mid height: under the low-stat list when the screen is tall enough, never over the hotbar's row.
            float top = Mathf.Max(needsBottom + Gap + AlertsHeight, screen.Y * 0.5f - Equipment.Size.Y * 0.5f);
            top = Mathf.Min(top, hotbarTop - Gap - Equipment.Size.Y);
            Equipment.Position = new Vector2(Margin - 4.0f, Mathf.Max(top, needsBottom + Gap));
            equipmentRight = Equipment.Position.X + Equipment.Size.X;
        }
        ObjectivesStacked = false;
        if (Objectives != null)
        {
            // Top centre under the compass strip, between the hearts and the descent note, when there's room;
            // else under the descent note, with the notes following under it.
            float left = cornerRight + Gap;
            float right = descentLeft - Gap - ObjectivePanel.PanelWidth;
            if (right >= left)
            {
                Objectives.Position = new Vector2(Mathf.Clamp((screen.X - ObjectivePanel.PanelWidth) * 0.5f, left, right), stripBottom + Gap * 0.8f);
                if (Banner != null)
                {
                    Banner.MinTop = Objectives.Position.Y + Objectives.MaxHeight + 12.0f;
                    Banner.FreeSpan = new Vector2(equipmentRight + 12.0f, screen.X);
                }
            }
            else
            {
                ObjectivesStacked = true;
                Objectives.Position = new Vector2(descentLeft, descentBottom + 8.0f);
                if (Banner != null)
                {
                    // The card goes between the equipment sheet and the goal note, under the corner vitals.
                    float free = equipmentRight + 12.0f;
                    Banner.MinTop = needsBottom + Gap;
                    Banner.FreeSpan = new Vector2(free, Objectives.Position.X - 12.0f);
                    if (Banner.FreeSpan.Y - Banner.FreeSpan.X < Banner.MinWidth)
                    {
                        // Too narrow beside it: the card goes below the goal note instead.
                        Banner.MinTop = Objectives.Position.Y + Objectives.MaxHeight + 12.0f;
                        Banner.FreeSpan = new Vector2(free, screen.X - 12.0f);
                    }
                }
            }
        }
        PlaceNotes(screen);
        Tooltip?.SetHome(new Vector2(screen.X - 350, screen.Y - 300));
        foreach (ParchmentPanel? page in new ParchmentPanel?[] { Inventory, MapPage, Journal, Talk, RadioPage })
        {
            if (page != null)
            {
                page.Position = (screen - page.Size) * 0.5f;
                page.PivotOffset = page.Size * 0.5f;
            }
        }
        LayoutEditor?.Apply(screen);
    }

    /// <summary>True when the goal note sits under the descent note (narrow windows) and the notes follow under it.</summary>
    public bool ObjectivesStacked { get; private set; }

    /// <summary>The notes under the descent note, or under the goal note's current height when it is stacked there.</summary>
    private void PlaceNotes(Vector2 screen)
    {
        if (Notes == null)
        {
            return;
        }
        if (LayoutEditor != null && LayoutEditor.Moved.ContainsKey("notes"))
        {
            return;
        }
        float top = Margin + 182.0f;
        if (Descent != null)
        {
            top = Descent.Position.Y + Descent.Size.Y + Gap + 2.0f;
        }
        if (ObjectivesStacked && Objectives != null && Objectives.Visible)
        {
            top = Objectives.Position.Y + Objectives.Size.Y + 8.0f;
        }
        Notes.Position = new Vector2(screen.X - Margin - Notes.Size.X, top);
    }

    /// <summary>Someone was spoken to (a traveller with E, the companion with Enter): open the conversation.</summary>
    private void OnTalkRequested(SpeakerInfo speaker)
    {
        if (speaker.IsCompanion && !EnableCompanionChat) { return; }
        if (Talk != null && !(Talk.IsOpen && Talk.Visible) && !GameEvents.IsPacking)
        {
            Toggle(Talk);
        }
    }

    /// <summary>E at the summit's Reno 4: the radio page opens (if it isn't already).</summary>
    private void OnRadioPageRequested()
    {
        if (RadioPage != null && !(RadioPage.IsOpen && RadioPage.Visible) && !GameEvents.IsPacking)
        {
            Toggle(RadioPage);
        }
    }

    private string _radioNoted = "";

    /// <summary>A paper note names each new song as it starts.</summary>
    private void OnRadioChanged(RadioState radio)
    {
        if (!radio.Playing || radio.TrackName.Length == 0 || radio.TrackName == _radioNoted)
        {
            return;
        }
        _radioNoted = radio.TrackName;
        Notes?.Note("Radio: " + radio.TrackName);
    }

    private void PlacePiece(Control piece, Vector2 at)
    {
        if (piece == Tooltip && Tooltip != null)
        {
            Tooltip.SetHome(at);
        }
        else
        {
            piece.Position = at;
        }
    }

    private bool _editHintShown;

    private void OnEditing(bool editing)
    {
        if (editing && !_editHintShown && Notes != null)
        {
            _editHintShown = true;
            Notes.Note("Drag to move the UI · Alt+Backspace resets");
        }
    }

    /// <summary>True while the pause menu or title screen holds the game (not the pages' own freeze).</summary>
    private bool MenuPaused => GetTree().Paused && !GameEvents.PagesFrozen;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (GameEvents.IsPacking || MenuPaused)
        {
            return;
        }
        if (@event.IsActionPressed(ActionInventory))
        {
            Toggle(Inventory);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(ActionJournal))
        {
            Toggle(Journal);
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed(ActionRadio))
        {
            Toggle(RadioPage);
            GetViewport().SetInputAsHandled();
        }
        else if (EnableCompanionChat && @event.IsActionPressed(ActionTalk) && Talk != null && !(Talk.IsOpen && Talk.Visible))
        {
            // Enter talks with the companion; once the page is open, Enter sends what's typed.
            GameEvents.RequestTalkTo(SpeakerInfo.Companion);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Esc closes the open page before the pause menu sees it.
        if (!@event.IsActionPressed("pause") || MenuPaused)
        {
            return;
        }
        foreach (ParchmentPanel? page in new ParchmentPanel?[] { Inventory, Journal, Talk, RadioPage, MapPage })
        {
            if (page != null && page.IsOpen && page.Visible)
            {
                page.Close();
                GetViewport().SetInputAsHandled();
                return;
            }
        }
    }

    /// <summary>Opens a page (closing the other one) or closes it if it's open.</summary>
    public void Toggle(ParchmentPanel? page)
    {
        if (page == null)
        {
            return;
        }
        if (page.IsOpen && page.Visible)
        {
            page.Close();
            return;
        }
        foreach (ParchmentPanel? other in new ParchmentPanel?[] { Inventory, Journal, Talk, RadioPage })
        {
            if (other != null && other != page && other.IsOpen && other.Visible)
            {
                other.SetOpenNow(false);
            }
        }
        if (page == Journal)
        {
            Journal!.OpenJournal();
        }
        else
        {
            page.Open();
        }
        GameEvents.PublishScreenOpen(true);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void OnPageClosed()
    {
        bool anyOpen = (Inventory != null && Inventory.IsOpen) || (Journal != null && Journal.IsOpen) || (Talk != null && Talk.IsOpen)
            || (RadioPage != null && RadioPage.IsOpen);
        if (anyOpen)
        {
            return;
        }
        GameEvents.PublishScreenOpen(false);
        if (!GameEvents.IsPacking && !GameEvents.IsMapOpen)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _Process(double delta)
    {
        if (_play == null)
        {
            return;
        }
        _play.Visible = !GameEvents.IsPacking && !Debug.DebugDraw3D.HideHud;
        if (ObjectivesStacked)
        {
            PlaceNotes(GetViewport().GetVisibleRect().Size);
        }
        if (Compass != null)
        {
            Compass.WaypointAngle = NearestWaypointAngle();
        }
    }

    /// <summary>Direction of the nearest waypoint (or, with none set, the next station) relative to the view, or NaN.</summary>
    private float NearestWaypointAngle()
    {
        if (MapPage?.Map == null)
        {
            return float.NaN;
        }
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera == null)
        {
            return float.NaN;
        }
        Vector3 here = GameEvents.CarrierPosition;
        Vector2 me = new(here.X, here.Z);
        Vector2 best = Vector2.Zero;
        float bestDistance = float.MaxValue;
        // No waypoints: the compass's marker points to the next station instead.
        IRouteQuery? route = MapPage.Map.Route;
        if (MapPage.Map.Chart.Waypoints.Count == 0 && route != null)
        {
            Vector3 station = route.Station(route.NextStationIndex(here.Z));
            best = new Vector2(station.X, station.Z);
            bestDistance = 0.0f;
        }
        foreach (Vector2 point in MapPage.Map.Chart.Waypoints)
        {
            float d = point.DistanceTo(me);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = point;
            }
        }
        Vector3 to = new(best.X - me.X, 0.0f, best.Y - me.Y);
        Vector3 forward = -camera.GlobalBasis.Z;
        forward.Y = 0.0f;
        if (to.IsZeroApprox() || forward.IsZeroApprox())
        {
            return float.NaN;
        }
        forward = forward.Normalized();
        Vector3 right = forward.Cross(Vector3.Up);
        to = to.Normalized();
        return Mathf.Atan2(to.Dot(right), to.Dot(forward));
    }
}
