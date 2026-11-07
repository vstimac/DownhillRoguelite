using System.Collections.Generic;
using DownhillRoguelite.Core;
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
    /// <summary>Offerings stand on an arc this far downhill of the station's fire, in m.</summary>
    [Export] public float Distance { get; set; } = 3.5f;
    /// <summary>Angle between neighbouring offerings on the arc, in degrees.</summary>
    [Export] public float Spread { get; set; } = 38.0f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    public IReadOnlyList<BoonStone> Stones => _stones;

    private readonly List<BoonStone> _stones = new();
    private readonly HashSet<int> _offered = new();
    private RunState? _run;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        GameEvents.StationReached += Offer;
        GameEvents.RespawnRequested += Clear;
    }

    public override void _ExitTree()
    {
        GameEvents.StationReached -= Offer;
        GameEvents.RespawnRequested -= Clear;
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
        return picked;
    }

    /// <summary>Lays out a station's offerings (once per station per run).</summary>
    public void Offer(int station)
    {
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

    /// <summary>Applies the stone's boon and takes away the station's other offerings.</summary>
    public bool Choose(BoonStone chosen)
    {
        if (_run == null || chosen.Boon == null)
        {
            return false;
        }
        chosen.Boon.ApplyTo(_run.Modifiers);
        _run.Boons.Add(chosen.Boon.DisplayName);
        for (int i = _stones.Count - 1; i >= 0; i--)
        {
            if (_stones[i].Station == chosen.Station)
            {
                _stones[i].QueueFree();
                _stones.RemoveAt(i);
            }
        }
        GameEvents.PublishBoonChosen($"{chosen.Boon.DisplayName} ({chosen.Boon.Effects()})");
        return true;
    }

    private void Clear()
    {
        foreach (BoonStone stone in _stones)
        {
            stone.QueueFree();
        }
        _stones.Clear();
        _offered.Clear();
    }
}
