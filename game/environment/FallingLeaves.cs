using DownhillRoguelite.Core;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// Leaves drifting down round the camera where the circle's trees shed them (Biome.LeafFall: thick
/// in the autumn circles, a few in the green ones), tumbling and fluttering and blown along by the
/// wind. Lit like the world, so they darken at night. TerrainStreamer adds it.
/// </summary>
public partial class FallingLeaves : Node3D
{
    public TerrainStreamer? Streamer { get; set; }
    /// <summary>Leaves at full fall (LeafFall 1).</summary>
    [Export] public int Amount { get; set; } = 900;
    [Export] public float Lifetime { get; set; } = 11.0f;
    /// <summary>Half size of the box leaves start in, round a point above the camera, in m.</summary>
    [Export] public Vector3 Extents { get; set; } = new(14.0f, 4.0f, 14.0f);
    [Export] public float Height { get; set; } = 6.0f;
    /// <summary>How fast a leaf sinks, in m/s.</summary>
    [Export] public float FallSpeed { get; set; } = 0.9f;
    /// <summary>How much of the wind's speed a leaf takes on.</summary>
    [Export] public float WindFactor { get; set; } = 0.55f;
    [Export] public Vector2 LeafSize { get; set; } = new(0.14f, 0.1f);
    /// <summary>The fall eases toward the circle's over this many seconds.</summary>
    [Export] public float Easing { get; set; } = 3.0f;
    /// <summary>How often the circle under the camera is looked up, in s.</summary>
    [Export] public float CheckEvery { get; set; } = 0.5f;

    public GpuParticles3D? Emitter => _emitter;
    /// <summary>How thick the fall is now (0 none, 1 full; tests).</summary>
    public float Fall => _fall;

    private GpuParticles3D? _emitter;
    private ParticleProcessMaterial? _process;
    private float _fall;
    private float _wanted;
    private Color _colour = new(0.8f, 0.45f, 0.15f);
    private float _check;
    private bool _first = true;

    public override void _Ready()
    {
        TopLevel = true;
        _process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = Extents,
            Direction = Vector3.Down,
            Spread = 20.0f,
            InitialVelocityMin = FallSpeed * 0.8f,
            InitialVelocityMax = FallSpeed * 1.2f,
            Gravity = Vector3.Zero,
            // Tumbling: each leaf turns its own way, some fast, some slow.
            AngleMin = -180.0f,
            AngleMax = 180.0f,
            AngularVelocityMin = -160.0f,
            AngularVelocityMax = 160.0f,
            ScaleMin = 0.7f,
            ScaleMax = 1.3f,
            HueVariationMin = -0.04f,
            HueVariationMax = 0.04f,
            // Fluttering: they swirl a little on the way down.
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 1.2f,
            TurbulenceNoiseScale = 4.0f,
            TurbulenceNoiseSpeedRandom = 0.4f,
            TurbulenceInfluenceMin = 0.05f,
            TurbulenceInfluenceMax = 0.15f,
            Color = _colour,
        };
        StandardMaterial3D material = new()
        {
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            Roughness = 0.9f,
        };
        _emitter = new GpuParticles3D
        {
            Name = "Leaves",
            Amount = Amount,
            Lifetime = Lifetime,
            Preprocess = Lifetime,
            LocalCoords = false,
            Emitting = false,
            ProcessMaterial = _process,
            DrawPass1 = new QuadMesh { Size = LeafSize, Material = material },
            VisibilityAabb = new Aabb(-Extents * 2.0f - Vector3.Up * Height * 3.0f, Extents * 4.0f + Vector3.Up * Height * 4.0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            AmountRatio = 0.0f,
        };
        AddChild(_emitter);
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            // `--leaves`: a full fall wherever you are (for looking at it).
            if (arg == "--leaves")
            {
                Force = 1.0f;
            }
        }
    }

    /// <summary>Overrides the circle's fall (tests, `--leaves`); negative: the circle decides.</summary>
    public float Force { get; set; } = -1.0f;

    public override void _Process(double delta)
    {
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera == null || _emitter == null || _process == null)
        {
            return;
        }
        _check -= (float)delta;
        if (_check <= 0.0f)
        {
            _check = CheckEvery;
            LookUp(camera.GlobalPosition);
            if (_first)
            {
                // Arriving in a wood (a new run, a station start): the leaves are already falling.
                _first = false;
                _fall = _wanted;
            }
        }
        _fall = Mathf.MoveToward(_fall, _wanted, (float)delta / Mathf.Max(Easing, 0.01f));
        bool on = _fall > 0.01f;
        if (_emitter.Emitting != on)
        {
            _emitter.Emitting = on;
        }
        if (!on)
        {
            return;
        }
        // Only what changed is written (every write re-uploads the material).
        if (Mathf.Abs(_emitter.AmountRatio - _fall) > 0.02f)
        {
            _emitter.AmountRatio = _fall;
        }
        Vector3 wind = GameEvents.CurrentWeather.Wind * WindFactor;
        wind.Y = 0.0f;
        Vector3 velocity = wind + Vector3.Down * FallSpeed;
        Vector3 direction = velocity.Normalized();
        if (_process.Direction.DistanceTo(direction) > 0.02f)
        {
            _process.Direction = direction;
            float speed = velocity.Length();
            _process.InitialVelocityMin = speed * 0.8f;
            _process.InitialVelocityMax = speed * 1.2f;
        }
        if (!_process.Color.IsEqualApprox(_colour))
        {
            _process.Color = _colour;
        }
        // The box sits upwind by half a leaf's drift, so the camera stays among them.
        GlobalPosition = camera.GlobalPosition + Vector3.Up * Height - wind * Lifetime * 0.5f;
    }

    private void LookUp(Vector3 at)
    {
        if (Force >= 0.0f)
        {
            _wanted = Force;
            return;
        }
        _wanted = 0.0f;
        TerrainGenerator? generator = Streamer?.Generator;
        if (generator == null || Streamer == null)
        {
            return;
        }
        float depth = Mathf.Max(0.0f, Streamer.SpawnPoint.Y - at.Y);
        Biome? biome = generator.BiomeAt(at.X, at.Z, depth);
        if (biome == null)
        {
            return;
        }
        _wanted = Mathf.Clamp(biome.LeafFall, 0.0f, 1.0f);
        _colour = biome.LeafColour;
    }
}
