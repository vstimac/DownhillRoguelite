using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// Boons (M12): the first time the carrier reaches a station in a run, three offerings glow beside
/// its fire, picked from the boon list by the run's seed and the station. Choosing one (E) applies
/// it to RunState.Modifiers for the rest of the run and the others go dark; one per station. A new
/// run clears them. Reads StationReached and RunState.Route; publishes BoonChosen.
/// </summary>
public partial class BoonOfferings : Node3D
{
    [Export] public BoonDefinition[] Boons { get; set; } = System.Array.Empty<BoonDefinition>();
    [Export] public int OfferCount { get; set; } = 3;
    /// <summary>Bargains: boons with a price (progression/boons/). From BargainsFromStation on, the middle offering is one of them.</summary>
    [Export] public BoonDefinition[] Bargains { get; set; } = System.Array.Empty<BoonDefinition>();
    [Export] public int BargainsFromStation { get; set; } = 2;
    /// <summary>Expedition rules: arrival gives shelter; completed work earns the station's boon.</summary>
    [Export] public bool RequireCompletedWork { get; set; }
    /// <summary>Offerings stand on an arc this far downhill of the station's fire, in m.</summary>
    [Export] public float Distance { get; set; } = 4.0f;
    /// <summary>
    /// Angle between neighbouring offerings on the arc, in degrees: wide enough that the stones stand
    /// further apart than their labels are wide (BoonStone.LabelWidthMetres), so no two labels meet.
    /// </summary>
    [Export] public float Spread { get; set; } = 48.0f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    public IReadOnlyList<BoonStone> Stones => _stones;

