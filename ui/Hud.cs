using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Map;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The journal UI (the director's Don't Starve-inspired design): builds and lays out every piece
/// and keeps them fed with real game data. During play: survival dials top left (health, hunger,
/// thirst, stamina from VitalsSnapshot), the descent note and compass top right, the equipment
/// sheet on the left, the hotbar along the bottom, the look-at tooltip on the right and paper notes
/// for events. Pages: inventory (I), map (M, through the field map), journal (J), conversation
/// with the companion (Enter); Esc closes the
/// open page. While a page is open the mouse belongs to it (GameEvents.IsScreenOpen). Re-lays out
/// on any window size; hidden while packing and in clean screenshot mode.
/// </summary>
public partial class Hud : CanvasLayer
{
    public const string Group = "hud";
    private static readonly StringName ActionInventory = "inventory_open";
    private static readonly StringName ActionJournal = "journal_open";
    private static readonly StringName ActionTalk = "talk_open";

    public StatDial? HealthDial { get; private set; }
    public StatDial? HungerDial { get; private set; }
    public StatDial? ThirstDial { get; private set; }
    public StatDial? StaminaDial { get; private set; }
    public DescentPanel? Descent { get; private set; }
    public CompassDial? Compass { get; private set; }
    public EquipmentPanel? Equipment { get; private set; }
    public Hotbar? Hotbar { get; private set; }
    public InteractionTooltip? Tooltip { get; private set; }
    public Notifications? Notes { get; private set; }
    public InventoryScreen? Inventory { get; private set; }
    public MapScreen? MapPage { get; private set; }
    public JournalScreen? Journal { get; private set; }
    public TalkScreen? Talk { get; private set; }
    public JournalLog? Log { get; private set; }

    private Control? _root;
    private Control? _play;

    public override void _Ready()
    {
        Layer = 20;
        Name = "Hud";
        AddToGroup(Group);
        AddChild(new ItemIcons());
        Log = new JournalLog();
        AddChild(Log);

        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiStyle.MakeTheme() };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        _play = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _play.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_play);

        HealthDial = Dial(InkIcon.Heart, UiStyle.Health);
        HungerDial = Dial(InkIcon.Stomach, UiStyle.Hunger);
        ThirstDial = Dial(InkIcon.Drop, UiStyle.Thirst);
        StaminaDial = Dial(InkIcon.Boot, UiStyle.Stamina);
        Descent = new DescentPanel { Size = new Vector2(350, 172) };
        _play.AddChild(Descent);
        Compass = new CompassDial { Size = new Vector2(132, 132) };
        _play.AddChild(Compass);
        Equipment = new EquipmentPanel { Size = new Vector2(184, 320) };
        _play.AddChild(Equipment);
        Hotbar = new Hotbar();
        _play.AddChild(Hotbar);
        Tooltip = new InteractionTooltip { Size = new Vector2(330, 170) };
        _play.AddChild(Tooltip);
        Notes = new Notifications { Size = new Vector2(330, 200) };
        _play.AddChild(Notes);

        Inventory = new InventoryScreen();
        _root.AddChild(Inventory);
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
        MapPage.Closed += OnPageClosed;

        GameEvents.VitalsUpdated += OnVitals;
        GetViewport().SizeChanged += Layout;
        Layout();
    }

    public override void _ExitTree()
    {
        GameEvents.VitalsUpdated -= OnVitals;
        if (GameEvents.IsScreenOpen)
        {
            GameEvents.PublishScreenOpen(false);
        }
    }

    private StatDial Dial(InkIcon icon, Color colour)
    {
        StatDial dial = new() { Icon = icon, RingColour = colour, Size = new Vector2(96, 116) };
        _play!.AddChild(dial);
        return dial;
    }

    private void OnVitals(VitalsSnapshot vitals)
    {
        HealthDial?.SetValue(vitals.Health, vitals.MaxHealth);
        HungerDial?.SetValue(vitals.Hunger, 100.0f);
        ThirstDial?.SetValue(vitals.Thirst, 100.0f);
        StaminaDial?.SetValue(vitals.Stamina, Mathf.Max(vitals.MaxStamina, 1.0f));
    }

    /// <summary>Places everything for the current window size.</summary>
    public void Layout()
    {
        LayoutFor(GetViewport().GetVisibleRect().Size);
    }

    /// <summary>Places everything for a screen of <paramref name="screen"/> pixels (also used by tests).</summary>
    public void LayoutFor(Vector2 screen)
    {
        StatDial?[] dials = { HealthDial, HungerDial, ThirstDial, StaminaDial };
        for (int i = 0; i < dials.Length; i++)
        {
            if (dials[i] != null)
            {
                dials[i]!.Position = new Vector2(18 + i * 104, 14);
            }
        }
        if (Compass != null)
        {
            Compass.Position = new Vector2(screen.X - 150, 10);
        }
        if (Descent != null)
        {
            Descent.Position = new Vector2(screen.X - 150 - 360, 16);
        }
        if (Notes != null)
        {
            Notes.Position = new Vector2(screen.X - 18 - Notes.Size.X, 200);
        }
        if (Equipment != null)
        {
            Equipment.Position = new Vector2(14, Mathf.Max(150, screen.Y * 0.5f - 160));
        }
        if (Hotbar != null)
        {
            Hotbar.Position = new Vector2((screen.X - Hotbar.Size.X) * 0.5f, screen.Y - Hotbar.Size.Y - 10);
        }
        Tooltip?.SetHome(new Vector2(screen.X - 350, screen.Y - 300));
        foreach (ParchmentPanel? page in new ParchmentPanel?[] { Inventory, MapPage, Journal, Talk })
        {
            if (page != null)
            {
                page.Position = (screen - page.Size) * 0.5f;
                page.PivotOffset = page.Size * 0.5f;
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (GameEvents.IsPacking)
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
        else if (@event.IsActionPressed(ActionTalk) && Talk != null && !(Talk.IsOpen && Talk.Visible))
        {
            // Enter opens the conversation; once open, Enter sends what's typed.
            Toggle(Talk);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Esc closes the open page before the pause menu sees it.
        if (!@event.IsActionPressed("pause"))
        {
            return;
        }
        foreach (ParchmentPanel? page in new ParchmentPanel?[] { Inventory, Journal, Talk, MapPage })
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
        foreach (ParchmentPanel? other in new ParchmentPanel?[] { Inventory, Journal, Talk })
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
        bool anyOpen = (Inventory != null && Inventory.IsOpen) || (Journal != null && Journal.IsOpen) || (Talk != null && Talk.IsOpen);
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
        if (Compass != null)
        {
            Compass.WaypointAngle = NearestWaypointAngle();
        }
    }

    /// <summary>Direction of the nearest waypoint relative to the view, or NaN.</summary>
    private float NearestWaypointAngle()
    {
        if (MapPage?.Map == null || MapPage.Map.Chart.Waypoints.Count == 0)
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
