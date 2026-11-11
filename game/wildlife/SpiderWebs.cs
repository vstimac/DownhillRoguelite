using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// Spider webs in the deeper circles (the giant spider's, from MinCircle on): strung between pairs of
/// trees standing close together round the carrier. Which pairs carry a web follows from where the
/// trees stand, so a web is always in the same place. Walking into one tears it and it clings to the
/// legs: every stride is slow for a few seconds (GameEvents.Snared). A torn web stays torn for the
/// run. BeastDirector adds it.
/// </summary>
public partial class SpiderWebs : Node3D
{
    [Export] public int MinCircle { get; set; } = 3;
    /// <summary>Webs are looked for among the trees this far round the carrier, in m.</summary>
    [Export] public float Radius { get; set; } = 45.0f;
    [Export] public float CheckEvery { get; set; } = 1.0f;
    /// <summary>Two trunks this far apart (centre to centre, in m) can carry a web.</summary>
    [Export] public Vector2 PairDistance { get; set; } = new(2.2f, 7.0f);
    /// <summary>The share of close pairs that do.</summary>
    [Export] public float Chance { get; set; } = 0.3f;
    [Export] public int MaxWebs { get; set; } = 16;
    /// <summary>The web's centre this high above the ground, in m.</summary>
    [Export] public float Height { get; set; } = 1.15f;
    [Export] public float MaxWebRadius { get; set; } = 1.05f;
    /// <summary>Room left beside each trunk, in m.</summary>
    [Export] public float TrunkRoom { get; set; } = 0.35f;
    /// <summary>The carrier's middle this high above the feet, in m (what walks into the web).</summary>
    [Export] public float CarrierMiddle { get; set; } = 0.9f;
    /// <summary>How close to the web's face the carrier's middle must come, in m.</summary>
    [Export] public float Thickness { get; set; } = 0.35f;
    [Export] public float SnareSeconds { get; set; } = 3.0f;
    /// <summary>Stride speed while the web clings (share of normal).</summary>
    [Export] public float SnareSpeed { get; set; } = 0.35f;
    [Export] public string SnareHint { get; set; } = "You blunder into a spider web. It clings to your legs…";
    [Export] public string BurnHint { get; set; } = "Your torch burns through a spider web.";
    [Export] public string[] TearSounds { get; set; } = { "res://audio/creatures/web_tear_0.wav", "res://audio/creatures/web_tear_1.wav" };
    [Export] public StringName SoundBus { get; set; } = "Ambience";
    [Export] public float TearVolumeDb { get; set; } = -2.0f;
    [Export] public Color Thread { get; set; } = new(0.86f, 0.87f, 0.92f, 0.55f);
    /// <summary>How bright the threads are at full night (unlit lines would glow white in the dark).</summary>
    [Export] public float NightBrightness { get; set; } = 0.22f;
    /// <summary>Forces the circle (tests); negative: the run's.</summary>
    [Export] public int CircleOverride { get; set; } = -1;

    /// <summary>The webs hanging now (tests).</summary>
    public IReadOnlyCollection<SpiderWeb> Webs => _webs.Values;
    /// <summary>Webs the carrier tore this run (tests).</summary>
    public int Torn => _torn.Count;
    /// <summary>Webs a burning torch went through (tests).</summary>
    public int Burned { get; private set; }

    private readonly Dictionary<string, SpiderWeb> _webs = new();
    private readonly HashSet<string> _torn = new();
    private RunState? _run;
    private Vector3 _carrier;
    private Vector3 _previous;
    private bool _hasCarrier;
    private float _check;
    private AudioStreamPlayer3D? _tear;
    private StandardMaterial3D? _material;
    private WorldClock? _clock;
    private readonly RandomNumberGenerator _random = new();

