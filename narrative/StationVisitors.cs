using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// Travellers at stations (M11 NPCs). Most stations have someone sitting by the fire, chosen by the
/// run's seed from the personas that belong to the current circle or above; nobody shows up twice
/// in a run while others are left. The traveller for the next station is seated ahead of time (the
/// first one at the start, the next whenever a station is reached), so nobody pops into view. A new
/// run clears them. Reads RunState.Route and StationReached.
/// </summary>
public partial class StationVisitors : Node3D
{
    [Export] public NpcPersona[] Personas { get; set; } = System.Array.Empty<NpcPersona>();
    /// <summary>Chance a station has a traveller (0 to 1).</summary>
    [Export] public float VisitChance { get; set; } = 0.75f;
    /// <summary>Where they sit relative to the fire: x across the slope (the lean-to is +x), z downhill (-z), in m.</summary>
    [Export] public Vector3 SeatOffset { get; set; } = new(-2.3f, 0.0f, 0.8f);

    public IReadOnlyList<NpcFigure> Figures => _figures;

    private readonly List<NpcFigure> _figures = new();
    private readonly HashSet<int> _seated = new();
    private readonly HashSet<string> _used = new();
    private RunState? _run;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        GameEvents.StationReached += OnStation;
        GameEvents.RespawnRequested += OnNewRun;
        _seatFirst = true;
    }

    private bool _seatFirst;

    public override void _Process(double delta)
    {
        // The route exists once the terrain has made the mountain: seat the first station's traveller then.
        if (_seatFirst && _run?.Route != null)
        {
            _seatFirst = false;
            SeatAhead(1);
        }
    }

    public override void _ExitTree()
    {
        GameEvents.StationReached -= OnStation;
        GameEvents.RespawnRequested -= OnNewRun;
    }

    /// <summary>The traveller at a station in this run, or null (the same for the same mountain).</summary>
    public NpcPersona? Pick(int station, int circle)
    {
        ulong seed = 0;
        if (_run != null)
        {
            seed = _run.Seed;
        }
        SplitMix64 random = new(SplitMix64.Mix(seed ^ 0x7E1A11Eul, (ulong)station));
        if (random.NextInt(1000) >= Mathf.RoundToInt(VisitChance * 1000.0f))
        {
            return null;
        }
        List<NpcPersona> fresh = new();
        List<NpcPersona> all = new();
        foreach (NpcPersona persona in Personas)
        {
            if (persona.MinCircle > circle)
            {
                continue;
            }
            all.Add(persona);
            if (!_used.Contains(persona.Id))
            {
                fresh.Add(persona);
            }
        }
        List<NpcPersona> pool = fresh;
        if (pool.Count == 0)
        {
            pool = all;
        }
        if (pool.Count == 0)
        {
            return null;
        }
        return pool[random.NextInt(pool.Count)];
    }

    private void OnStation(int station)
    {
        SeatAhead(station + 1);
    }

    /// <summary>Seats the traveller (if any) at a station, once per run.</summary>
    public void SeatAhead(int station)
    {
        IRouteQuery? route = _run?.Route;
        if (route == null || station < 1 || !_seated.Add(station))
        {
            return;
        }
        int circle = 0;
        if (_run != null)
        {
            circle = _run.Circle;
        }
        NpcPersona? persona = Pick(station, circle);
        if (persona == null)
        {
            return;
        }
        _used.Add(persona.Id);
        Vector3 fire = route.Station(station);
        NpcFigure figure = new();
        figure.Setup(persona, station, fire);
        AddChild(figure);
        figure.GlobalPosition = fire + SeatOffset;
        _figures.Add(figure);
        GameEvents.PublishVisitorSeated(station, persona.DisplayName, persona.Title);
    }

    private void OnNewRun()
    {
        foreach (NpcFigure figure in _figures)
        {
            figure.QueueFree();
        }
        _figures.Clear();
        _seated.Clear();
        _used.Clear();
        _seatFirst = true;
    }
}
