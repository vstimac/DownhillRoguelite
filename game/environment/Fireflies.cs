using DownhillRoguelite.Core;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// Fireflies on dry nights in the green circles (Biome.Fireflies): small lights wandering low over
/// the ground round the camera, each glowing up and fading out again. Gone by day and in rain or
/// snow. They only glow (no real light), so they cost next to nothing. TerrainStreamer adds it.
/// </summary>
public partial class Fireflies : Node3D
{
    public TerrainStreamer? Streamer { get; set; }
    /// <summary>Fireflies at full strength (Biome.Fireflies 1).</summary>
    [Export] public int Amount { get; set; } = 160;
    [Export] public float Lifetime { get; set; } = 5.0f;
    /// <summary>Half size of the box they wander in, round a point by the camera, in m.</summary>
    [Export] public Vector3 Extents { get; set; } = new(16.0f, 1.2f, 16.0f);
    /// <summary>The box's centre this far below the camera, in m (they keep near the ground).</summary>
    [Export] public float BelowCamera { get; set; } = 1.2f;
    [Export] public float Size { get; set; } = 0.07f;
    [Export] public Color Glow { get; set; } = new(0.85f, 1.0f, 0.35f);
    /// <summary>They come out below this daylight (0 night, 1 day).</summary>
    [Export] public float DarkBelow { get; set; } = 0.3f;
    /// <summary>Rain or snow above this keeps them in.</summary>
    [Export] public float WetAbove { get; set; } = 0.1f;
    [Export] public float Easing { get; set; } = 4.0f;
    [Export] public float CheckEvery { get; set; } = 0.5f;

    public GpuParticles3D? Emitter => _emitter;
    /// <summary>How many are out now (0 none, 1 all; tests).</summary>
    public float Strength => _strength;
    /// <summary>Overrides the circle and the hour (tests, `--fireflies`); negative: they decide.</summary>
    public float Force { get; set; } = -1.0f;

    private GpuParticles3D? _emitter;
    private float _strength;
    private float _wanted;
    private float _check;
    private bool _first = true;
    private WorldClock? _clock;

    public override void _Ready()
    {
        TopLevel = true;
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        // Each one fades in, glows and fades out over its life.
        Gradient blink = new()
        {
            Offsets = new[] { 0.0f, 0.25f, 0.5f, 0.75f, 1.0f },
            Colors = new[] { new Color(1, 1, 1, 0), new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.35f), new Color(1, 1, 1, 1), new Color(1, 1, 1, 0) },
        };
        ParticleProcessMaterial process = new()
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = Extents,
            Direction = Vector3.Up,
            Spread = 180.0f,
            InitialVelocityMin = 0.1f,
            InitialVelocityMax = 0.4f,
            Gravity = Vector3.Zero,
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = 2.0f,
            TurbulenceNoiseScale = 2.5f,
            TurbulenceInfluenceMin = 0.05f,
            TurbulenceInfluenceMax = 0.12f,
            ScaleMin = 0.7f,
            ScaleMax = 1.3f,
            ColorRamp = new GradientTexture1D { Gradient = blink },
        };
        Gradient dot = new()
        {
            Offsets = new[] { 0.0f, 0.35f, 1.0f },
            Colors = new[] { Colors.White, new Color(1, 1, 1, 0.5f), new Color(1, 1, 1, 0) },
        };
        StandardMaterial3D material = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = Glow,
            AlbedoTexture = new GradientTexture2D
            {
                Gradient = dot,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(0.5f, 0.0f),
                Width = 32,
                Height = 32,
            },
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _emitter = new GpuParticles3D
        {
            Name = "Fireflies",
            Amount = Amount,
            Lifetime = Lifetime,
            Preprocess = Lifetime,
            LocalCoords = false,
            Emitting = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = Vector2.One * Size, Material = material },
            VisibilityAabb = new Aabb(-Extents * 2.0f, Extents * 4.0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            AmountRatio = 0.0f,
        };
        AddChild(_emitter);
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            // `--fireflies`: all of them, wherever and whenever (for looking at them).
            if (arg == "--fireflies")
            {
                Force = 1.0f;
            }
        }
    }

    public override void _Process(double delta)
    {
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera == null || _emitter == null)
        {
            return;
        }
        _check -= (float)delta;
        if (_check <= 0.0f)
        {
            _check = CheckEvery;
            _wanted = Wanted(camera.GlobalPosition);
            if (_first)
            {
                _first = false;
                _strength = _wanted;
            }
        }
        _strength = Mathf.MoveToward(_strength, _wanted, (float)delta / Mathf.Max(Easing, 0.01f));
        bool on = _strength > 0.01f;
        if (_emitter.Emitting != on)
        {
            _emitter.Emitting = on;
        }
        if (!on)
        {
            return;
        }
        if (Mathf.Abs(_emitter.AmountRatio - _strength) > 0.02f)
        {
            _emitter.AmountRatio = _strength;
        }
        GlobalPosition = camera.GlobalPosition + Vector3.Down * BelowCamera;
    }

    private float Wanted(Vector3 at)
    {
        if (Force >= 0.0f)
        {
            return Force;
        }
        WeatherSnapshot weather = GameEvents.CurrentWeather;
        if (weather.Rain > WetAbove || weather.Snow > WetAbove)
        {
            return 0.0f;
        }
        float daylight = 1.0f;
        if (_clock != null)
        {
            daylight = _clock.Daylight;
        }
        if (daylight >= DarkBelow)
        {
            return 0.0f;
        }
        TerrainGenerator? generator = Streamer?.Generator;
        if (generator == null || Streamer == null)
        {
            return 0.0f;
        }
        Biome? biome = generator.BiomeAt(at.X, at.Z, Mathf.Max(0.0f, Streamer.SpawnPoint.Y - at.Y));
        if (biome == null)
        {
            return 0.0f;
        }
        // Darker is more: they come out at dusk and are all out at full night.
        float dark = Mathf.Clamp((DarkBelow - daylight) / Mathf.Max(DarkBelow, 0.01f) * 2.0f, 0.0f, 1.0f);
        return Mathf.Clamp(biome.Fireflies, 0.0f, 1.0f) * dark;
    }
}