    public override void _Ready()
    {
        TopLevel = true;
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        _material = SpiderWeb.MakeMaterial(Thread);
        GameEvents.LocomotionUpdated += OnLocomotion;
        GameEvents.NewMountain += OnNewMountain;
        GameEvents.RespawnRequested += Forget;
        // Headless runs (tests) have no audio output.
        if (DisplayServer.GetName() != "headless")
        {
            _random.Randomize();
            _tear = new AudioStreamPlayer3D { Name = "Tear", Bus = SoundBus, VolumeDb = TearVolumeDb, UnitSize = 3.0f, MaxDistance = 25.0f };
            AddChild(_tear);
        }
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
        GameEvents.NewMountain -= OnNewMountain;
        GameEvents.RespawnRequested -= Forget;
    }

    private void OnNewMountain(ulong seed) => Forget();

    /// <summary>A new run: every web is whole again (and the old ones go with the old mountain).</summary>
    public void Forget()
    {
        _torn.Clear();
        ClearWebs();
    }

    private void ClearWebs()
    {
        foreach (SpiderWeb web in _webs.Values)
        {
            if (IsInstanceValid(web))
            {
                web.QueueFree();
            }
        }
        _webs.Clear();
    }

    private int Circle()
    {
        if (CircleOverride >= 0)
        {
            return CircleOverride;
        }
        if (_run != null)
        {
            return _run.Circle;
        }
        return 0;
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        Vector3 middle = snapshot.Position + Vector3.Up * CarrierMiddle;
        if (!_hasCarrier || _previous.DistanceTo(middle) > 3.0f)
        {
            // The first report, or a jump (a teleport): nothing was walked through on the way.
            _previous = middle;
        }
        _carrier = middle;
        _hasCarrier = true;
        SpiderWeb? caught = null;
        foreach (SpiderWeb web in _webs.Values)
        {
            if (IsInstanceValid(web) && web.IsInsideTree() && (web.Holds(middle, Thickness) || web.Crossed(_previous, middle)))
            {
                caught = web;
                break;
            }
        }
        _previous = middle;
        if (caught != null)
        {
            Tear(caught);
        }
    }

    private void Tear(SpiderWeb web)
    {
        _torn.Add(web.Key);
        _webs.Remove(web.Key);
        web.QueueFree();
        PlayTear(web.GlobalPosition);
        // A burning torch shrivels the web before it can cling (and nothing feels it tear).
        if (GameEvents.CarrierTorchLit)
        {
            Burned++;
            if (BurnHint.Length > 0)
            {
                GameEvents.PublishHint(BurnHint);
            }
            return;
        }
        GameEvents.PublishSnared(SnareSeconds, SnareSpeed);
        if (SnareHint.Length > 0)
        {
            GameEvents.PublishHint(SnareHint);
        }
    }

    /// <summary>Dims the threads with the light: pale by day, barely there at night.</summary>
    private void Shade()
    {
        if (_material == null)
        {
            return;
        }
        float daylight = 1.0f;
        if (_clock != null)
        {
            daylight = _clock.Daylight;
        }
        float bright = Mathf.Lerp(NightBrightness, 1.0f, Mathf.Clamp(daylight, 0.0f, 1.0f));
        Color colour = new(Thread.R * bright, Thread.G * bright, Thread.B * bright, Thread.A);
        if (!_material.AlbedoColor.IsEqualApprox(colour))
        {
            _material.AlbedoColor = colour;
        }
    }

    private void PlayTear(Vector3 at)
    {
        if (_tear == null || TearSounds.Length == 0)
        {
            return;
        }
        string path = TearSounds[_random.RandiRange(0, TearSounds.Length - 1)];
        if (!ResourceLoader.Exists(path) || ResourceLoader.Load<AudioStream>(path) is not AudioStream stream)
        {
            return;
        }
        _tear.GlobalPosition = at;
        _tear.Stream = stream;
        _tear.PitchScale = _random.RandfRange(0.9f, 1.1f);
        _tear.Play();
    }

