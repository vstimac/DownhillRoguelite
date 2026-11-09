using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// The "Odmorište Konačnih Rezultata" and its president (docs/restsite-president.md). Sets up the
/// restsite beside every EveryStations-th station's fire from FirstStation on, ahead of the carrier
/// like the travellers, and is the president's host: his save, the three quests (records placed down
/// the trail, worker-automatons and bridge material, the 12 kg banner carried to a stand far below),
/// the favour-paid shop, the podium speech, the temporary mandate's last chance, the megaphone, the
/// "Historic victories" board and the secret scene. Talks to other systems through GameEvents,
/// RunState, the route and the WorldClock only; the dialogue graph is PresidentDialogue.
/// </summary>
public partial class PresidentDirector : Node3D, IPresidentHost
{
    [ExportGroup("Data")]
    [Export(PropertyHint.File, "*.csv")] public string ManifestPath { get; set; } = "res://narrative/president/data/npc_restsite_president.csv";
    [Export(PropertyHint.File, "*.json")] public string VoicePackPath { get; set; } = "res://narrative/president/data/npc_restsite_president_voice.json";
    [Export(PropertyHint.File, "*.csv")] public string TextPath { get; set; } = "res://narrative/president/data/player_text.csv";
    /// <summary>Which recorded take to play: "forest" or "outdoor" (`-- --president-voice=forest` overrides).</summary>
    [Export] public string VoiceVariant { get; set; } = VoicePackLine.Outdoor;
    /// <summary>Save to user:// (off in headless runs and tests, so they never touch the player's save).</summary>
    [Export] public bool PersistentSave { get; set; } = true;

    [ExportGroup("Where")]
    [Export] public int FirstStation { get; set; } = 1;
    [Export] public int EveryStations { get; set; } = 2;
    /// <summary>The carrier is "at" a restsite within this distance of its fire, in m.</summary>
    [Export] public float VisitRadius { get; set; } = 25.0f;
    [Export] public PackedScene? SetScene { get; set; }
    [Export] public PackedScene? NpcModel { get; set; }

    [ExportGroup("Carrier")]
    [Export] public float HeavyKg { get; set; } = 25.0f;
    [Export] public float LowHealthShare { get; set; } = 0.3f;
    [Export] public int LateCircle { get; set; } = 3;
    [Export] public int LateReturns { get; set; } = 6;

    [ExportGroup("Quest 1: records")]
    [Export(PropertyHint.File, "*.tres")] public string RecordPath { get; set; } = "res://inventory/items/president_record.tres";
    [Export(PropertyHint.File, "*.tres")] public string DamagedRecordPath { get; set; } = "res://inventory/items/president_record_damaged.tres";
    /// <summary>Where the records lie between this restsite and the next, as shares of the way.</summary>
    [Export] public float[] RecordSpots { get; set; } = { 0.3f, 0.55f, 0.8f };
    /// <summary>How far off the path they lie, in m (random in the range, either side).</summary>
    [Export] public Vector2 RecordSideways { get; set; } = new(3.0f, 7.0f);
    /// <summary>A record left this far behind (uphill) is lost to the wind; a damaged copy waits at the next restsite.</summary>
    [Export] public float LostBehind { get; set; } = 40.0f;
    [Export] public int ResultsFavor { get; set; } = 2;
    [Export(PropertyHint.File, "*.tres")] public string MandatePath { get; set; } = "res://inventory/items/item_temporary_mandate.tres";
    [Export] public float MandateShare { get; set; } = 0.25f;
    /// <summary>Deaths the mandate doesn't undo (it's for falls).</summary>
    [Export] public string[] MandateIgnores { get; set; } = { "hunger", "thirst", "cold", "the abyss", "gave up" };

    [ExportGroup("Quest 2: contractors")]
    [Export] public int WorkersNeeded { get; set; } = 2;
    [Export] public int MaterialNeeded { get; set; } = 4;
    [Export(PropertyHint.File, "*.tres")] public string MaterialPath { get; set; } = "res://inventory/items/bridge_material.tres";
    [Export] public int ContractorsFavor { get; set; } = 2;

    [ExportGroup("Quest 3: historic victory")]
    [Export(PropertyHint.File, "*.tres")] public string BannerPath { get; set; } = "res://inventory/items/historic_banner.tres";
    [Export] public float VictoryDrop { get; set; } = 400.0f;
    /// <summary>A knock of at least this much damage tears the banner when it rides on the pack's outside (exposure at least TearExposure).</summary>
    [Export] public float TearDamage { get; set; } = 6.0f;
    [Export] public float TearExposure { get; set; } = 0.5f;
    [Export] public int VictoryFavor { get; set; } = 3;
    [Export] public int TornFavor { get; set; } = 1;
    [Export(PropertyHint.File, "*.tres")] public string MegaphonePath { get; set; } = "res://inventory/items/megaphone.tres";
    [Export] public float MegaphoneRadius { get; set; } = 25.0f;
    [Export] public float MegaphoneConfuse { get; set; } = 4.0f;
    [Export] public float MegaphoneNotice { get; set; } = 10.0f;
    [Export] public float MegaphoneNoticeLevel { get; set; } = 1.3f;
    [Export] public float MegaphoneCooldown { get; set; } = 30.0f;

