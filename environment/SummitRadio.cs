using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// The summit's Renault 4: an old car parked at the top of every mountain, and beside it a skeleton
/// on a bench with a rose in his hand. Walk up and press E: he plays "Reno 4" on the radio (the
/// player's own songs, through RadioPlayer) and the radio page opens. While music plays he nods his
/// head and sways to it. Stands on the summit's level pad (the terrain flattens it, and nothing is
/// scattered on it: IRouteQuery.SummitPad), placed once the run's mountain exists and again for
/// every new mountain; if the ground under it still tilts, the car follows the tilt. The car is solid.
/// </summary>
public partial class SummitRadio : Node3D, IInteractable, IDescribed
{
    [Export] public PackedScene? CarScene { get; set; }
    [Export] public PackedScene? SkeletonScene { get; set; }
    [Export] public PackedScene? BenchScene { get; set; }
    /// <summary>The car's centre from the summit pad's centre (x, z in the world), in m.</summary>
    [Export] public Vector2 CarOffset { get; set; } = new(0.0f, 0.9f);
    /// <summary>The car's turn about the vertical, in degrees (0: its length along the route; 90: across it).</summary>
    [Export] public float CarYawDegrees { get; set; } = 90.0f;
    /// <summary>The car's size for its collision and E reach (width, height, length), in m.</summary>
    [Export] public Vector3 CarSize { get; set; } = new(1.7f, 1.6f, 3.7f);
    [Export] public float GroundSink { get; set; } = 0.05f;
    /// <summary>Where the bench stands, in the car's frame (m): beside the car, towards the start.</summary>
    [Export] public Vector3 BenchOffset { get; set; } = new(1.9f, 0.0f, 0.3f);
    /// <summary>The bench's turn relative to the car, in degrees (90: it faces the start).</summary>
    [Export] public float BenchYawDegrees { get; set; } = 90.0f;
    /// <summary>The Kenney bench is made small; this brings its seat to 0.45 m.</summary>
    [Export] public float BenchScale { get; set; } = 1.9f;
    /// <summary>The bench is painted as old weathered wood (the Kenney model is bright plastic).</summary>
    [Export] public Color BenchColour { get; set; } = new(0.34f, 0.24f, 0.16f);
    /// <summary>Where the skeleton sits, in the bench's frame (m): his seat on the plank.</summary>
    [Export] public Vector3 SeatOffset { get; set; } = new(0.3f, 0.45f, 0.06f);
    /// <summary>Names of the skeleton's nodes: his head (nods), his chest (sways) and his right palm (holds the rose).</summary>
    [Export] public string HeadNode { get; set; } = "head";
    [Export] public string TorsoNode { get; set; } = "torso";
    [Export] public string HandNode { get; set; } = "hand_r";
    /// <summary>The rose in his right palm (m, in the palm's frame) and its size (it is built at 1/2.4 of life size).</summary>
    [Export] public Vector3 RoseOffset { get; set; } = new(0.0f, -0.02f, 0.0f);
    [Export] public Vector3 RoseRotationDegrees { get; set; } = new(-15.0f, 0.0f, 0.0f);
    [Export] public float RoseScale { get; set; } = 2.4f;
    /// <summary>The song E asks for (the first song whose name holds this; else the first song).</summary>
    [Export] public string SongName { get; set; } = "Reno 4";
    /// <summary>Beats per minute he nods to.</summary>
    [Export] public float NodTempo { get; set; } = 124.0f;
    [Export] public float NodDegrees { get; set; } = 12.0f;
    [Export] public float SwayDegrees { get; set; } = 5.0f;
    /// <summary>E works this far out from the car's sides, in m.</summary>
    [Export] public float ReachMargin { get; set; } = 0.4f;
    /// <summary>Height of the E point above the ground, in m.</summary>
    [Export] public float FocusHeight { get; set; } = 0.9f;
    [Export(PropertyHint.Layers3DPhysics)] public uint CollisionLayer { get; set; } = 1;

    public string Title => "Reno 4";
    public string Description => "An old Renault 4 at the top of the world. A skeleton sits beside it with a rose in his hand; the radio is his.";
    public string Details => DetailsFor(GameEvents.Radio);
    public string Prompt => PromptFor(GameEvents.Radio);
    public Resource? IconSource => null;

    public Node3D? Car => _car;
    public Node3D? Skeleton => _skeleton;
    public Node3D? Rose => _rose;
    public Node3D? Bench => _benchGroup;
    public bool Placed => _placedRoute != null;

    public Vector3 InteractionPoint
    {
        get
        {
            if (_car == null || !Placed)
            {
                return GlobalPosition + Vector3.Up * 1000.0f;
            }
            return NearestPoint(GameEvents.CarrierPosition);
        }
    }

