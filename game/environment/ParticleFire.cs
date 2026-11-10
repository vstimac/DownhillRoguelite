using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// A fire made of particles, for a campfire or a torch's head (Size scales it):
/// - flames: soft additive glows that rise, swell, then shrink and fade from yellow to deep red;
/// - a hot core: small white-yellow ones at the base;
/// - sparks: tiny bright embers flung up and drifting on the wind;
/// - smoke: grey puffs that rise slowly above it and spread.
/// Optionally a flickering light. The particles live in world space, so a moving torch leaves its
/// flame trailing behind it. Burn (0..1) dims and shrinks it as the fuel runs out.
/// </summary>
public partial class ParticleFire : Node3D
{
    /// <summary>1 = a campfire's flames (about 0.8 m tall); a torch is about 0.3.</summary>
    [Export] public float Size { get; set; } = 1.0f;
    [Export] public bool WithLight { get; set; } = true;
    [Export] public Color LightColor { get; set; } = new(1.0f, 0.58f, 0.25f);
    [Export] public float LightEnergy { get; set; } = 2.5f;
    [Export] public float LightRange { get; set; } = 12.0f;
    [Export] public bool LightShadows { get; set; } = true;
    /// <summary>How much the light flickers (share of its energy), and how fast.</summary>
    [Export] public float Flicker { get; set; } = 0.25f;
    [Export] public float FlickerSpeed { get; set; } = 9.0f;
    [Export] public bool Smoke { get; set; } = true;
    [Export] public bool Sparks { get; set; } = true;
    /// <summary>The wind pushing flames, sparks and smoke sideways (m/s², world space).</summary>
    [Export] public Vector3 Wind { get; set; } = new(0.25f, 0.0f, 0.1f);

    /// <summary>The flames' and sparks' particle counts at Size 1 (a torch uses fewer).</summary>
    [Export] public int FlameCount { get; set; } = 60;
    [Export] public int SparkCount { get; set; } = 24;
    [Export] public int SmokeCount { get; set; } = 18;

    public OmniLight3D? Light { get; private set; }
    public CpuParticles3D? Flames { get; private set; }
    public CpuParticles3D? SparkParticles { get; private set; }
    public CpuParticles3D? SmokeParticles { get; private set; }

    /// <summary>How much fuel is left (0..1): below about a fifth the fire shrinks and dims.</summary>
    public float Burn { get; set; } = 1.0f;

    private CpuParticles3D? _core;
    private float _time;
    private static Texture2D? _soft;

    public override void _Ready()
    {
        _time = (float)(GetInstanceId() % 997) * 0.37f;
        _soft ??= SoftDot();
        float s = Mathf.Max(Size, 0.05f);
        int flames = Mathf.Max(8, (int)(FlameCount * Mathf.Sqrt(s)));

        Flames = Layer("Flames", flames, 0.9f, 0.35f, new Vector3(0.18f, 0.05f, 0.18f) * s, 1.1f * s, 0.25f, 0.42f * s,
            FlameRamp(), SwellCurve(), true);
        Flames.Gravity = Vector3.Up * 0.8f * s + Wind * 0.4f;
        Flames.AngleMin = -180.0f;
        Flames.AngleMax = 180.0f;
        Flames.AngularVelocityMin = -40.0f;
        Flames.AngularVelocityMax = 40.0f;
        AddChild(Flames);

        _core = Layer("Core", Mathf.Max(6, flames / 3), 0.45f, 0.3f, new Vector3(0.1f, 0.02f, 0.1f) * s, 0.5f * s, 0.2f, 0.26f * s,
            CoreRamp(), SwellCurve(), true);
        AddChild(_core);

        if (Sparks)
        {
            SparkParticles = Layer("Sparks", Mathf.Max(4, (int)(SparkCount * s)), 1.6f, 0.6f, new Vector3(0.15f, 0.05f, 0.15f) * s, 2.2f * s, 0.6f, 0.025f,
                SparkRamp(), null, true);
            SparkParticles.Gravity = Vector3.Up * 0.3f + Wind;
            SparkParticles.Spread = 25.0f;
            SparkParticles.ScaleAmountMin = 0.6f;
            SparkParticles.ScaleAmountMax = 1.4f;
            AddChild(SparkParticles);
        }
        if (Smoke)
        {
            SmokeParticles = Layer("Smoke", Mathf.Max(4, (int)(SmokeCount * Mathf.Sqrt(s))), 3.2f, 0.3f, new Vector3(0.12f, 0.0f, 0.12f) * s, 0.7f * s, 0.3f, 0.5f * s,
                SmokeRamp(), GrowCurve(), false);
            SmokeParticles.Position = Vector3.Up * 0.55f * s;
            SmokeParticles.Gravity = Vector3.Up * 0.15f + Wind * 1.5f;
            AddChild(SmokeParticles);
        }
        if (WithLight)
        {
            Light = new OmniLight3D
            {
                Name = "Light",
                LightColor = LightColor,
                LightEnergy = LightEnergy,
                OmniRange = LightRange,
                ShadowEnabled = LightShadows,
                Position = Vector3.Up * 0.35f * s,
            };
            AddChild(Light);
        }
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        // Two waves and a quick jitter: a fire's light never settles.
        float wave = Mathf.Sin(_time * FlickerSpeed) * 0.5f + Mathf.Sin(_time * FlickerSpeed * 2.37f + 1.3f) * 0.3f
            + Mathf.Sin(_time * FlickerSpeed * 5.1f + 0.7f) * 0.2f;
        float fuel = Mathf.Clamp(Burn / 0.2f, 0.15f, 1.0f);
        if (Light != null)
        {
            Light.LightEnergy = LightEnergy * fuel * (1.0f + Flicker * wave);
            Light.OmniRange = LightRange * Mathf.Lerp(0.6f, 1.0f, fuel);
        }
        if (Flames != null)
        {
            Flames.ScaleAmountMin = 0.7f * fuel;
            Flames.ScaleAmountMax = 1.25f * fuel;
        }
    }

