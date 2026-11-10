using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// Relics (roguelite Phase 3). Each run, every relic may lie once on the mountain, at a spot chosen
/// by the seed: halfway along a side path of a stage in its range (beside the main path if the
/// stage has none), by a small stone with candles. It is a real item: the carrier may walk past,
/// pick it up (it weighs and fills the pack like anything else), drop it and take it again.
///
/// Coming close to it, or picking it up, finds it: the first time in a run the journal notes what
/// was seen (RelicDiscovered), and the "seen" mark stays across runs. Fragile relics lose condition
/// to hard landings and falls like contract cargo, less when packed deep. Every station has an altar:
/// E, then E again, leaves a carried relic there to be studied: score (in proportion to its
/// condition), one of its interpretations in the journal (RelicDelivered), and a "studied" mark
/// that stays across runs. A new run clears every relic, site and altar; the marks remain.
/// </summary>
public partial class RelicDirector : Node3D
{
    [Export] public RelicDefinition[] Relics { get; set; } = System.Array.Empty<RelicDefinition>();
    /// <summary>Where along a side path (0 split, 1 join) a relic lies.</summary>
    [Export] public Vector2 BranchSpan { get; set; } = new(0.42f, 0.58f);
    /// <summary>...or along the stage (0 top, 1 bottom) when the stage has no side path.</summary>
    [Export] public Vector2 MainPathSpan { get; set; } = new(0.4f, 0.6f);
    /// <summary>Distance beside the path's centre, in m.</summary>
    [Export] public float SideOffset { get; set; } = 2.2f;
    /// <summary>The carrier finds a relic by coming this close, in m.</summary>
    [Export] public float DiscoverDistance { get; set; } = 4.5f;
    [Export] public float LoadDistance { get; set; } = 260.0f;
    /// <summary>Where a station's altar stands, from its fire (x across, z along the slope; -z is downhill), in m.</summary>
    [Export] public Vector3 AltarOffset { get; set; } = new(6.0f, 0.0f, -3.5f);
    [Export] public float AltarLoadDistance { get; set; } = 400.0f;
    [Export] public float GroundSink { get; set; } = 0.05f;

    [ExportGroup("Condition (INITIAL TUNING)")]
    [Export] public float HardLanding { get; set; } = 6.0f;
    [Export] public float LandingDamagePerMs { get; set; } = 5.0f;
    [Export] public float FallDamage { get; set; } = 12.0f;
    [Export] public float ProtectedFactor { get; set; } = 0.4f;
    [Export] public float ExposedFactor { get; set; } = 1.4f;
    /// <summary>At or above this condition a studied relic scores in full; below it, in proportion.</summary>
    [Export] public float IntactCondition { get; set; } = 90.0f;

    private const ulong RelicSalt = 0x52454C43ul;

    public readonly record struct Placement(RelicDefinition Relic, int Stage, int Path, Vector3 Position);

    public IReadOnlyList<RelicDefinition> ValidRelics => _valid;
    public IReadOnlyDictionary<string, Node3D> Sites => _sites;
    public IReadOnlyDictionary<int, RelicAltar> Altars => _altars;