    private RunState? _run;
    private IRouteQuery? _placedRoute;
    private ulong _placedSeed;
    private Node3D? _car;
    private Node3D? _benchGroup;
    private Node3D? _skeleton;
    private Node3D? _head;
    private Node3D? _torso;
    private Node3D? _rose;
    private float _beat;
    private float _groove;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        AddToGroup(IInteractable.Group);
        Build();
    }

    public static string PromptFor(RadioState radio)
    {
        if (radio.Playing)
        {
            return "E  Open the radio";
        }
        return "E  Ask him to play";
    }

    public static string DetailsFor(RadioState radio)
    {
        if (!radio.HasTracks)
        {
            return "No songs on the radio yet: put MP3s in " + radio.Folder;
        }
        if (radio.Playing)
        {
            return "Playing: " + radio.TrackName;
        }
        return radio.Count + " songs on the radio";
    }

    public void SetFocused(bool focused)
    {
    }

    public bool Interact(Node3D interactor)
    {
        if (!GameEvents.Radio.Playing)
        {
            if (!GameEvents.Radio.Started)
            {
                GameEvents.RequestRadioSong(SongName);
            }
            else
            {
                GameEvents.CommandRadio(RadioCommand.Play);
            }
        }
        GameEvents.RequestRadioPage();
        return true;
    }

    private void Build()
    {
        _car = new Node3D { Name = "Reno4" };
        AddChild(_car);
        if (CarScene != null)
        {
            _car.AddChild(CarScene.Instantiate<Node3D>());
        }
        StaticBody3D body = new() { Name = "Body", CollisionLayer = CollisionLayer, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = CarSize },
            Position = new Vector3(0.0f, CarSize.Y * 0.5f, 0.0f),
        });
        _car.AddChild(body);

        _benchGroup = new Node3D { Name = "Bench" };
        AddChild(_benchGroup);
        if (BenchScene != null)
        {
            Node3D bench = BenchScene.Instantiate<Node3D>();
            bench.Scale = Vector3.One * BenchScale;
            _benchGroup.AddChild(bench);
            StandardMaterial3D wood = new() { AlbedoColor = BenchColour, Roughness = 0.92f };
            foreach (Node node in bench.FindChildren("*", "MeshInstance3D", true, false))
            {
                ((MeshInstance3D)node).MaterialOverride = wood;
            }
        }
        if (SkeletonScene != null)
        {
            _skeleton = SkeletonScene.Instantiate<Node3D>();
            _skeleton.Name = "Skeleton";
            _skeleton.Position = SeatOffset;
            _benchGroup.AddChild(_skeleton);
            _head = _skeleton.FindChild(HeadNode, true, false) as Node3D;
            _torso = _skeleton.FindChild(TorsoNode, true, false) as Node3D;
            Node3D? hand = _skeleton.FindChild(HandNode, true, false) as Node3D;
            _rose = MakeRose();
            _rose.Position = RoseOffset;
            _rose.RotationDegrees = RoseRotationDegrees;
            _rose.Scale = Vector3.One * RoseScale;
            if (hand != null)
            {
                hand.AddChild(_rose);
            }
            else
            {
                _skeleton.AddChild(_rose);
            }
        }
        Visible = false;
    }

    /// <summary>A red rose on a green stem with two leaves (built small; RoseScale brings it to life size).</summary>
    private static Node3D MakeRose()
    {
        Node3D rose = new() { Name = "Rose" };
        StandardMaterial3D stemMaterial = new() { AlbedoColor = new Color(0.18f, 0.4f, 0.14f), Roughness = 0.8f };
        StandardMaterial3D petalMaterial = new() { AlbedoColor = new Color(0.62f, 0.03f, 0.07f), Roughness = 0.55f };
        rose.AddChild(new MeshInstance3D
        {
            Name = "Stem",
            Mesh = new CylinderMesh { TopRadius = 0.004f, BottomRadius = 0.005f, Height = 0.16f, RadialSegments = 6 },
            Position = new Vector3(0.0f, 0.05f, 0.0f),
            MaterialOverride = stemMaterial,
        });
        rose.AddChild(new MeshInstance3D
        {
            Name = "Bloom",
            Mesh = new SphereMesh { Radius = 0.026f, Height = 0.042f, RadialSegments = 10, Rings = 6 },
            Position = new Vector3(0.0f, 0.14f, 0.0f),
            MaterialOverride = petalMaterial,
        });
        rose.AddChild(new MeshInstance3D
        {
            Name = "Cup",
            Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.006f, Height = 0.024f, RadialSegments = 8 },
            Position = new Vector3(0.0f, 0.122f, 0.0f),
            MaterialOverride = petalMaterial,
        });
        for (int side = -1; side <= 1; side += 2)
        {
            rose.AddChild(new MeshInstance3D
            {
                Name = "Leaf" + side,
                Mesh = new BoxMesh { Size = new Vector3(0.035f, 0.004f, 0.016f) },
                Position = new Vector3(side * 0.018f, 0.04f + side * 0.012f, 0.0f),
                RotationDegrees = new Vector3(0.0f, 0.0f, side * 25.0f),
                MaterialOverride = stemMaterial,
            });
        }
        return rose;
    }

    public override void _Process(double delta)
    {
        IRouteQuery? route = _run?.Route;
        if (route != null && _run != null && (route != _placedRoute || _run.Seed != _placedSeed))
        {
            Place(route, _run.Seed);
        }
        Groove((float)delta);
    }

    /// <summary>Stands the car and the bench beside the route's start on <paramref name="route"/>'s ground.</summary>
    public void Place(IRouteQuery route, ulong seed)
    {
        _placedRoute = route;
        _placedSeed = seed;
        if (_car == null || _benchGroup == null)
        {
            return;
        }
        Vector3 pad = route.SummitPad;
        Vector3 centre = new(pad.X + CarOffset.X, 0.0f, pad.Z + CarOffset.Y);
        float yaw = Mathf.DegToRad(CarYawDegrees);
        Vector3 along = new(Mathf.Sin(yaw), 0.0f, Mathf.Cos(yaw));
        Vector3 across = new(along.Z, 0.0f, -along.X);
        float halfLength = CarSize.Z * 0.5f;
        float halfWidth = CarSize.X * 0.5f;
        float front = Ground(route, centre + along * halfLength);
        float back = Ground(route, centre - along * halfLength);
        float right = Ground(route, centre + across * halfWidth);
        float left = Ground(route, centre - across * halfWidth);
        Vector3 z = (along * CarSize.Z + Vector3.Up * (front - back)).Normalized();
        Vector3 x = (across * CarSize.X + Vector3.Up * (right - left)).Normalized();
        Vector3 y = z.Cross(x).Normalized();
        x = y.Cross(z).Normalized();
        float ground = Mathf.Min((front + back + right + left) * 0.25f, Ground(route, centre));
        _car.GlobalTransform = new Transform3D(new Basis(x, y, z), new Vector3(centre.X, ground - GroundSink, centre.Z));

        Vector3 benchAt = centre + across * BenchOffset.X + along * BenchOffset.Z;
        benchAt.Y = Ground(route, benchAt) + BenchOffset.Y - GroundSink;
        Basis benchBasis = new Basis(Vector3.Up, yaw + Mathf.DegToRad(BenchYawDegrees));
        _benchGroup.GlobalTransform = new Transform3D(benchBasis, benchAt);
        Visible = true;
    }

    private static float Ground(IRouteQuery route, Vector3 at)
    {
        return route.GroundHeight(at.X, at.Z);
    }

    /// <summary>The point on (or by) the car or the bench nearest <paramref name="from"/>, at waist height.</summary>
    public Vector3 NearestPoint(Vector3 from)
    {
        Vector3 best = GlobalPosition;
        if (_car != null)
        {
            Transform3D frame = _car.GlobalTransform;
            Vector3 local = frame.AffineInverse() * from;
            float halfWidth = CarSize.X * 0.5f + ReachMargin;
            float halfLength = CarSize.Z * 0.5f + ReachMargin;
            Vector3 clamped = new(Mathf.Clamp(local.X, -halfWidth, halfWidth), FocusHeight, Mathf.Clamp(local.Z, -halfLength, halfLength));
            best = frame * clamped;
        }
        if (_benchGroup != null)
        {
            Vector3 bench = _benchGroup.GlobalPosition + Vector3.Up * FocusHeight;
            if (Flat(bench - from) < Flat(best - from))
            {
                best = bench;
            }
        }
        return best;
    }

    private static float Flat(Vector3 offset)
    {
        return new Vector2(offset.X, offset.Z).Length();
    }

    /// <summary>He nods on the beat and sways while a song plays, and settles when it stops.</summary>
    private void Groove(float delta)
    {
        float target = 0.0f;
        if (GameEvents.Radio.Playing)
        {
            target = 1.0f;
        }
        _groove = Mathf.MoveToward(_groove, target, delta * 2.0f);
        _beat += delta * NodTempo / 60.0f * Mathf.Tau;
        if (_beat > Mathf.Tau * 64.0f)
        {
            _beat -= Mathf.Tau * 64.0f;
        }
        if (_head != null)
        {
            float nod = Mathf.Max(0.0f, Mathf.Sin(_beat));
            _head.Rotation = new Vector3(Mathf.DegToRad(NodDegrees) * nod * _groove, 0.0f, 0.0f);
        }
        if (_torso != null)
        {
            _torso.Rotation = new Vector3(0.0f, 0.0f, Mathf.DegToRad(SwayDegrees) * Mathf.Sin(_beat * 0.5f) * _groove);
        }
    }
}