    [ExportGroup("Shop and podium")]
    [Export] public string[] ShopItemPaths { get; set; } = { "res://inventory/items/seal_of_trust.tres", "res://inventory/items/strategic_sandwich.tres", "res://inventory/items/optimism_map.tres" };
    [Export] public string[] ShopTextIds { get; set; } = { "c_shop_seal", "c_shop_sandwich", "c_shop_map" };
    [Export] public int[] ShopCosts { get; set; } = { 1, 1, 2 };
    [Export] public float PodiumSeconds { get; set; } = 60.0f;
    [Export] public float PodiumStamina { get; set; } = 1.1f;
    [Export] public float PodiumDetect { get; set; } = 1.1f;
    /// <summary>The optimism map rounds the distance to the next station by this share.</summary>
    [Export] public float OptimismShare { get; set; } = 0.5f;

    public const string BannerCargoId = "q3_banner";

    public PresidentSave Save { get; private set; } = new(false);
    public TextTable Text { get; private set; } = new();
    public VoiceManifest Manifest { get; private set; } = new();
    public VoicePack Pack { get; private set; } = new();
    public PresidentDialogue? Dialogue { get; private set; }
    public IReadOnlyDictionary<int, RestsiteSet> Sets => _sets;
    public IReadOnlyDictionary<int, PresidentNpc> Npcs => _npcs;
    /// <summary>The restsite station the carrier is at, or 0.</summary>
    public int Visiting { get; private set; }
    public int WorkersSent => _workersSent;
    public UsableSpot? Stand => _stand;
    public IReadOnlyList<WorldItem> Records => _records;