    /// <summary>Turns the fire on or off (the particles finish their life on the way out).</summary>
    public void SetBurning(bool burning)
    {
        foreach (CpuParticles3D? layer in new[] { Flames, _core, SparkParticles, SmokeParticles })
        {
            if (layer != null)
            {
                layer.Emitting = burning;
            }
        }
        if (Light != null)
        {
            Light.Visible = burning;
        }
    }

    private CpuParticles3D Layer(string name, int amount, float lifetime, float randomness, Vector3 box, float speed, float speedRandom,
        float size, Gradient colours, Curve? scale, bool additive)
    {
        BaseMaterial3D.BlendModeEnum blend = BaseMaterial3D.BlendModeEnum.Mix;
        if (additive)
        {
            blend = BaseMaterial3D.BlendModeEnum.Add;
        }
        StandardMaterial3D material = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = blend,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = _soft,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        CpuParticles3D layer = new()
        {
            Name = name,
            Amount = amount,
            Lifetime = lifetime,
            Randomness = randomness,
            LocalCoords = false,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = box,
            Direction = Vector3.Up,
            Spread = 12.0f,
            InitialVelocityMin = speed * (1.0f - speedRandom),
            InitialVelocityMax = speed,
            DampingMin = 0.4f,
            DampingMax = 1.2f,
            ColorRamp = colours,
            Mesh = new QuadMesh { Size = Vector2.One * size, Material = material },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Preprocess = lifetime,
        };
        if (scale != null)
        {
            layer.ScaleAmountCurve = scale;
        }
        return layer;
    }

    /// <summary>A round soft dot: white in the middle fading to nothing at the edge.</summary>
    private static Texture2D SoftDot()
    {
        Gradient fade = new();
        fade.SetColor(0, new Color(1, 1, 1, 1));
        fade.SetColor(1, new Color(1, 1, 1, 0));
        fade.AddPoint(0.4f, new Color(1, 1, 1, 0.55f));
        return new GradientTexture2D
        {
            Gradient = fade,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.0f, 0.5f),
            Width = 64,
            Height = 64,
        };
    }

    private static Gradient Ramp(params (float At, Color Colour)[] stops)
    {
        Gradient ramp = new();
        ramp.SetOffset(0, stops[0].At);
        ramp.SetColor(0, stops[0].Colour);
        ramp.SetOffset(1, stops[^1].At);
        ramp.SetColor(1, stops[^1].Colour);
        for (int i = 1; i < stops.Length - 1; i++)
        {
            ramp.AddPoint(stops[i].At, stops[i].Colour);
        }
        return ramp;
    }

    private static Gradient FlameRamp()
    {
        return Ramp((0.0f, new Color(1.0f, 0.85f, 0.45f, 0.0f)), (0.1f, new Color(1.0f, 0.75f, 0.3f, 0.85f)),
            (0.45f, new Color(1.0f, 0.42f, 0.1f, 0.6f)), (0.8f, new Color(0.7f, 0.12f, 0.03f, 0.25f)), (1.0f, new Color(0.3f, 0.05f, 0.02f, 0.0f)));
    }

    private static Gradient CoreRamp()
    {
        return Ramp((0.0f, new Color(1.0f, 1.0f, 0.85f, 0.0f)), (0.15f, new Color(1.0f, 0.95f, 0.7f, 0.9f)),
            (0.7f, new Color(1.0f, 0.65f, 0.2f, 0.5f)), (1.0f, new Color(1.0f, 0.4f, 0.1f, 0.0f)));
    }

    private static Gradient SparkRamp()
    {
        return Ramp((0.0f, new Color(1.0f, 0.95f, 0.6f, 1.0f)), (0.5f, new Color(1.0f, 0.55f, 0.15f, 1.0f)),
            (1.0f, new Color(0.8f, 0.2f, 0.05f, 0.0f)));
    }

    private static Gradient SmokeRamp()
    {
        return Ramp((0.0f, new Color(0.25f, 0.23f, 0.22f, 0.0f)), (0.2f, new Color(0.3f, 0.28f, 0.27f, 0.32f)),
            (0.7f, new Color(0.4f, 0.4f, 0.4f, 0.15f)), (1.0f, new Color(0.5f, 0.5f, 0.5f, 0.0f)));
    }

    /// <summary>A flame swells quickly, then thins as it rises.</summary>
    private static Curve SwellCurve()
    {
        Curve curve = new();
        curve.AddPoint(new Vector2(0.0f, 0.35f));
        curve.AddPoint(new Vector2(0.25f, 1.0f));
        curve.AddPoint(new Vector2(1.0f, 0.15f));
        return curve;
    }

    /// <summary>Smoke keeps spreading as it rises.</summary>
    private static Curve GrowCurve()
    {
        Curve curve = new();
        curve.AddPoint(new Vector2(0.0f, 0.3f));
        curve.AddPoint(new Vector2(1.0f, 1.0f));
        return curve;
    }
}
