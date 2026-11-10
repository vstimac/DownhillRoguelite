using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Ui;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Session-only developer UI. Reuses real scenes and survives their replacement, without an autoload.</summary>
public partial class DevPlaygroundConsole : CanvasLayer
{
    public Godot.Collections.Array<DevPlaygroundEntry> Labs { get; set; } = new();
    [Export] public ulong Seed { get; set; } = 1;
    [Export] public float RefreshSeconds { get; set; } = 0.25f;
    [Export] public float DamageAmount { get; set; } = 25;
    [Export] public float LaunchHeight { get; set; } = 4;
    [Export] public float TeleportClearance { get; set; } = 0.5f;
    [Export] public string LoadSavePath { get; set; } = "user://dev_playground_load.json";
    public bool ChangingLab { get; private set; }
    public bool MenuOpen => _paper?.Visible == true;
    public string ActiveLabId { get; private set; } = "";
    public PlayerController? Player { get; private set; }

    private Control _paper = null!;
    private Label _status = null!;
    private Label _description = null!;
    private Label _controls = null!;
    private Label _path = null!;
    private ItemList _list = null!;
    private OptionButton _items = null!;
    private OptionButton _places = null!;
    private Button _launch = null!;
    private readonly List<DevPlaygroundEntry> _filtered = new();
    private readonly List<ItemDefinition> _itemDefinitions = new();
    private readonly List<Vector3> _checkpoints = new();
    private CarrierInventory? _inventory;
    private WeatherSystem? _weather;
    private TestCourseBuilder? _course;
    private TerrainStreamer? _terrain;
    private BeastDirector? _beasts;
    private Vector3 _spawn;
    private float _spawnYaw;
    private float _refresh;
    private bool _wasPaused;
    private bool _wasScreenOpen;
    private bool _bringBeast;
    private Vector3? _waitingForGround;
    private PhysicsRayQueryParameters3D? _groundProbe;
    private Input.MouseModeEnum _wasMouse;
    private string _message = "Odaberi laboratorij ili nastavi na poligonu.";

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ProcessPhysicsPriority = -1000;
        Layer = 200;
        BuildUi();
        DebugDraw3D.SetEnabled(false);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F10)
        {
            if (!ChangingLab) { SetMenuOpen(!MenuOpen); }
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        _refresh -= (float)delta;
        if (_refresh > 0) { return; }
        _refresh = RefreshSeconds;
        string stats = "Ovaj laboratorij nema igraca.";
        if (Player != null && IsInstanceValid(Player))
        {
            stats = $"HP {Player.Health?.Current:0}/{Player.Health?.EffectiveMax:0}  |  Stamina {Player.Stamina?.Current:0}/{Player.Stamina?.EffectiveMax:0}  |  Teret {CarriedLoad.Mass:0.0} kg";
        }
        _status.Text = _message + "\n" + stats;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_waitingForGround is Vector3 target && Player != null && _terrain?.Generator != null && !GetTree().Paused)
        {
            // A registered chunk is not proof that its collision has reached the physics server.
            // Query the real floor before releasing the carrier over a freshly streamed area.
            _groundProbe!.From = target + Vector3.Up * 2;
            _groundProbe.To = target + Vector3.Down * 2;
            bool floorReady = false;
            // Pickups, station props and NPCs can already exist while the ground is still building.
            // They must not count as terrain beneath a teleported carrier.
            for (int attempt = 0; attempt < 16; attempt++)
            {
                var ground = Player.GetWorld3D().DirectSpaceState.IntersectRay(_groundProbe);
                if (ground.Count == 0) { break; }
                if (ground["collider"].AsGodotObject() is TerrainChunk) { floorReady = true; break; }
                if (ground["collider"].AsGodotObject() is CollisionObject3D obstacle)
                {
                    var exclude = _groundProbe.Exclude; exclude.Add(obstacle.GetRid()); _groundProbe.Exclude = exclude;
                }
                else { break; }
            }
            if (floorReady) { _waitingForGround = null; _groundProbe = null; }
            else { Player.Teleport(target + Vector3.Up * TeleportClearance, _spawnYaw); }
        }
        if (!_bringBeast || GetTree().Paused || _beasts == null || Player == null) { return; }
        _bringBeast = false;
        NightBeast? beast = _beasts.Bring(Player.GlobalPosition, Player.GetWorld3D().DirectSpaceState);
        _message = "Nema prikladnog mjesta za zvijer. Makni se od vrha i probaj opet.";
        if (beast != null) { _message = "Zvijer je stigla. Izadi iz sigurnosti vatre za stvarni lov."; }
    }

    public void SetMenuOpen(bool open)
    {
        if (MenuOpen == open) { return; }
        if (open)
        {
            _wasPaused = GetTree().Paused;
            _wasScreenOpen = GameEvents.IsScreenOpen;
            _wasMouse = Input.MouseMode;
            _paper.Visible = true;
            GetTree().Paused = true;
            GameEvents.PublishScreenOpen(true);
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else
        {
            _paper.Visible = false;
            GetTree().Paused = _wasPaused;
            GameEvents.PublishScreenOpen(_wasScreenOpen);
            Input.MouseMode = _wasMouse;
        }
    }

    /// <summary>Replaces only the active world; an empty transition lets old subscriptions leave before reset.</summary>
    public async Task<bool> OpenLab(string id, bool showMenu = false)
    {
        if (ChangingLab) { return false; }
        DevPlaygroundEntry? entry = null;
        foreach (DevPlaygroundEntry candidate in Labs) { if (candidate.Id == id) { entry = candidate; break; } }
        if (entry == null || !ResourceLoader.Exists(entry.ScenePath))
        {
            _message = "Laboratorij ili scena ne postoji: " + id;
            return false;
        }
        ChangingLab = true;
        _launch.Disabled = true;
        _message = "Ucitavam: " + entry.Title;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            SetMenuOpen(false);
            GetTree().Paused = false;
            Player = null; _inventory = null; _weather = null; _course = null; _terrain = null; _beasts = null;
            if (GetTree().ChangeSceneToFile("res://debug/playground/DevTransition.tscn") != Error.Ok) { return false; }
            await ToSignal(GetTree(), SceneTree.SignalName.SceneChanged);
            ResetSession();
            // World teardown leaves managed resource wrappers for the finalizer. Drain them while
            // there is no world reading/reloading those resources (a loading pause, never gameplay).
            GC.Collect();
            GC.WaitForPendingFinalizers();
            PackedScene? scene = ResourceLoader.Load<PackedScene>(entry.ScenePath);
            if (scene == null) { _message = "Scena se ne moze ucitati: " + entry.ScenePath; return false; }
            if (GetTree().ChangeSceneToPacked(scene) != Error.Ok) { return false; }
            await ToSignal(GetTree(), SceneTree.SignalName.SceneChanged);
            ActiveLabId = id;
            _bringBeast = false;
            _waitingForGround = null;
            _groundProbe = null;
            Player = Find<PlayerController>(GetTree().CurrentScene);
            _inventory = Find<CarrierInventory>(GetTree().CurrentScene);
            _weather = Find<WeatherSystem>(GetTree().CurrentScene);
            _course = Find<TestCourseBuilder>(GetTree().CurrentScene);
            _terrain = Find<TerrainStreamer>(GetTree().CurrentScene);
            _beasts = Find<BeastDirector>(GetTree().CurrentScene);
            if (Player != null) { _spawn = Player.GlobalPosition; _spawnYaw = Player.GlobalRotation.Y; }
            FillCheckpoints();
            _message = entry.Title + " | F10 izbornik | F9 snimanje svakih 5 frameova";
            // Only the entry point requests the developer menu. Starting a lab must enter gameplay.
            if (showMenu) { SetMenuOpen(true); }
            return true;
        }
        catch (Exception problem)
        {
            _message = "Ucitavanje nije uspjelo: " + problem.Message;
            GD.PushError("DevPlayground: " + problem);
            return false;
        }
        finally { ChangingLab = false; _launch.Disabled = _filtered.Count == 0; }
    }

    public Task<bool> LaunchSelectedLab()
    {
        if (ChangingLab || !_list.IsAnythingSelected()) { return Task.FromResult(false); }
        string id = _filtered[_list.GetSelectedItems()[0]].Id;
        if (id == ActiveLabId) { SetMenuOpen(false); return Task.FromResult(true); }
        return OpenLab(id);
    }

    private void ResetSession()
    {
        GameEvents.PublishScreenOpen(false);
        GameEvents.PublishMapOpen(false);
        GameEvents.PublishPagesFrozen(false);
        GameEvents.PublishPackingMode(false);
        GameEvents.PublishConversation(false);
        GameEvents.PublishSpeaking(false);
        GameEvents.PublishShelter(false, -1);
        GameEvents.PublishDowned(false);
        GameEvents.PublishLoad(LoadSnapshot.Empty);
        GameEvents.PublishGear(GearSnapshot.None);
        GameEvents.PublishWeather(WeatherSnapshot.Calm);
        GameEvents.PublishCarriedItems(Array.Empty<string>());
        GameEvents.PublishObjectives(Array.Empty<ObjectiveNote>(), Array.Empty<ObjectiveNote>());
        RunState run = GetNode<RunState>("/root/RunState");
        run.Route = null;
        run.StartNewRun(Seed);
        WorldClock clock = GetNode<WorldClock>("/root/WorldClock");
        clock.Paused = false;
        clock.DebugTimeScale = 1;
        Engine.TimeScale = 1;
        DebugDraw3D.SetEnabled(false);
        DebugDraw3D.HideHud = false;
        foreach (StringName action in InputMap.GetActions()) { Input.ActionRelease(action); }
    }

    public bool RefillPlayer()
    {
        if (Player == null || !IsInstanceValid(Player)) { _message = "Ovaj laboratorij nema igraca."; return false; }
        Player.GetNodeOrNull<CarrierNeeds>("Needs")?.Refill();
        Player.Health?.Refill();
        Player.Stamina?.Refill();
        _message = "Health, stamina, glad i zed su napunjeni.";
        return true;
    }

    public bool GrantItem(string path)
    {
        if (!ResourceLoader.Exists(path)) { _message = "Predmet ne postoji: " + path; return false; }
        bool granted = GameEvents.TryGrantItem(path);
        _message = "Nema prostora ili ova scena nema inventory.";
        if (granted) { _message = "Predmet je dodan u ruksak."; }
        return granted;
    }

    public bool ResetPlayer()
    {
        if (Player == null || !IsInstanceValid(Player)) { return false; }
        MovePlayer(_spawn);
        RefillPlayer();
        _message = "Lik je vracen na pocetak. Za potpuno novu probu ponovno pokreni laboratorij.";
        return true;
    }

    public bool MovePlayer(Vector3 point)
    {
        if (Player == null || !IsInstanceValid(Player) || !point.IsFinite()) { return false; }
        _waitingForGround = point;
        _groundProbe = new PhysicsRayQueryParameters3D { CollisionMask = 1, Exclude = new Godot.Collections.Array<Rid> { Player.GetRid() } };
        Player.Teleport(point + Vector3.Up * TeleportClearance, _spawnYaw);
        if (_terrain == null) { _waitingForGround = null; _groundProbe = null; }
        return true;
    }

    private void DropTestItem()
    {
        if (Player == null || _items.Selected < 0) { _message = "Potreban je igrac i odabran predmet."; return; }
        string path = _items.GetItemMetadata(_items.Selected).AsString();
        ItemDefinition? item = ResourceLoader.Load<ItemDefinition>(path);
        if (item == null) { return; }
        WorldItem pickup = new() { Definition = item, InstanceId = Guid.NewGuid().ToString("N") };
        GetTree().CurrentScene.AddChild(pickup);
        pickup.GlobalPosition = Player.GlobalPosition - Player.GlobalBasis.Z * 2 + Vector3.Up;
        _message = "Testni predmet je na tlu. Nastavi igrati i pokupi ga s E.";
    }

    private void FillCheckpoints()
    {
        _places.Clear(); _checkpoints.Clear();
        if (Player == null) { return; }
        AddCheckpoint("Pocetna pozicija", _spawn);
        if (_course != null)
        {
            for (int i = 0; i < _course.LaneAngles.Length; i++) { AddCheckpoint("Nagib " + _course.LaneAngles[i] + " stupnjeva", _course.LanePoint(i, 1)); }
            for (int i = 0; i < _course.StepHeights.Length; i++) { AddCheckpoint("Stepenice " + _course.StepHeights[i] + " m", _course.StairStart(i)); }
            AddCheckpoint("Poprecni nagib", _course.CrossSlopeStart());
            AddCheckpoint("Polje kamenja", _course.RockFieldStart());
            for (int s = 0; s < _course.DropSurfaces.Count; s++)
            {
                for (int h = 0; h < _course.DropHeights.Length; h++) { AddCheckpoint("Pad " + _course.DropHeights[h] + " m / " + _course.DropSurfaces[s].DisplayName, _course.DropStart(s, h)); }
            }
        }
        if (_terrain?.Generator != null)
        {
            AddCheckpoint("Vrh planine", _terrain.SpawnPoint);
            for (int i = 1; i <= 3; i++)
            {
                float z = _terrain.SpawnPoint.Z - 100 * i;
                float x = _terrain.Generator.SpineX(z);
                AddCheckpoint("Staza " + i * 100 + " m nizbrdo", new Vector3(x, _terrain.Generator.Height(x, z), z));
            }
            IRouteQuery? route = GetNode<RunState>("/root/RunState").Route;
            if (route != null) { for (int i = 1; i <= 3; i++) { AddCheckpoint("Stanica " + i + " / NPC i poslovi", route.Station(i) + Vector3.Back * 6); } }
        }
    }
    private void AddCheckpoint(string label, Vector3 point) { _places.AddItem(label); _checkpoints.Add(point); }

    private void TeleportSelected()
    {
        if (Player == null || _places.Selected < 0) { return; }
        MovePlayer(_checkpoints[_places.Selected]);
        RefillPlayer();
        _message = "Checkpoint: " + _places.GetItemText(_places.Selected);
        SetMenuOpen(false);
    }

    private static T? Find<T>(Node root) where T : Node
    {
        if (root is T match) { return match; }
        foreach (Node child in root.GetChildren()) { T? found = Find<T>(child); if (found != null) { return found; } }
        return null;
    }

    private void BuildUi()
    {
        Control root = new() { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiStyle.MakeTheme() };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(root);
        Button badge = new() { Text = "DEV  |  F10", Position = new Vector2(12, 8), CustomMinimumSize = new Vector2(145, 32) };
        badge.Pressed += () => { if (!ChangingLab) { SetMenuOpen(!MenuOpen); } }; root.AddChild(badge);
        ColorRect shade = new() { Color = new Color(0.02f, 0.025f, 0.03f, 0.82f), Visible = false };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); root.AddChild(shade); _paper = shade;
        MarginContainer margin = new(); margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) { margin.AddThemeConstantOverride("margin_" + edge, 24); }
        shade.AddChild(margin);
        PanelContainer panel = new();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = UiStyle.Paper, ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 14, ContentMarginBottom = 14 });
        margin.AddChild(panel);
        VBoxContainer column = new(); column.AddThemeConstantOverride("separation", 10); panel.AddChild(column);
        column.AddChild(UiStyle.MakeLabel("THE DESCENT / DEV PLAYGROUND", 28, true));
        column.AddChild(Wrap("Jedno mjesto za testiranje. F10 otvara/zatvara ovaj izbornik i pauzira probu. Promjena laboratorija resetira trenutnu probu.", 17));
        TabContainer tabs = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; column.AddChild(tabs);
        tabs.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = UiStyle.Paper.Lightened(0.06f), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 10, ContentMarginBottom = 10 });
        VBoxContainer labs = new() { Name = "Laboratoriji" }; tabs.AddChild(labs);
        LineEdit search = new() { PlaceholderText = "Trazi: odjeca, teren, NPC, teret..." }; labs.AddChild(search);
        HBoxContainer split = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; labs.AddChild(split);
        _list = new ItemList { CustomMinimumSize = new Vector2(270, 160), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        split.AddChild(_list);
        ScrollContainer details = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; split.AddChild(details);
        VBoxContainer detailRows = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; details.AddChild(detailRows);
        _description = Wrap("", 20); detailRows.AddChild(_description);
        _controls = Wrap("", 17); detailRows.AddChild(_controls);
        _path = Wrap("", 14); detailRows.AddChild(_path);
        _launch = AddButton(detailRows, "Pokreni odabrani laboratorij", () => { _ = LaunchSelectedLab(); });
        _list.ItemSelected += index => SelectLab((int)index);
        _list.ItemActivated += index => { _ = LaunchSelectedLab(); };
        search.TextChanged += FilterLabs;
        FilterLabs("");

        ScrollContainer tools = new() { Name = "Alati" }; tabs.AddChild(tools);
        VBoxContainer rows = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; rows.AddThemeConstantOverride("separation", 10); tools.AddChild(rows);
        rows.AddChild(Wrap("Alati rade nad aktivnim laboratorijem. Za likove/odjecu koristi i njihove radionice. Promjene su debug naredbe za ovu probu.", 18));
        HFlowContainer health = new(); rows.AddChild(health);
        AddButton(health, "Napuni sve statove", () => RefillPlayer());
        AddButton(health, "Vrati lika na pocetak", () => ResetPlayer());
        AddButton(health, "Stamina na 30%", () => { if (Player?.Stamina != null) { Player.Stamina.SetCurrent(Player.Stamina.EffectiveMax * 0.3f); } });
        AddButton(health, "Osteti lika", () => Player?.Health?.TakeDamage(DamageAmount, "dev playground"));
        AddButton(health, "Test odbacivanja / koluta", () => { if (Player != null) { GameEvents.RequestLaunch(Vector3.Zero, LaunchHeight, "dev playground"); SetMenuOpen(false); } });
        _places = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; rows.AddChild(_places);
        AddButton(rows, "Idi na checkpoint i napuni statove", TeleportSelected);
        rows.AddChild(Wrap("Predmeti: dodavanje postuje prostor ruksaka. Predmet na tlu je nova testna kopija za E pickup.", 17));
        _items = new OptionButton(); rows.AddChild(_items);
        string[] files = DirAccess.GetFilesAt("res://inventory/items"); Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            if (!file.EndsWith(".tres", StringComparison.Ordinal)) { continue; }
            string path = "res://inventory/items/" + file;
            if (ResourceLoader.Load(path) is ItemDefinition item)
            {
                // Keep the scripted resource's managed wrapper alive while this persistent UI uses
                // its catalog; later inventories must not race its finalizer when loading it again.
                _itemDefinitions.Add(item);
                _items.AddItem(item.DisplayName + " / " + item.ItemId); _items.SetItemMetadata(_items.ItemCount - 1, path);
            }
        }
        HFlowContainer inventory = new(); rows.AddChild(inventory);
        AddButton(inventory, "Dodaj u ruksak", () => { if (_items.Selected >= 0) { GrantItem(_items.GetItemMetadata(_items.Selected).AsString()); } });
        AddButton(inventory, "Stvori predmet na tlu", DropTestItem);
        AddButton(inventory, "Spremi testni teret", () => {
            if (_inventory != null && EquipmentPersistence.Save(_inventory, LoadSavePath, out string error)) { _message = "Teret spremljen: " + LoadSavePath; }
            else { _message = "Teret nije spremljen; potrebna je scena s inventoryjem."; }
        });
        AddButton(inventory, "Ucitaj testni teret", () => {
            if (_inventory != null && EquipmentPersistence.Load(_inventory, LoadSavePath, out string error)) { _message = "Testni teret ucitan."; }
            else { _message = "Nema spremljenog tereta ili ga ova scena ne moze ucitati."; }
        });
        HFlowContainer environment = new(); rows.AddChild(environment);
        AddButton(environment, "Dan (12 h)", () => GetNode<WorldClock>("/root/WorldClock").SetTimeOfDay(12));
        AddButton(environment, "Noc (23 h)", () => GetNode<WorldClock>("/root/WorldClock").SetTimeOfDay(23));
        AddButton(environment, "Promijeni vrijeme", () => { if (_weather != null) { _weather.CycleForced(); _message = "Vrijeme: " + _weather.Forced; } else { _message = "Ova scena nema WeatherSystem."; } });
        AddButton(environment, "Dovedi nocnu zvijer", () => {
            if (_beasts == null || Player == null) { _message = "Za lov otvori Sumu ili Cijelu igru."; return; }
            if (_beasts.Beasts.Count >= _beasts.MostBeasts()) { _message = "Zvijeri su vec u svijetu. Nastavi igrati izvan vatre."; return; }
            GetNode<WorldClock>("/root/WorldClock").SetTimeOfDay(23); _bringBeast = true; SetMenuOpen(false);
        });
        AddButton(environment, "Ukljuci/iskljuci debug (F3)", () => DebugDraw3D.SetEnabled(!DebugDraw3D.Enabled));
        AddButton(environment, "Ukljuci/iskljuci snimanje (F9)", () => {
            PlaytestCapture? capture = Find<PlaytestCapture>(GetTree().Root);
            if (capture != null) { capture.Enabled = !capture.Enabled; _message = "Snimanje svakih 5 frameova: " + capture.Enabled; }
        });
        _status = Wrap("", 17); column.AddChild(_status);
        column.AddChild(Wrap("Koristi lokalni profil igre; questovi i postavke mogu se spremati. Testni teret ima zaseban dev_playground_load.json.", 14));
        HBoxContainer footer = new(); column.AddChild(footer);
        AddButton(footer, "Nastavi igrati (F10)", () => SetMenuOpen(false));
        AddButton(footer, "Vrati glavni poligon", () => { _ = OpenLab("sandbox"); });
        AddButton(footer, "Ponovno pokreni ovaj lab", () => { _ = OpenLab(ActiveLabId); });
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--playground-tools") >= 0) { tabs.CurrentTab = 1; }
    }

    private void FilterLabs(string text)
    {
        _filtered.Clear(); _list.Clear();
        foreach (DevPlaygroundEntry entry in Labs)
        {
            if (text.Length > 0 && !(entry.Title + " " + entry.Category + " " + entry.Description).Contains(text, StringComparison.OrdinalIgnoreCase)) { continue; }
            _filtered.Add(entry); _list.AddItem(entry.Category + " / " + entry.Title);
            _list.SetItemTooltip(_list.ItemCount - 1, entry.Title);
        }
        _launch.Disabled = _filtered.Count == 0 || ChangingLab;
        if (_filtered.Count > 0) { _list.Select(0); SelectLab(0); }
        else { _description.Text = "Nema rezultata."; _controls.Text = ""; _path.Text = ""; }
    }
    private void SelectLab(int index)
    {
        if (index < 0 || index >= _filtered.Count) { return; }
        DevPlaygroundEntry entry = _filtered[index];
        _description.Text = entry.Title + "\n\n" + entry.Description;
        _controls.Text = "\nKontrole\n" + entry.Controls;
        _path.Text = "\nScena za otvaranje u editoru:\n" + entry.ScenePath;
    }
    private static Label Wrap(string text, int size)
    {
        Label label = UiStyle.MakeLabel(text, size);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }
    private static Button AddButton(Node parent, string text, Action action)
    {
        Button button = new() { Text = text, CustomMinimumSize = new Vector2(160, 34) };
        StyleBoxFlat normal = new() { BgColor = UiStyle.Paper.Lightened(0.14f), BorderColor = UiStyle.InkSoft,
            BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 4, ContentMarginBottom = 4 };
        button.AddThemeStyleboxOverride("normal", normal);
        StyleBoxFlat hover = (StyleBoxFlat)normal.Duplicate(); hover.BgColor = UiStyle.Highlight;
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.Pressed += action; parent.AddChild(button); return button;
    }
}