    public override void _Process(double delta)
    {
        _check -= (float)delta;
        if (_check > 0.0f || !_hasCarrier)
        {
            return;
        }
        _check = CheckEvery;
        Shade();
        if (Circle() < MinCircle)
        {
            if (_webs.Count > 0)
            {
                ClearWebs();
            }
            return;
        }
        Refresh();
    }

    /// <summary>Drops the webs left far behind and strings new ones among the trees near the carrier.</summary>
    public void Refresh()
    {
        List<string> far = new();
        foreach (KeyValuePair<string, SpiderWeb> pair in _webs)
        {
            if (!IsInstanceValid(pair.Value) || Flat(pair.Value.GlobalPosition - _carrier) > Radius * 1.3f)
            {
                far.Add(pair.Key);
            }
        }
        foreach (string key in far)
        {
            if (IsInstanceValid(_webs[key]))
            {
                _webs[key].QueueFree();
            }
            _webs.Remove(key);
        }
        if (_webs.Count >= MaxWebs)
        {
            return;
        }
        List<Vector3> trunks = Trunks();
        for (int i = 0; i < trunks.Count && _webs.Count < MaxWebs; i++)
        {
            for (int j = i + 1; j < trunks.Count && _webs.Count < MaxWebs; j++)
            {
                float apart = Flat(trunks[j] - trunks[i]);
                if (apart < PairDistance.X || apart > PairDistance.Y)
                {
                    continue;
                }
                string key = Key(trunks[i], trunks[j]);
                if (_webs.ContainsKey(key) || _torn.Contains(key))
                {
                    continue;
                }
                ulong hash = Hash(key);
                if ((hash % 10000UL) >= (ulong)(Mathf.Clamp(Chance, 0.0f, 1.0f) * 10000.0f))
                {
                    continue;
                }
                float radius = Mathf.Min(MaxWebRadius, apart * 0.5f - TrunkRoom);
                if (radius < 0.4f)
                {
                    continue;
                }
                float ground = Mathf.Max(trunks[i].Y, trunks[j].Y);
                Vector3 left = new(trunks[i].X, ground + Height, trunks[i].Z);
                Vector3 right = new(trunks[j].X, ground + Height, trunks[j].Z);
                SpiderWeb web = new() { Name = "Web" };
                web.Setup(key, left, right, radius, hash, _material);
                AddChild(web);
                _webs[key] = web;
            }
        }
    }

    /// <summary>The trunks round the carrier, each at the foot of the tree (its ground).</summary>
    private List<Vector3> Trunks()
    {
        List<Vector3> found = new();
        foreach (Node body in GetTree().GetNodesInGroup(TerrainChunk.TreeGroup))
        {
            foreach (Node child in body.GetChildren())
            {
                if (child is not CollisionShape3D shape)
                {
                    continue;
                }
                Vector3 at = shape.GlobalPosition;
                if (Flat(at - _carrier) > Radius)
                {
                    continue;
                }
                float half = 1.0f;
                if (shape.Shape is CylinderShape3D cylinder)
                {
                    half = cylinder.Height * 0.5f;
                }
                else if (shape.Shape is CapsuleShape3D capsule)
                {
                    half = capsule.Height * 0.5f;
                }
                found.Add(at + Vector3.Down * half);
            }
        }
        return found;
    }

    private static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();

    /// <summary>The pair's name, the same whichever trunk comes first (to a tenth of a metre).</summary>
    private static string Key(Vector3 a, Vector3 b)
    {
        long ax = Mathf.RoundToInt(a.X * 10.0f);
        long az = Mathf.RoundToInt(a.Z * 10.0f);
        long bx = Mathf.RoundToInt(b.X * 10.0f);
        long bz = Mathf.RoundToInt(b.Z * 10.0f);
        if (ax > bx || (ax == bx && az > bz))
        {
            (ax, bx) = (bx, ax);
            (az, bz) = (bz, az);
        }
        return $"{ax}:{az}:{bx}:{bz}";
    }

    /// <summary>FNV-1a: the same number for the same pair on every machine and every run.</summary>
    private static ulong Hash(string key)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in key)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash;
    }
}
