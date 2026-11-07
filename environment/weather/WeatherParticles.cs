using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// Rain streaks and snowflakes in a box around the active camera, as many as the weather's
/// rain and snow intensity, blown by its wind and dimmed at night. Particles live in world space,
/// so they keep falling where they are while the box follows the camera; the box sits upwind by
/// half a particle's drift so the camera stays in the thick of it.
/// </summary>
public partial class WeatherParticles : Node3D
{
    [ExportGroup("Rain")]
    [Export] public int RainAmount { get; set; } = 4000;
    [Export] public float RainSpeed { get; set; } = 18.0f;
    [Export] public Vector2 RainDropSize { get; set; } = new(0.02f, 0.55f);
    [Export] public Color RainColor { get; set; } = new(0.75f, 0.8f, 0.9f, 0.35f);
    /// <summary>How much of the wind velocity the drops take on.</summary>
    [Export] public float RainWindFactor { get; set; } = 0.8f;

    [ExportGroup("Snow")]
    [Export] public int SnowAmount { get; set; } = 3000;
    [Export] public float SnowSpeed { get; set; } = 1.4f;
    [Export] public float SnowFlakeSize { get; set; } = 0.1f;
    [Export] public Color SnowColor { get; set; } = new(0.95f, 0.96f, 1.0f, 0.9f);
    [Export] public float SnowWindFactor { get; set; } = 0.9f;

    [ExportGroup("Area")]
    /// <summary>Half size of the box particles are born in around the camera, in m.</summary>
    [Export] public Vector3 Extents { get; set; } = new(16.0f, 6.0f, 16.0f);
    /// <summary>Height of the box centre above the camera, in m.</summary>
    [Export] public float Height { get; set; } = 6.0f;
    /// <summary>Particle brightness at full night.</summary>
    [Export] public float NightBrightness { get; set; } = 0.15f;

    public GpuParticles3D? Rain => _rain;
    public GpuParticles3D? Snow => _snow;

    private GpuParticles3D? _rain;
    private GpuParticles3D? _snow;
    private ParticleProcessMaterial? _rainProcess;
    private ParticleProcessMaterial? _snowProcess;
    private StandardMaterial3D? _rainMaterial;
    private StandardMaterial3D? _snowMaterial;
    private WorldClock? _clock;

    public override void _Ready()
    {
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        TopLevel = true;

        _rainMaterial = Material(RainColor, Streak());
        _rainProcess = Process(RainSpeed, 2.0f);
        float rainLife = (Extents.Y * 2.0f + Height) / RainSpeed;
        _rain = Emitter(RainAmount, rainLife, _rainProcess, new QuadMesh { Size = RainDropSize, Material = _rainMaterial });
        _rain.TransformAlign = GpuParticles3D.TransformAlignEnum.ZBillboardYToVelocity;

        _snowMaterial = Material(SnowColor, Flake());
        _snowProcess = Process(SnowSpeed, 25.0f);
        _snowProcess.TurbulenceEnabled = true;
        _snowProcess.TurbulenceNoiseStrength = 0.6f;
        _snowProcess.TurbulenceNoiseScale = 3.0f;
        float snowLife = (Extents.Y * 2.0f + Height) / SnowSpeed;
        _snow = Emitter(SnowAmount, snowLife, _snowProcess, new QuadMesh { Size = Vector2.One * SnowFlakeSize, Material = _snowMaterial });
        _snow.TransformAlign = GpuParticles3D.TransformAlignEnum.ZBillboard;
    }

    public override void _Process(double delta)
    {
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera != null)
        {
            GlobalPosition = camera.GlobalPosition + Vector3.Up * Height;
        }

        WeatherSnapshot weather = GameEvents.CurrentWeather;
        float brightness = 1.0f;
        if (_clock != null)
        {
            brightness = Mathf.Lerp(NightBrightness, 1.0f, _clock.Daylight);
        }
        Update(_rain, _rainProcess, _rainMaterial, RainColor, weather.Rain, RainSpeed, weather.Wind * RainWindFactor, brightness);
        Update(_snow, _snowProcess, _snowMaterial, SnowColor, weather.Snow, SnowSpeed, weather.Wind * SnowWindFactor, brightness);
    }

    private static void Update(GpuParticles3D? emitter, ParticleProcessMaterial? process, StandardMaterial3D? material,
        Color color, float intensity, float fallSpeed, Vector3 wind, float brightness)
    {
        if (emitter == null || process == null || material == null)
        {
            return;
        }
        bool on = intensity > 0.01f;
        if (emitter.Emitting != on)
        {
            emitter.Emitting = on;
        }
        emitter.AmountRatio = Mathf.Clamp(intensity, 0.0f, 1.0f);
        // Particles fall at their own speed and drift with the wind from birth.
        Vector3 velocity = new Vector3(wind.X, 0.0f, wind.Z) + Vector3.Down * fallSpeed;
        process.Direction = velocity.Normalized();
        process.InitialVelocityMin = velocity.Length() * 0.9f;
        process.InitialVelocityMax = velocity.Length() * 1.1f;
        emitter.Position = -new Vector3(wind.X, 0.0f, wind.Z) * (float)emitter.Lifetime * 0.5f;
        Color lit = new(color.R * brightness, color.G * brightness, color.B * brightness, color.A);
        material.AlbedoColor = lit;
    }

    private ParticleProcessMaterial Process(float speed, float spread)
    {
        return new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = Extents,
            Direction = Vector3.Down,
            Spread = spread,
            InitialVelocityMin = speed * 0.9f,
            InitialVelocityMax = speed * 1.1f,
            Gravity = Vector3.Zero,
        };
    }

    private static StandardMaterial3D Material(Color color, Texture2D texture)
    {
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = color,
            AlbedoTexture = texture,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled,
        };
    }

    /// <summary>A soft round flake: opaque centre fading to a clear edge.</summary>
    private static Texture2D Flake()
    {
        Gradient gradient = new()
        {
            Offsets = new[] { 0.0f, 0.45f, 1.0f },
            Colors = new[] { Colors.White, new Color(1.0f, 1.0f, 1.0f, 0.7f), new Color(1.0f, 1.0f, 1.0f, 0.0f) },
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(0.5f, 0.0f),
            Width = 32,
            Height = 32,
        };
    }

    /// <summary>A rain streak: clear at both ends, strongest in the middle.</summary>
    private static Texture2D Streak()
    {
        Gradient gradient = new()
        {
            Offsets = new[] { 0.0f, 0.5f, 1.0f },
            Colors = new[] { new Color(1.0f, 1.0f, 1.0f, 0.0f), Colors.White, new Color(1.0f, 1.0f, 1.0f, 0.0f) },
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            FillFrom = new Vector2(0.5f, 0.0f),
            FillTo = new Vector2(0.5f, 1.0f),
            Width = 4,
            Height = 32,
        };
    }

    private GpuParticles3D Emitter(int amount, float lifetime, ParticleProcessMaterial process, Mesh mesh)
    {
        GpuParticles3D emitter = new()
        {
            Amount = amount,
            Lifetime = lifetime,
            Preprocess = lifetime,
            LocalCoords = false,
            Emitting = false,
            ProcessMaterial = process,
            DrawPass1 = mesh,
            VisibilityAabb = new Aabb(-Extents * 2.0f - Vector3.Up * Height * 2.0f, Extents * 4.0f + Vector3.Up * Height * 4.0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(emitter);
        return emitter;
    }
}