    private readonly List<BoonStone> _stones = new();
    private readonly HashSet<int> _offered = new();
    private readonly HashSet<int> _earned = new();
    private readonly HashSet<int> _hardmodeOffered = new();
    private const string HardmodePath = "res://progression/boons/tuntun_hardmode.tres";
    private RunState? _run;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        GameEvents.StationReached += Offer;
        GameEvents.RespawnRequested += Clear;
        GameEvents.ContractResolved += OnContractResolved;
        GameEvents.RelicDelivered += OnRelicDelivered;
        GameEvents.SharedPickupTaken += OnSharedTaken;
    }

    public override void _ExitTree()
    {
        GameEvents.StationReached -= Offer;
        GameEvents.RespawnRequested -= Clear;
        GameEvents.ContractResolved -= OnContractResolved;
        GameEvents.RelicDelivered -= OnRelicDelivered;
        GameEvents.SharedPickupTaken -= OnSharedTaken;
    }

    /// <summary>The boons a station offers in this run: the same for the same mountain.</summary>
    public List<BoonDefinition> Pick(int station)
    {
        List<BoonDefinition> pool = new(Boons);
        List<BoonDefinition> picked = new();
        ulong seed = 0;
        if (_run != null)
        {
            seed = _run.Seed;
        }
        SplitMix64 random = new(SplitMix64.Mix(seed ^ 0xB0035ul, (ulong)station));
        while (picked.Count < OfferCount && pool.Count > 0)
        {
            int index = random.NextInt(pool.Count);
            picked.Add(pool[index]);
            pool.RemoveAt(index);
        }
        // The bargain has its own stream, so the plain offerings stay what they were for this mountain.
        if (Bargains.Length > 0 && station >= BargainsFromStation && picked.Count > 0)
        {
            SplitMix64 bargains = new(SplitMix64.Mix(seed ^ 0xBA4641ul, (ulong)station));
            picked[picked.Count / 2] = Bargains[bargains.NextInt(Bargains.Length)];
        }
        return picked;
    }

    /// <summary>Lays out a station's offerings (once per station per run).</summary>
    public void Offer(int station)
    {
        OfferTuntunHardmode(station);
        if (GameEvents.WasSharedPickupTaken("boon:" + station)) { return; }
        if (station < 1 || (RequireCompletedWork && !_earned.Contains(station))) { return; }
        IRouteQuery? route = _run?.Route;
        if (route == null || !_offered.Add(station))
        {
            return;
        }
        Vector3 centre = route.Station(station);
        List<BoonDefinition> picks = Pick(station);
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        for (int i = 0; i < picks.Count; i++)
        {
            float angle = Mathf.DegToRad((i - (picks.Count - 1) * 0.5f) * Spread);
            // Downhill of the fire (-Z), away from the lean-to on +X.
            Vector3 at = centre + new Vector3(Mathf.Sin(angle), 0.0f, -Mathf.Cos(angle)) * Distance;
            Godot.Collections.Dictionary hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 6.0f, at + Vector3.Down * 6.0f, GroundMask));
            if (hit.Count > 0)
            {
                at = (Vector3)hit["position"];
            }
            BoonStone stone = new();
            stone.Setup(picks[i], station, this);
            AddChild(stone);
            stone.GlobalPosition = at;
            _stones.Add(stone);
        }
    }

    private void OnSharedTaken(string key)
    {
        if (!key.StartsWith("boon:", System.StringComparison.Ordinal) || !int.TryParse(key.Substring(5), out int station)) { return; }
        for (int i = _stones.Count - 1; i >= 0; i--)
        {
            if (_stones[i].Station == station)
            { if (IsInstanceValid(_stones[i])) { _stones[i].QueueFree(); } _stones.RemoveAt(i); }
        }
    }

    /// <summary>
    /// Tuntun Sahur hard mode (once per run): a stone beside the station's hand cart, offered at every
    /// rest site until it is taken. It is not one of the station's three boons.
    /// </summary>
    private void OfferTuntunHardmode(int station)
    {
        if (station < 1 || _run == null || _run.TuntunHardmode || !_hardmodeOffered.Add(station)) { return; }
        if (GameEvents.WasSharedPickupTaken("boon:tuntun:" + station)) { return; }
        if (!ResourceLoader.Exists(HardmodePath) || ResourceLoader.Load(HardmodePath) is not BoonDefinition boon) { return; }
        IRouteQuery? route = _run.Route;
        if (route == null) { return; }
        Vector3 centre = route.Station(station);
        Vector3 at = centre + new Vector3(Distance, 0.0f, 0.0f);
        HandCart? cart = NearestCart(centre);
        if (cart != null)
        {
            at = cart.GlobalPosition + cart.GlobalBasis.X * 1.6f;
        }
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        Godot.Collections.Dictionary hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 6.0f, at + Vector3.Down * 6.0f, GroundMask));
        if (hit.Count > 0)
        {
            at = (Vector3)hit["position"];
        }
        BoonStone stone = new();
        stone.Setup(boon, station, this);
        AddChild(stone);
        stone.GlobalPosition = at;
        _stones.Add(stone);
    }

    /// <summary>The hand cart of the station nearest to <paramref name="centre"/>, if the scene has one.</summary>
    private HandCart? NearestCart(Vector3 centre)
    {
        HandCart? best = null;
        float bestDistance = float.MaxValue;
        foreach (Node node in GetTree().CurrentScene.FindChildren("HandCart", "", true, false))
        {
            if (node is not HandCart cart) { continue; }
            float distance = cart.GlobalPosition.DistanceSquaredTo(centre);
            if (distance < bestDistance) { best = cart; bestDistance = distance; }
        }
        return best;
    }

    private void RemoveTuntunHardmodeStones()
    {
        for (int i = _stones.Count - 1; i >= 0; i--)
        {
            if (_stones[i].Boon?.TuntunHardmode != true) { continue; }
            if (IsInstanceValid(_stones[i])) { _stones[i].QueueFree(); }
            _stones.RemoveAt(i);
        }
    }

    /// <summary>Applies the stone's boon and takes away the station's other offerings.</summary>
    public bool Choose(BoonStone chosen)
    {
        if (_run == null || chosen.Boon == null || !_stones.Contains(chosen) || chosen.IsQueuedForDeletion())
        {
            return false;
        }
        if (chosen.Boon.TuntunHardmode)
        {
            // Hard mode is not a stat boon: it starts the run's Tuntun Sahurs and leaves the station's offerings alone.
            _run.StartTuntunHardmode();
            _run.Boons.Add(chosen.Boon.DisplayName);
            RemoveTuntunHardmodeStones();
            GameEvents.PublishBoonChosen($"{chosen.Boon.DisplayName} (Tuntun Sahurs: 3, +1 per station)");
            return true;
        }
        if (chosen.Boon.IsConditional)
        {
            // Works only in its situation: RunState switches it on and off (and saves it without its effect).
            _run.AddConditionalBoon(chosen.Boon);
        }
        else
        {
            chosen.Boon.ApplyTo(_run.Modifiers);
        }
        _run.Boons.Add(chosen.Boon.DisplayName);
        for (int i = _stones.Count - 1; i >= 0; i--)
        {
            if (_stones[i].Station == chosen.Station)
            {
                _stones[i].QueueFree();
                _stones.RemoveAt(i);
            }
        }
        string effects = chosen.Boon.Gifts();
        if (chosen.Boon.IsBargain)
        {
            effects += "; price: " + chosen.Boon.Costs();
        }
        GameEvents.PublishBoonChosen($"{chosen.Boon.DisplayName} ({effects})");
        return true;
    }

    private void OnContractResolved(ContractNote note)
    {
        if (note.Status == ContractStatus.Delivered) { Earn(note.ToStation); }
    }

    private void OnRelicDelivered(RelicNote note)
    {
        Earn(note.Station);
    }

    private void Earn(int station)
    {
        if (!RequireCompletedWork || station < 1) { return; }
        _earned.Add(station);
        Offer(station);
    }

    private void Clear()
    {
        foreach (BoonStone stone in _stones)
        {
            stone.QueueFree();
        }
        _stones.Clear();
        _offered.Clear();
        _earned.Clear();
        _hardmodeOffered.Clear();
    }
}