    private readonly Dictionary<int, RestsiteSet> _sets = new();
    private readonly Dictionary<int, PresidentNpc> _npcs = new();
    private readonly List<WorldItem> _records = new();
    private readonly List<Node3D> _placed = new();
    private readonly HashSet<int> _returnGreeted = new();
    private readonly HashSet<int> _reminded = new();
    private readonly HashSet<int> _counted = new();
    private readonly RandomNumberGenerator _random = new();
    private RunState? _run;
    private WorldClock? _clock;
    private bool _seatFirst = true;
    private bool _deathGreeted;
    private bool _recordsPlaced;
    private bool _contractorsPlaced;
    private int _contractorsFrom;
    private int _lostRecords;
    private bool _resultsDamaged;
    private int _workersSent;
    private UsableSpot? _stand;
    private bool _granted;
    private bool _handedOver;
    private bool _podiumUsed;
    private float _podiumLeft;
    private float _megaphoneCooldown;
    private float _confuseLeft;
    private float _noticeLeft;
    private VitalsSnapshot _vitals;
    private string _notes = "";

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        _random.Randomize();
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("--president-voice=", System.StringComparison.Ordinal))
            {
                VoiceVariant = argument["--president-voice=".Length..];
            }
        }
        Manifest = VoiceManifest.Load(ManifestPath);
        Pack = VoicePack.Load(VoicePackPath);
        Text = TextTable.Load(TextPath);
        Save = new PresidentSave(PersistentSave && DisplayServer.GetName() != "headless");
        Dialogue = new PresidentDialogue(this, Manifest);
        GD.Print($"Restsite president: {Manifest.Count} lines, {Pack.Count} recorded ({VoiceVariant}), {Text.Count} texts.");
        GameEvents.StationReached += OnStation;
        GameEvents.RespawnRequested += OnNewRun;
        GameEvents.CarrierDied += OnDied;
        GameEvents.VitalsUpdated += OnVitals;
        GameEvents.Damaged += OnDamaged;
        GameEvents.CargoGranted += OnCargoGranted;
        GameEvents.CargoHandedOver += OnCargoHandedOver;
        GameEvents.UseItemRequested += OnUseItem;
        GameEvents.LastChanceRequested += OnLastChance;
        CallDeferred(MethodName.GrantUnlocks);
    }

    public override void _ExitTree()
    {
        GameEvents.StationReached -= OnStation;
        GameEvents.RespawnRequested -= OnNewRun;
        GameEvents.CarrierDied -= OnDied;
        GameEvents.VitalsUpdated -= OnVitals;
        GameEvents.Damaged -= OnDamaged;
        GameEvents.CargoGranted -= OnCargoGranted;
        GameEvents.CargoHandedOver -= OnCargoHandedOver;
        GameEvents.UseItemRequested -= OnUseItem;
        GameEvents.LastChanceRequested -= OnLastChance;
        EndPodiumBuff(true);
    }

    /// <summary>Uses a fresh save in memory (tests).</summary>
    public void UseSave(PresidentSave save)
    {
        Save = save;
    }

    public bool IsRestsite(int station) => station >= FirstStation && (station - FirstStation) % Mathf.Max(EveryStations, 1) == 0;

    /// <summary>The first restsite station at or below this one.</summary>
    public int RestsiteFrom(int station)
    {
        int every = Mathf.Max(EveryStations, 1);
        if (station <= FirstStation)
        {
            return FirstStation;
        }
        int steps = (station - FirstStation + every - 1) / every;
        return FirstStation + steps * every;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        IRouteQuery? route = _run?.Route;
        if (_seatFirst && route != null)
        {
            _seatFirst = false;
            Seat(FirstStation);
        }
        Vector3 carrier = GameEvents.CarrierPosition;
        Visiting = 0;
        foreach (RestsiteSet set in _sets.Values)
        {
            if (set.GlobalPosition.DistanceTo(carrier) < VisitRadius)
            {
                Visiting = set.Station;
            }
        }
        TrackRecords(carrier);
        TrackContractors();
        Timers(dt);
        PublishNotes(carrier);
    }

    // Restsites.

    /// <summary>Builds the restsite at a station (once per run).</summary>
    public RestsiteSet? Seat(int station)
    {
        IRouteQuery? route = _run?.Route;
        if (route == null || !IsRestsite(station) || _sets.ContainsKey(station) || Dialogue == null)
        {
            return null;
        }
        RestsiteSet set;
        if (SetScene?.Instantiate() is RestsiteSet scene)
        {
            set = scene;
        }
        else
        {
            set = new RestsiteSet();
        }
        set.Build(station, Text);
        AddChild(set);
        set.GlobalPosition = route.Station(station);
        set.SetVictories(Save.Victories);
        set.SetBridgeBuilt(Save.BridgeUnlocked);
        PresidentNpc npc = new() { Model = NpcModel };
        npc.Setup(Manifest, Pack, VoiceVariant, Text, Dialogue);
        set.AddChild(npc);
        npc.Position = set.PresidentOffset;
        if (set.Podium != null)
        {
            set.Podium.Prompt = "E  " + Text.Get("podium_name");
            set.Podium.Use = () => UsePodium(npc);
        }
        _sets[station] = set;
        _npcs[station] = npc;
        return set;
    }

    private bool UsePodium(PresidentNpc npc)
    {
        if (npc.Talking || Dialogue == null)
        {
            return false;
        }
        DialogueBeat? beat = Dialogue.Podium();
        if (beat == null)
        {
            GameEvents.PublishHint(Text.Get("podium_used"));
            return true;
        }
        npc.Run(beat);
        return true;
    }

    private void OnStation(int station)
    {
        // A checkpoint passed: one more historic victory.
        AddVictory();
        Seat(RestsiteFrom(station + 1));
        if (!IsRestsite(station))
        {
            return;
        }
        Seat(station);
        if (Save.Met && _counted.Add(station))
        {
            Save.Returns++;
            Save.Save();
        }
        RestsiteSet set = _sets[station];
        for (int i = 0; i < _lostRecords; i++)
        {
            Drop(DamagedRecordPath, set.GlobalPosition + set.BoardOffset + new Vector3(0.8f + 0.5f * i, 0.0f, 1.0f));
        }
        _lostRecords = 0;
        if (Save.Quest(PresidentSave.QuestResults) == QuestStatus.Active && !_recordsPlaced)
        {
            PlaceRecords(station);
        }
        if (Save.Quest(PresidentSave.QuestContractors) == QuestStatus.Active && !_contractorsPlaced)
        {
            PlaceContractors(station);
        }
    }

    private void AddVictory()
    {
        Save.Victories++;
        Save.Save();
        foreach (RestsiteSet set in _sets.Values)
        {
            set.SetVictories(Save.Victories);
        }
    }

    private void OnDied(string cause)
    {
        Save.LastRunDied = true;
        AddVictory();
    }

    private void OnNewRun()
    {
        foreach (RestsiteSet set in _sets.Values)
        {
            set.QueueFree();
        }
        _sets.Clear();
        _npcs.Clear();
        foreach (Node3D node in _placed)
        {
            if (IsInstanceValid(node))
            {
                node.QueueFree();
            }
        }
        _placed.Clear();
        _records.Clear();
        _stand = null;
        _returnGreeted.Clear();
        _reminded.Clear();
        _counted.Clear();
        _seatFirst = true;
        _deathGreeted = false;
        _recordsPlaced = false;
        _contractorsPlaced = false;
        _lostRecords = 0;
        _workersSent = 0;
        _podiumUsed = false;
        EndPodiumBuff(false);
        // What was carried is gone with the run: gathered quests are gathered again; the banner is lost.
        if (Save.Quest(PresidentSave.QuestResults) == QuestStatus.Ready)
        {
            Save.SetQuest(PresidentSave.QuestResults, QuestStatus.Active);
        }
        if (Save.Quest(PresidentSave.QuestContractors) == QuestStatus.Ready)
        {
            Save.SetQuest(PresidentSave.QuestContractors, QuestStatus.Active);
        }
        if (Save.Quest(PresidentSave.QuestVictory) == QuestStatus.Active)
        {
            Save.SetQuest(PresidentSave.QuestVictory, QuestStatus.NotStarted);
            Save.VictoryTorn = false;
        }
        Save.Save();
        _notes = "";
        CallDeferred(MethodName.GrantUnlocks);
    }

    /// <summary>What the president's quests unlocked comes with every run (after the inventory has been reset).</summary>
    private void GrantUnlocks()
    {
        if (Save.MandateUnlocked && !Carried(MandatePath.GetFile().GetBaseName()))
        {
            GameEvents.TryGrantItem(MandatePath);
        }
        if (Save.MegaphoneUnlocked && !Carried(MegaphonePath.GetFile().GetBaseName()))
        {
            GameEvents.TryGrantItem(MegaphonePath);
        }
    }

    // The carrier.

    private void OnVitals(VitalsSnapshot vitals) => _vitals = vitals;

    public bool LowHealth => _vitals.MaxHealth > 0.0f && !_vitals.Dead && _vitals.Health / _vitals.MaxHealth < LowHealthShare;
    public bool HeavyLoad => GameEvents.CurrentLoad.Mass > HeavyKg;
    public bool Late => (_run?.Circle ?? 0) >= LateCircle || Save.Returns >= LateReturns;
    public bool DeathGreetingPending => Save.LastRunDied && !_deathGreeted && Save.Met;

    public void MarkDeathGreeted()
    {
        _deathGreeted = true;
        Save.LastRunDied = false;
        Save.Save();
    }

    public bool ReturnGreetingPending => Save.Met && Save.Returns > 0 && Visiting > 0 && !_returnGreeted.Contains(Visiting);

    public void MarkReturnGreeted() => _returnGreeted.Add(Visiting);

    public bool ContractorsReminderPending => Save.Quest(PresidentSave.QuestContractors) == QuestStatus.Active
        && Visiting > 0 && Visiting != _contractorsFrom && !_reminded.Contains(Visiting);

    public void MarkContractorsReminded() => _reminded.Add(Visiting);

    public void MarkMet()
    {
        if (Save.Met)
        {
            return;
        }
        Save.Met = true;
        Save.Save();
    }

    public void Rest()
    {
        if (_clock != null && _clock.CanSleepNow && _clock.SleepUntilMorning() > 0.0f)
        {
            GameEvents.PublishHint(Text.Get("hint_rest_sleep"));
            return;
        }
        GameEvents.PublishHint(Text.Get("hint_rest_early"));
    }

    // Quest 1: three records down the trail.

    public void AcceptResults()
    {
        if (Save.Quest(PresidentSave.QuestResults) != QuestStatus.NotStarted)
        {
            return;
        }
        Save.SetQuest(PresidentSave.QuestResults, QuestStatus.Active);
        Save.Save();
        PlaceRecords(CurrentStation());
    }

    /// <summary>Lays the three records along the way from this restsite to the next.</summary>
    public void PlaceRecords(int station)
    {
        IRouteQuery? route = _run?.Route;
        if (route == null || station < 1)
        {
            return;
        }
        _recordsPlaced = true;
        float from = route.Station(station).Z;
        float to = route.Station(station + Mathf.Max(EveryStations, 1)).Z;
        foreach (float share in RecordSpots)
        {
            float z = Mathf.Lerp(from, to, share);
            Vector3 point = route.RoutePoint(z);
            float side = _random.RandfRange(RecordSideways.X, RecordSideways.Y);
            if (_random.Randf() < 0.5f)
            {
                side = -side;
            }
            WorldItem? record = Drop(RecordPath, point + new Vector3(side, 0.0f, 0.0f));
            if (record != null)
            {
                _records.Add(record);
            }
        }
    }

    private WorldItem? Drop(string path, Vector3 at)
    {
        if (ResourceLoader.Load(path) is not ItemDefinition definition)
        {
            return null;
        }
        IRouteQuery? route = _run?.Route;
        float y = at.Y;
        if (route != null)
        {
            y = route.GroundHeight(at.X, at.Z);
        }
        WorldItem item = new() { Definition = definition, Name = "President_" + definition.ItemId };
        AddChild(item);
        item.GlobalPosition = new Vector3(at.X, y + 0.1f, at.Z);
        _placed.Add(item);
        return item;
    }

    public int RecordsCarried => Count(RecordPath) + Count(DamagedRecordPath);

    private void TrackRecords(Vector3 carrier)
    {
        QuestStatus status = Save.Quest(PresidentSave.QuestResults);
        if (status != QuestStatus.Active && status != QuestStatus.Ready)
        {
            return;
        }
        for (int i = _records.Count - 1; i >= 0; i--)
        {
            WorldItem record = _records[i];
            if (!IsInstanceValid(record) || !record.IsInsideTree())
            {
                _records.RemoveAt(i);
                continue;
            }
            if (carrier.Z < record.GlobalPosition.Z - LostBehind)
            {
                record.QueueFree();
                _records.RemoveAt(i);
                _lostRecords++;
                GameEvents.PublishHint(Text.Get("hint_document_lost"));
            }
        }
        int carried = RecordsCarried;
        int needed = RecordSpots.Length;
        if (status == QuestStatus.Active && carried >= needed)
        {
            Save.SetQuest(PresidentSave.QuestResults, QuestStatus.Ready);
            Save.Save();
        }
        else if (status == QuestStatus.Ready && carried < needed)
        {
            Save.SetQuest(PresidentSave.QuestResults, QuestStatus.Active);
            Save.Save();
        }
    }

    public void HandInResults()
    {
        _resultsDamaged = Count(DamagedRecordPath) > 0;
        int left = RecordSpots.Length;
        left -= Use(DamagedRecordPath, left);
        Use(RecordPath, left);
    }

    public void FinishResults(string ending)
    {
        if (Save.Quest(PresidentSave.QuestResults) == QuestStatus.Completed)
        {
            return;
        }
        Save.SetQuest(PresidentSave.QuestResults, QuestStatus.Completed);
        Save.ResultsEnding = ending;
        int favor = ResultsFavor;
        if (_resultsDamaged)
        {
            favor = Mathf.Max(1, favor - 1);
        }
        Save.Favor += favor;
        Save.MandateUnlocked = true;
        Save.Save();
        PublishDone(PresidentSave.QuestResults, "quest_results_title");
        GameEvents.TryGrantItem(MandatePath);
    }

    private float OnLastChance(string cause)
    {
        if (!Save.MandateUnlocked || System.Array.IndexOf(MandateIgnores, cause) >= 0)
        {
            return 0.0f;
        }
        string mandate = MandatePath.GetFile().GetBaseName();
        if (!Carried(mandate))
        {
            return 0.0f;
        }
        GameEvents.RequestItemUse(mandate);
        GameEvents.PublishHint(Text.Get("hint_mandate"));
        return MandateShare;
    }

    // Quest 2: worker-automatons or bridge material.

    public void AcceptContractors()
    {
        if (Save.Quest(PresidentSave.QuestContractors) != QuestStatus.NotStarted)
        {
            return;
        }
        Save.SetQuest(PresidentSave.QuestContractors, QuestStatus.Active);
        Save.Save();
        PlaceContractors(CurrentStation());
    }

    /// <summary>Two worker-automatons and four units of material between this restsite and the next (placeholders until art exists).</summary>
    public void PlaceContractors(int station)
    {
        IRouteQuery? route = _run?.Route;
        if (route == null || station < 1)
        {
            return;
        }
        _contractorsPlaced = true;
        _contractorsFrom = station;
        float from = route.Station(station).Z;
        float to = route.Station(station + Mathf.Max(EveryStations, 1)).Z;
        for (int i = 0; i < WorkersNeeded; i++)
        {
            float z = Mathf.Lerp(from, to, 0.25f + 0.5f * i / Mathf.Max(WorkersNeeded - 1, 1));
            UsableSpot worker = Worker();
            AddChild(worker);
            Vector3 point = route.RoutePoint(z) + new Vector3(4.0f - 8.0f * (i % 2), 0.0f, 0.0f);
            worker.GlobalPosition = new Vector3(point.X, route.GroundHeight(point.X, point.Z), point.Z);
            _placed.Add(worker);
        }
        for (int i = 0; i < MaterialNeeded; i++)
        {
            float z = Mathf.Lerp(from, to, 0.15f + 0.7f * i / Mathf.Max(MaterialNeeded - 1, 1));
            Drop(MaterialPath, route.RoutePoint(z) + new Vector3(-3.0f + 6.0f * (i % 2), 0.0f, 0.0f));
        }
    }

    /// <summary>A worker-automaton (TODO art: a box robot until a model exists). E sends it to the bridge.</summary>
    private UsableSpot Worker()
    {
        UsableSpot worker = new()
        {
            Name = "WorkerAutomaton",
            Title = Text.Get("worker_name"),
            Description = Text.Get("worker_description"),
            Prompt = Text.Get("worker_prompt"),
        };
        StandardMaterial3D metal = new() { AlbedoColor = new Color(0.55f, 0.55f, 0.5f), Metallic = 0.6f, Roughness = 0.4f };
        worker.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.8f, 0.35f) }, Position = new Vector3(0.0f, 0.75f, 0.0f), MaterialOverride = metal });
        worker.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.3f, 0.3f) }, Position = new Vector3(0.0f, 1.35f, 0.0f), MaterialOverride = metal });
        worker.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.35f, 0.12f) }, Position = new Vector3(-0.15f, 0.18f, 0.0f), MaterialOverride = metal });
        worker.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.35f, 0.12f) }, Position = new Vector3(0.15f, 0.18f, 0.0f), MaterialOverride = metal });
        worker.Use = () =>
        {
            if (Save.Quest(PresidentSave.QuestContractors) != QuestStatus.Active || _workersSent >= WorkersNeeded)
            {
                return false;
            }
            _workersSent++;
            GameEvents.PublishHint(Text.Format("worker_sent", "count", _workersSent, "total", WorkersNeeded));
            worker.QueueFree();
            return true;
        };
        return worker;
    }

    public int MaterialCarried => Count(MaterialPath);

    private void TrackContractors()
    {
        QuestStatus status = Save.Quest(PresidentSave.QuestContractors);
        bool ready = _workersSent >= WorkersNeeded || MaterialCarried >= MaterialNeeded;
        if (status == QuestStatus.Active && ready)
        {
            Save.SetQuest(PresidentSave.QuestContractors, QuestStatus.Ready);
            Save.Save();
        }
        else if (status == QuestStatus.Ready && !ready)
        {
            Save.SetQuest(PresidentSave.QuestContractors, QuestStatus.Active);
            Save.Save();
        }
    }

    public void FinishContractors()
    {
        if (Save.Quest(PresidentSave.QuestContractors) == QuestStatus.Completed)
        {
            return;
        }
        if (_workersSent < WorkersNeeded)
        {
            Use(MaterialPath, MaterialNeeded);
        }
        Save.SetQuest(PresidentSave.QuestContractors, QuestStatus.Completed);
        Save.Favor += ContractorsFavor;
        Save.BridgeUnlocked = true;
        Save.Save();
        PublishDone(PresidentSave.QuestContractors, "quest_contractors_title");
        foreach (RestsiteSet set in _sets.Values)
        {
            set.SetBridgeBuilt(true);
        }
    }

    // Quest 3: the banner, 400 m down.

    public bool AcceptVictory()
    {
        if (Save.Quest(PresidentSave.QuestVictory) != QuestStatus.NotStarted)
        {
            return false;
        }
        _granted = false;
        GameEvents.RequestCargoGrant(BannerPath, BannerCargoId);
        if (!_granted)
        {
            GameEvents.PublishHint(Text.Get("hint_banner_no_room"));
            return false;
        }
        Save.SetQuest(PresidentSave.QuestVictory, QuestStatus.Active);
        Save.VictoryTorn = false;
        Save.Save();
        PlaceStand(GameEvents.CarrierPosition);
        return true;
    }

    private void OnCargoGranted(string cargoId, bool packed)
    {
        if (cargoId == BannerCargoId)
        {
            _granted = packed;
        }
    }

    private void OnCargoHandedOver(string cargoId, bool removed)
    {
        if (cargoId == BannerCargoId)
        {
            _handedOver = removed;
        }
    }

    /// <summary>The stand for the banner, on the path VictoryDrop metres lower than here.</summary>
    public void PlaceStand(Vector3 from)
    {
        IRouteQuery? route = _run?.Route;
        if (route == null)
        {
            return;
        }
        float z = from.Z;
        Vector3 point = route.RoutePoint(z);
        for (int step = 0; step < 2000 && point.Y > from.Y - VictoryDrop; step++)
        {
            z -= 5.0f;
            point = route.RoutePoint(z);
        }
        _stand = new UsableSpot
        {
            Name = "VictoryStand",
            Title = Text.Get("stand_name"),
            Description = Text.Get("stand_description"),
            Prompt = Text.Get("stand_prompt"),
        };
        StandardMaterial3D wood = new() { AlbedoColor = new Color(0.45f, 0.3f, 0.18f) };
        _stand.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.0f, 0.3f, 1.2f) }, Position = new Vector3(0.0f, 0.15f, 0.0f), MaterialOverride = wood });
        _stand.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 3.0f }, Position = new Vector3(0.0f, 1.8f, 0.0f), MaterialOverride = wood });
        _stand.Use = PlantBanner;
        AddChild(_stand);
        Vector3 at = point + new Vector3(4.0f, 0.0f, 0.0f);
        _stand.GlobalPosition = new Vector3(at.X, route.GroundHeight(at.X, at.Z), at.Z);
        _placed.Add(_stand);
    }

    private bool PlantBanner()
    {
        if (Save.Quest(PresidentSave.QuestVictory) != QuestStatus.Active || _stand == null)
        {
            return false;
        }
        _handedOver = false;
        GameEvents.RequestCargoHandOver(BannerCargoId);
        if (!_handedOver)
        {
            return false;
        }
        Color cloth = new(0.75f, 0.12f, 0.15f);
        if (Save.VictoryTorn)
        {
            cloth = cloth.Darkened(0.4f);
        }
        _stand.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.4f, 0.8f, 0.02f) }, Position = new Vector3(0.72f, 2.8f, 0.0f), MaterialOverride = new StandardMaterial3D { AlbedoColor = cloth } });
        Save.SetQuest(PresidentSave.QuestVictory, QuestStatus.Ready);
        Save.Save();
        return true;
    }

    private void OnDamaged(float amount)
    {
        if (Save.Quest(PresidentSave.QuestVictory) != QuestStatus.Active || Save.VictoryTorn || amount < TearDamage)
        {
            return;
        }
        if (GameEvents.CargoExposure.TryGetValue(BannerCargoId, out float exposure) && exposure >= TearExposure)
        {
            Save.VictoryTorn = true;
            Save.Save();
            GameEvents.PublishHint(Text.Get("hint_banner_torn"));
        }
    }

    public bool VictoryTorn => Save.VictoryTorn;

    public void FinishVictory()
    {
        if (Save.Quest(PresidentSave.QuestVictory) == QuestStatus.Completed)
        {
            return;
        }
        Save.SetQuest(PresidentSave.QuestVictory, QuestStatus.Completed);
        if (Save.VictoryTorn)
        {
            Save.Favor += TornFavor;
        }
        else
        {
            Save.Favor += VictoryFavor;
        }
        Save.MegaphoneUnlocked = true;
        Save.Save();
        PublishDone(PresidentSave.QuestVictory, "quest_victory_title");
        GameEvents.TryGrantItem(MegaphonePath);
    }

    // The shop: paid in favour.

    public int ShopCount => Mathf.Min(ShopItemPaths.Length, Mathf.Min(ShopTextIds.Length, ShopCosts.Length));

    public string ShopTextId(int index) => ShopTextIds[index];

    public int ShopCost(int index) => ShopCosts[index];

    public bool Buy(int index)
    {
        if (index < 0 || index >= ShopCount || Save.Favor < ShopCosts[index])
        {
            return false;
        }
        if (!GameEvents.TryGrantItem(ShopItemPaths[index]))
        {
            GameEvents.PublishHint(Text.Get("hint_no_room"));
            return false;
        }
        Save.Favor -= ShopCosts[index];
        Save.Save();
        string name = ShopItemPaths[index].GetFile().GetBaseName();
        if (ResourceLoader.Load(ShopItemPaths[index]) is ItemDefinition item)
        {
            name = item.DisplayName;
        }
        GameEvents.PublishHint(Text.Format("hint_bought", "item", name));
        return true;
    }

    // The podium.

    public bool PodiumUsed => _podiumUsed;

    public void Podium(bool speak)
    {
        if (_podiumUsed)
        {
            return;
        }
        _podiumUsed = true;
        if (!speak || _run == null)
        {
            return;
        }
        _run.Modifiers.StaminaRecovery *= PodiumStamina;
        _podiumLeft = PodiumSeconds;
        GameEvents.PublishHint(Text.Get("hint_podium_buff"));
    }

    private void EndPodiumBuff(bool restore)
    {
        if (_podiumLeft > 0.0f && restore && _run != null)
        {
            _run.Modifiers.StaminaRecovery /= PodiumStamina;
        }
        _podiumLeft = 0.0f;
    }

    // The secret scene.

    public void FinishSecret()
    {
        if (Save.SecretSeen)
        {
            return;
        }
        Save.SecretSeen = true;
        Save.BadgeEarned = true;
        Save.Save();
        GameEvents.PublishHint(Text.Get("hint_badge"));
    }

    // Items used from the hotbar.

    private void OnUseItem(string itemId)
    {
        if (itemId == MegaphonePath.GetFile().GetBaseName())
        {
            if (_megaphoneCooldown > 0.0f)
            {
                GameEvents.PublishHint(Text.Get("hint_megaphone_wait"));
                return;
            }
            _megaphoneCooldown = MegaphoneCooldown;
            _confuseLeft = MegaphoneConfuse;
            _noticeLeft = MegaphoneNotice;
            GameEvents.PublishMegaphone(GameEvents.CarrierPosition, MegaphoneRadius, MegaphoneConfuse, MegaphoneNotice);
            GameEvents.PublishHint(Text.Get("hint_megaphone"));
        }
        else if (itemId == "optimism_map")
        {
            IRouteQuery? route = _run?.Route;
            if (route == null)
            {
                return;
            }
            Vector3 carrier = GameEvents.CarrierPosition;
            Vector3 station = route.Station(route.NextStationIndex(carrier.Z));
            float real = new Vector2(station.X - carrier.X, station.Z - carrier.Z).Length();
            GameEvents.PublishHint(Text.Format("hint_map", "short", Mathf.RoundToInt(real * OptimismShare), "real", Mathf.RoundToInt(real)));
        }
    }

    private void Timers(float dt)
    {
        _megaphoneCooldown = Mathf.Max(0.0f, _megaphoneCooldown - dt);
        float detect = 1.0f;
        if (_podiumLeft > 0.0f)
        {
            _podiumLeft -= dt;
            detect *= PodiumDetect;
            if (_podiumLeft <= 0.0f)
            {
                _podiumLeft = 0.001f;
                EndPodiumBuff(true);
            }
        }
        if (_confuseLeft > 0.0f)
        {
            _confuseLeft -= dt;
        }
        else if (_noticeLeft > 0.0f)
        {
            _noticeLeft -= dt;
            detect *= MegaphoneNoticeLevel;
        }
        if (!Mathf.IsEqualApprox(GameEvents.Detectability, detect))
        {
            GameEvents.PublishDetectability(detect);
        }
    }

    // The objectives panel.

    private void PublishNotes(Vector3 carrier)
    {
        string giver = Text.Get("npc_name");
        List<ObjectiveNote> notes = new();
        QuestStatus results = Save.Quest(PresidentSave.QuestResults);
        if (results == QuestStatus.Active || results == QuestStatus.Ready)
        {
            notes.Add(Note(PresidentSave.QuestResults, "quest_results_title", Text.Get("quest_results_why"),
                Text.Format("quest_results_progress", "count", RecordsCarried, "total", RecordSpots.Length), results, giver));
        }
        QuestStatus contractors = Save.Quest(PresidentSave.QuestContractors);
        if (contractors == QuestStatus.Active || contractors == QuestStatus.Ready)
        {
            notes.Add(Note(PresidentSave.QuestContractors, "quest_contractors_title", Text.Get("quest_contractors_why"),
                Text.Format("quest_contractors_progress", "workers", _workersSent, "workers_total", WorkersNeeded, "material", MaterialCarried, "material_total", MaterialNeeded), contractors, giver));
        }
        QuestStatus victory = Save.Quest(PresidentSave.QuestVictory);
        if (victory == QuestStatus.Active || victory == QuestStatus.Ready)
        {
            int metres = 0;
            if (_stand != null && IsInstanceValid(_stand))
            {
                metres = Mathf.RoundToInt(Mathf.Max(0.0f, carrier.Y - _stand.GlobalPosition.Y));
            }
            notes.Add(Note(PresidentSave.QuestVictory, "quest_victory_title", Text.Format("quest_victory_why", "drop", Mathf.RoundToInt(VictoryDrop)),
                Text.Format("quest_victory_progress", "metres", metres), victory, giver));
        }
        string signature = "";
        foreach (ObjectiveNote note in notes)
        {
            signature += note.Id + note.Progress + "|";
        }
        if (signature == _notes)
        {
            return;
        }
        _notes = signature;
        foreach (ObjectiveNote note in notes)
        {
            GameEvents.PublishQuestNote(note);
        }
    }

    private ObjectiveNote Note(string id, string titleId, string why, string progress, QuestStatus status, string giver)
    {
        if (status == QuestStatus.Ready)
        {
            progress = Text.Get("quest_ready");
        }
        return new ObjectiveNote("president:" + id, ObjectiveKind.Errand, Text.Get(titleId), why, progress, false, false, giver);
    }

    /// <summary>Publishes a quest's last line as done (the panel strikes it through).</summary>
    public void PublishDone(string id, string titleId)
    {
        GameEvents.PublishQuestNote(new ObjectiveNote("president:" + id, ObjectiveKind.Errand, Text.Get(titleId), "", Text.Get("quest_done"), true, false, Text.Get("npc_name")));
    }

    // Helpers.

    private int CurrentStation()
    {
        if (Visiting > 0)
        {
            return Visiting;
        }
        IRouteQuery? route = _run?.Route;
        if (route == null)
        {
            return FirstStation;
        }
        int nearest = FirstStation;
        float best = float.MaxValue;
        foreach (int station in _sets.Keys)
        {
            float distance = route.Station(station).DistanceTo(GameEvents.CarrierPosition);
            if (distance < best)
            {
                best = distance;
                nearest = station;
            }
        }
        return nearest;
    }

    private static bool Carried(string itemId)
    {
        foreach (string id in GameEvents.CarriedItems)
        {
            if (id == itemId)
            {
                return true;
            }
        }
        return false;
    }

    private static int Count(string path)
    {
        string itemId = path.GetFile().GetBaseName();
        int count = 0;
        foreach (string id in GameEvents.CarriedItems)
        {
            if (id == itemId)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>Uses up to <paramref name="most"/> carried items of a kind; returns how many.</summary>
    private static int Use(string path, int most)
    {
        string itemId = path.GetFile().GetBaseName();
        int used = 0;
        while (used < most && Count(path) > 0)
        {
            int before = Count(path);
            GameEvents.RequestItemUse(itemId);
            if (Count(path) >= before)
            {
                break;
            }
            used++;
        }
        return used;
    }
}