    private readonly List<RelicDefinition> _valid = new();
    private readonly Dictionary<string, Placement> _plan = new();
    private readonly Dictionary<string, Node3D> _sites = new();
    private readonly Dictionary<int, RelicAltar> _altars = new();
    private readonly HashSet<string> _taken = new();
    private readonly HashSet<string> _delivered = new();
    private readonly Dictionary<string, float> _condition = new();
    private bool _planned;
    private ulong _plannedSeed;
    private RunState? _run;
    private Vector3 _focus;
    private bool _hasFocus;
    private string _awaitedCargo = "";
    private bool _answer;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        Reload();
        GameEvents.LocomotionUpdated += OnLocomotion;
        GameEvents.RespawnRequested += OnRespawn;
        GameEvents.ItemPickedUp += OnItemPickedUp;
        GameEvents.CargoHandedOver += OnCargoAnswer;
        GameEvents.Landed += OnLanded;
        GameEvents.DownedChanged += OnDowned;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
        GameEvents.RespawnRequested -= OnRespawn;
        GameEvents.ItemPickedUp -= OnItemPickedUp;
        GameEvents.CargoHandedOver -= OnCargoAnswer;
        GameEvents.Landed -= OnLanded;
        GameEvents.DownedChanged -= OnDowned;
    }

    /// <summary>Reads Relics again, keeping only definitions that make sense (the rest are skipped with a warning).</summary>
    public void Reload()
    {
        _valid.Clear();
        HashSet<string> ids = new();
        foreach (RelicDefinition? relic in Relics)
        {
            if (relic == null)
            {
                continue;
            }
            string problem = relic.Problem();
            if (problem.Length == 0 && !ids.Add(relic.Id))
            {
                problem = relic.Id + " is defined twice";
            }
            if (problem.Length > 0)
            {
                GD.PushWarning("RelicDirector: skipping a relic: " + problem);
                continue;
            }
            _valid.Add(relic);
        }
        _valid.Sort(CompareIds);
        _planned = false;
    }

    private static int CompareIds(RelicDefinition a, RelicDefinition b)
    {
        return string.CompareOrdinal(a.Id, b.Id);
    }

    public RelicDefinition? Find(string id)
    {
        foreach (RelicDefinition relic in _valid)
        {
            if (relic.Id == id)
            {
                return relic;
            }
        }
        return null;
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        _focus = snapshot.Position;
        _hasFocus = true;
    }

    /// <summary>A new run: no sites, no altars, nothing taken or carried; the seen and studied marks stay (RunState).</summary>
    private void OnRespawn()
    {
        Forget();
    }

    public void Forget()
    {
        foreach (Node3D site in _sites.Values)
        {
            site.QueueFree();
        }
        _sites.Clear();
        foreach (RelicAltar altar in _altars.Values)
        {
            altar.QueueFree();
        }
        _altars.Clear();
        _plan.Clear();
        _taken.Clear();
        _delivered.Clear();
        _condition.Clear();
        _planned = false;
    }

    /// <summary>Where each relic lies this run (relics that don't lie on this mountain are absent).</summary>
    public IReadOnlyDictionary<string, Placement> Plan(IRouteQuery route)
    {
        if (_run == null)
        {
            return _plan;
        }
        if (_planned && _plannedSeed == _run.Seed)
        {
            return _plan;
        }
        _plan.Clear();
        _planned = true;
        _plannedSeed = _run.Seed;
        HashSet<int> usedStages = new();
        for (int i = 0; i < _valid.Count; i++)
        {
            RelicDefinition relic = _valid[i];
            SplitMix64 random = new(SplitMix64.Mix(_run.Seed ^ RelicSalt, (ulong)(i + 1)));
            if (random.NextFloat() >= relic.SpawnChance)
            {
                continue;
            }
            int span = relic.StageRange.Y - relic.StageRange.X + 1;
            int stage = relic.StageRange.X + Mathf.Min((int)(random.NextFloat() * span), span - 1);
            // Two relics never share a stage: take the next free one in the range, if any.
            for (int step = 0; step < span && usedStages.Contains(stage); step++)
            {
                stage = relic.StageRange.X + (stage - relic.StageRange.X + 1) % span;
            }
            if (usedStages.Contains(stage))
            {
                continue;
            }
            usedStages.Add(stage);
            _plan[relic.Id] = Place(route, relic, stage, random);
        }
        return _plan;
    }

    private Placement Place(IRouteQuery route, RelicDefinition relic, int stage, SplitMix64 random)
    {
        int paths = route.PathCount(stage);
        int path = 0;
        float z;
        if (paths > 1)
        {
            path = Mathf.Min(1 + (int)(random.NextFloat() * (paths - 1)), paths - 1);
            Vector2 zone = route.BranchZone(stage);
            z = Mathf.Lerp(zone.X, zone.Y, Mathf.Lerp(BranchSpan.X, BranchSpan.Y, random.NextFloat()));
        }
        else
        {
            float top = route.StartZ;
            if (stage > 0)
            {
                top = route.Station(stage).Z;
            }
            z = Mathf.Lerp(top, route.Station(stage + 1).Z, Mathf.Lerp(MainPathSpan.X, MainPathSpan.Y, random.NextFloat()));
        }
        Vector3 centre = route.PathPoint(stage, path, z);
        float side = 1.0f;
        if (random.NextFloat() < 0.5f)
        {
            side = -1.0f;
        }
        float x = centre.X + side * SideOffset;
        return new Placement(relic, stage, path, new Vector3(x, route.GroundHeight(x, z) - GroundSink, z));
    }

    public override void _PhysicsProcess(double delta)
    {
        IRouteQuery? route = _run?.Route;
        if (!_hasFocus || route == null || _run == null)
        {
            return;
        }
        StreamSites(route);
        StreamAltars(route);
        DiscoverNearby();
    }

    private void StreamSites(IRouteQuery route)
    {
        foreach (Placement placement in Plan(route).Values)
        {
            string id = placement.Relic.Id;
            bool near = Mathf.Abs(placement.Position.Z - _focus.Z) <= LoadDistance && !_taken.Contains(id);
            if (near && !_sites.ContainsKey(id))
            {
                Node3D? site = BuildSite(placement);
                if (site != null)
                {
                    _sites[id] = site;
                }
            }
            else if (!near && _sites.TryGetValue(id, out Node3D? old))
            {
                old.QueueFree();
                _sites.Remove(id);
            }
        }
    }

    /// <summary>A stone with candles and the relic on the ground before it (a real item, picked up with E).</summary>
    private Node3D? BuildSite(Placement placement)
    {
        if (ResourceLoader.Load(placement.Relic.ItemPath) is not ItemDefinition kind || kind.Duplicate() is not ItemDefinition item)
        {
            return null;
        }
        item.CargoInstanceId = placement.Relic.CargoId;
        Node3D site = new() { Name = "RelicSite_" + placement.Relic.Id };
        AddChild(site);
        site.GlobalPosition = placement.Position;
        RelicShrine shrine = new();
        site.AddChild(shrine);
        shrine.Position = new Vector3(0.0f, 0.0f, -0.7f);
        WorldItem world = new() { Name = "Relic", Definition = item };
        site.AddChild(world);
        world.Position = new Vector3(0.0f, 0.15f, 0.2f);
        return site;
    }

    private void StreamAltars(IRouteQuery route)
    {
        List<int> wanted = new();
        int first = Mathf.Max(1, route.NextStationIndex(_focus.Z + AltarLoadDistance));
        for (int station = first; ; station++)
        {
            Vector3 centre = route.Station(station);
            if (centre.Z < _focus.Z - AltarLoadDistance)
            {
                break;
            }
            wanted.Add(station);
            if (_altars.ContainsKey(station))
            {
                continue;
            }
            RelicAltar altar = new();
            altar.Setup(station, this);
            AddChild(altar);
            Vector3 at = centre + AltarOffset;
            altar.GlobalPosition = new Vector3(at.X, route.GroundHeight(at.X, at.Z) - GroundSink, at.Z);
            _altars[station] = altar;
        }
        List<int> drop = new();
        foreach (int station in _altars.Keys)
        {
            if (!wanted.Contains(station))
            {
                drop.Add(station);
            }
        }
        foreach (int station in drop)
        {
            _altars[station].QueueFree();
            _altars.Remove(station);
        }
    }

    private void DiscoverNearby()
    {
        foreach (Placement placement in _plan.Values)
        {
            if (_run!.RelicsSeen.ReadThisRun(placement.Relic.Id))
            {
                continue;
            }
            Vector3 offset = placement.Position - _focus;
            if (new Vector2(offset.X, offset.Z).Length() <= DiscoverDistance)
            {
                Discover(placement.Relic);
            }
        }
    }

    /// <summary>Marks a relic found this run (and ever); announces it the first time this run. False if already found this run.</summary>
    public bool Discover(RelicDefinition relic)
    {
        if (_run == null || _run.RelicsSeen.ReadThisRun(relic.Id))
        {
            return false;
        }
        _run.RelicsSeen.Read(relic.Id, out bool _, out bool firstEver);
        GameEvents.PublishRelicDiscovered(new RelicNote(relic.Id, relic.DisplayName, relic.SeenText, firstEver));
        return true;
    }

    private void OnItemPickedUp(string itemId, string category)
    {
        foreach (string cargo in GameEvents.CarriedCargo)
        {
            RelicDefinition? relic = FromCargo(cargo);
            if (relic == null || _taken.Contains(relic.Id))
            {
                continue;
            }
            _taken.Add(relic.Id);
            if (!_condition.ContainsKey(relic.Id))
            {
                _condition[relic.Id] = 100.0f;
            }
            Discover(relic);
            // The site's candles stay lit; the relic itself is in the pack now.
        }
    }

    private RelicDefinition? FromCargo(string cargoId)
    {
        foreach (RelicDefinition relic in _valid)
        {
            if (relic.CargoId == cargoId)
            {
                return relic;
            }
        }
        return null;
    }

    /// <summary>The relics in the carrier's pack or pockets right now.</summary>
    public List<RelicDefinition> CarriedRelics()
    {
        List<RelicDefinition> carried = new();
        foreach (string cargo in GameEvents.CarriedCargo)
        {
            RelicDefinition? relic = FromCargo(cargo);
            if (relic != null)
            {
                carried.Add(relic);
            }
        }
        return carried;
    }

    /// <summary>A relic's condition this run, 100 (intact) to 0.</summary>
    public float ConditionOf(string id)
    {
        if (_condition.TryGetValue(id, out float condition))
        {
            return condition;
        }
        return 100.0f;
    }

    public bool WasDelivered(string id)
    {
        return _delivered.Contains(id);
    }

    /// <summary>A knock to every carried fragile relic, scaled by its fragility and how exposed it is in the pack.</summary>
    public void Knock(float amount)
    {
        foreach (RelicDefinition relic in CarriedRelics())
        {
            if (relic.Fragility <= 0.0f || amount <= 0.0f)
            {
                continue;
            }
            float exposure = 1.0f;
            if (GameEvents.CargoExposure.TryGetValue(relic.CargoId, out float packed))
            {
                exposure = Mathf.Clamp(packed, 0.0f, 1.0f);
            }
            float before = ConditionOf(relic.Id);
            float after = Mathf.Max(0.0f, before - amount * relic.Fragility * Mathf.Lerp(ProtectedFactor, ExposedFactor, exposure));
            _condition[relic.Id] = after;
            if (before - after >= 1.0f)
            {
                string state = ContractResolver.ConditionText(after);
                if (after <= 0.0f)
                {
                    state = "ruined";
                }
                GameEvents.PublishHint(relic.DisplayName + " took a knock: " + state + ".");
            }
        }
    }

    private void OnLanded(float speed)
    {
        if (speed > HardLanding)
        {
            Knock((speed - HardLanding) * LandingDamagePerMs);
        }
    }

    private void OnDowned(bool downed)
    {
        if (downed)
        {
            Knock(FallDamage);
        }
    }

    /// <summary>What leaving a relic now would score: in full when intact, in proportion to its condition when damaged.</summary>
    public int ScoreFor(RelicDefinition relic)
    {
        float condition = ConditionOf(relic.Id);
        if (condition >= IntactCondition)
        {
            return relic.BaseScore;
        }
        return Mathf.RoundToInt(relic.BaseScore * Mathf.Clamp(condition, 0.0f, 100.0f) / 100.0f);
    }

    /// <summary>The interpretation this run's study gives (chosen by the seed among the relic's readings).</summary>
    public string InterpretationFor(RelicDefinition relic)
    {
        ulong seed = 0;
        if (_run != null)
        {
            seed = _run.Seed;
        }
        int index = _valid.IndexOf(relic);
        SplitMix64 random = new(SplitMix64.Mix(seed ^ RelicSalt ^ 0x5354554455ul, (ulong)(index + 1)));
        int pick = Mathf.Min((int)(random.NextFloat() * relic.Interpretations.Length), relic.Interpretations.Length - 1);
        return relic.Interpretations[pick];
    }

    /// <summary>
    /// Leaves a carried relic at station <paramref name="station"/>'s altar: the inventory removes that
    /// very item, then it is studied (once per run) and scored. False if it isn't carried or can't be left.
    /// </summary>
    public bool Deliver(RelicDefinition relic, int station)
    {
        if (!relic.CanDeliver || _delivered.Contains(relic.Id) || _run == null)
        {
            return false;
        }
        _awaitedCargo = relic.CargoId;
        _answer = false;
        GameEvents.RequestCargoHandOver(relic.CargoId);
        _awaitedCargo = "";
        if (!_answer)
        {
            return false;
        }
        _delivered.Add(relic.Id);
        int score = ScoreFor(relic);
        float condition = ConditionOf(relic.Id);
        _run.RelicsStudied.Read(relic.Id, out bool _, out bool firstEver);
        string reading = InterpretationFor(relic);
        GameEvents.PublishRelicDelivered(new RelicNote(relic.Id, relic.DisplayName, reading, firstEver, station, condition, score));
        GameEvents.PublishHint("Left " + relic.DisplayName.ToLowerInvariant() + " at the altar (+" + score + "). It's in your journal.");
        return true;
    }

    private void OnCargoAnswer(string cargoId, bool success)
    {
        if (_awaitedCargo.Length > 0 && cargoId == _awaitedCargo)
        {
            _answer = success;
        }
    }
}
