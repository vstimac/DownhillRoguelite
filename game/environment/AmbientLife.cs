using System;
using DownhillRoguelite.Core;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// The small life of the mountain round the camera, all of it cheap: a few flocks of birds wheeling
/// high over the slope (one MultiMesh, wings beating in the shader), pollen and dust drifting in the
/// light, insects over the green circles' grass, and soft banks of mist lying in the distance. Birds,
/// pollen and insects keep in by night and in rain or snow; the mist thickens in the cold circles.
/// It only decorates: nothing collides, and it uses its own random numbers, so the run's seeded
/// streams never move. TerrainStreamer adds it.
/// </summary>
public partial class AmbientLife : Node3D
{
    public TerrainStreamer? Streamer { get; set; }
    [Export] public int Flocks { get; set; } = 3;
    [Export] public int BirdsPerFlock { get; set; } = 7;
    /// <summary>Flocks circle this high above the ground (range), in m.</summary>
    [Export] public Vector2 FlockHeight { get; set; } = new(16.0f, 40.0f);
    /// <summary>...round a point this far from the camera (range), in m.</summary>
    [Export] public Vector2 FlockDistance { get; set; } = new(25.0f, 90.0f);
    [Export] public float BirdSpan { get; set; } = 0.55f;
    [Export] public int Motes { get; set; } = 140;
    [Export] public int Insects { get; set; } = 36;
    [Export] public int MistBanks { get; set; } = 9;
    /// <summary>Mist banks lie this far from the camera (range), in m.</summary>
    [Export] public Vector2 MistDistance { get; set; } = new(35.0f, 120.0f);
    [Export] public float MistOpacity { get; set; } = 0.22f;
    [Export] public float CheckEvery { get; set; } = 0.5f;
    /// <summary>Share shown at Low, Medium and High graphics quality.</summary>
    [Export] public float[] QualityShare { get; set; } = { 0.4f, 0.75f, 1.0f };

    /// <summary>How much daytime life is out now (0 none, 1 all; tests).</summary>
    public float DayLife => _day;
    public MultiMeshInstance3D? Birds => _birds;

    private readonly Random _random = new(0x51F7);
    private MultiMeshInstance3D? _birds;
    private GpuParticles3D? _motes;
    private GpuParticles3D? _insects;
    private MeshInstance3D[] _mist = Array.Empty<MeshInstance3D>();
    private Flock[] _flocks = Array.Empty<Flock>();
    private float[] _birdOffsets = Array.Empty<float>();
    private WorldClock? _clock;
    private float _check;
    private float _day;
    private float _dayWanted;
    private float _green;
    private float _cold;
    private bool _first = true;

    private struct Flock
    {
        public Vector3 Centre;
        public float Radius;
        public float Angle;
        public float Speed;
        public float Height;
    }

    public override void _Ready()
    {
        TopLevel = true;
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        BuildBirds();
        _motes = Particles("Motes", Motes, 9.0f, new Vector3(14.0f, 4.0f, 14.0f), 0.03f, new Color(1.0f, 0.97f, 0.85f, 0.55f), 0.6f, 0.08f);
        _insects = Particles("Insects", Insects, 4.0f, new Vector3(7.0f, 0.7f, 7.0f), 0.05f, new Color(0.15f, 0.12f, 0.1f, 0.9f), 2.5f, 0.6f);
        BuildMist();
    }

    public override void _Process(double delta)
    {
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera == null)
        {
            return;
        }
        float dt = (float)delta;
        Vector3 eye = camera.GlobalPosition;
        _check -= dt;
        if (_check <= 0.0f)
        {
            _check = CheckEvery;
            Survey(eye);
        }
        _day = Mathf.MoveToward(_day, _dayWanted, dt / 3.0f);
        float share = QualityScale();
        SetAmount(_motes, _day * share, eye + Vector3.Down * 0.5f);
        SetAmount(_insects, _day * _green * share, eye + Vector3.Down * 1.4f);
        MoveBirds(eye, dt, _day > 0.05f);
        MoveMist(eye);
    }

    /// <summary>What the hour, the weather and the circle allow now.</summary>
    private void Survey(Vector3 eye)
    {
        float daylight = 1.0f;
        if (_clock != null)
        {
            daylight = _clock.Daylight;
        }
        WeatherSnapshot weather = GameEvents.CurrentWeather;
        float wet = Mathf.Max(weather.Rain, weather.Snow);
        _dayWanted = Mathf.Clamp((daylight - 0.25f) * 3.0f, 0.0f, 1.0f) * (1.0f - Mathf.Clamp(wet * 2.5f, 0.0f, 1.0f));
        _green = 0.0f;
        _cold = 0.3f;
        TerrainGenerator? generator = Streamer?.Generator;
        if (generator != null && Streamer != null)
        {
            Biome? biome = generator.BiomeAt(eye.X, eye.Z, Mathf.Max(0.0f, Streamer.SpawnPoint.Y - eye.Y));
            if (biome != null)
            {
                _green = Mathf.Clamp(biome.MeadowFlowerDensity * 4.0f + biome.FlowerDensity * 10.0f, 0.0f, 1.0f);
                if (biome.Name == "Limbo" || biome.Name == "Fraud" || biome.Name == "Treachery")
                {
                    _cold = 1.0f;
                }
            }
        }
        if (_first)
        {
            _first = false;
            _day = _dayWanted;
        }
    }

    private void SetAmount(GpuParticles3D? emitter, float amount, Vector3 at)
    {
        if (emitter == null)
        {
            return;
        }
        bool on = amount > 0.02f;
        if (emitter.Emitting != on)
        {
            emitter.Emitting = on;
        }
        if (!on)
        {
            return;
        }
        if (Mathf.Abs(emitter.AmountRatio - amount) > 0.03f)
        {
            emitter.AmountRatio = amount;
        }
        emitter.GlobalPosition = at;
    }

    private GpuParticles3D Particles(string name, int amount, float lifetime, Vector3 extents, float size, Color colour, float wander, float speed)
    {
        Gradient fade = new()
        {
            Offsets = new[] { 0.0f, 0.2f, 0.8f, 1.0f },
            Colors = new[] { new Color(1, 1, 1, 0), Colors.White, Colors.White, new Color(1, 1, 1, 0) },
        };
        ParticleProcessMaterial process = new()
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = extents,
            Direction = Vector3.Up,
            Spread = 180.0f,
            InitialVelocityMin = speed * 0.3f,
            InitialVelocityMax = speed,
            Gravity = Vector3.Zero,
            TurbulenceEnabled = true,
            TurbulenceNoiseStrength = wander,
            TurbulenceNoiseScale = 3.0f,
            TurbulenceInfluenceMin = 0.04f,
            TurbulenceInfluenceMax = 0.15f,
            ScaleMin = 0.6f,
            ScaleMax = 1.4f,
            ColorRamp = new GradientTexture1D { Gradient = fade },
        };
        Gradient dot = new()
        {
            Offsets = new[] { 0.0f, 0.5f, 1.0f },
            Colors = new[] { Colors.White, new Color(1, 1, 1, 0.6f), new Color(1, 1, 1, 0) },
        };
        StandardMaterial3D material = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = colour,
            AlbedoTexture = new GradientTexture2D
            {
                Gradient = dot,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(0.5f, 0.0f),
                Width = 16,
                Height = 16,
            },
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        GpuParticles3D emitter = new()
        {
            Name = name,
            Amount = amount,
            Lifetime = lifetime,
            Preprocess = lifetime,
            LocalCoords = false,
            Emitting = false,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = Vector2.One * size, Material = material },
            VisibilityAabb = new Aabb(-extents * 2.0f, extents * 4.0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            AmountRatio = 0.0f,
        };
        AddChild(emitter);
        return emitter;
    }

    /// <summary>A bird is a dark V: a small body and two wings that beat in the shader (by each one's own phase).</summary>
    private void BuildBirds()
    {
        float half = BirdSpan * 0.5f;
        Vector3[] vertices =
        {
            // Left wing, right wing (inner edge at the body, tip out), then the body.
            new(0.0f, 0.0f, -0.08f), new(-half, 0.02f, 0.04f), new(0.0f, 0.0f, 0.08f),
            new(0.0f, 0.0f, -0.08f), new(0.0f, 0.0f, 0.08f), new(half, 0.02f, 0.04f),
            new(0.0f, 0.0f, -0.16f), new(-0.03f, 0.0f, 0.12f), new(0.03f, 0.0f, 0.12f),
        };
        Vector3[] normals = new Vector3[vertices.Length];
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = Vector3.Up;
        }
        using Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        Shader shader = new()
        {
            Code = @"shader_type spatial;
render_mode unshaded, cull_disabled;
uniform vec4 colour : source_color = vec4(0.12, 0.12, 0.13, 1.0);
void vertex() {
	float phase = INSTANCE_CUSTOM.x * 6.2831;
	float beat = sin(TIME * (7.0 + INSTANCE_CUSTOM.y * 3.0) + phase);
	// Glide now and then: the beat rests while the slow wave is low.
	float glide = smoothstep(-0.2, 0.4, sin(TIME * 0.6 + phase * 2.0));
	VERTEX.y += abs(VERTEX.x) * beat * 0.9 * glide;
}
void fragment() {
	ALBEDO = colour.rgb;
}
",
        };
        int count = Mathf.Max(Flocks * BirdsPerFlock, 1);
        MultiMesh multimesh = new()
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = mesh,
            InstanceCount = count,
        };
        _birdOffsets = new float[count * 3];
        for (int i = 0; i < count; i++)
        {
            multimesh.SetInstanceCustomData(i, new Color((float)_random.NextDouble(), (float)_random.NextDouble(), 0.0f, 0.0f));
            _birdOffsets[i * 3] = (float)(_random.NextDouble() * 2.0 - 1.0) * 5.0f;
            _birdOffsets[i * 3 + 1] = (float)(_random.NextDouble() * 2.0 - 1.0) * 1.5f;
            _birdOffsets[i * 3 + 2] = (float)(_random.NextDouble() * 2.0 - 1.0) * 5.0f;
        }
        _birds = new MultiMeshInstance3D
        {
            Name = "Birds",
            Multimesh = multimesh,
            MaterialOverride = new ShaderMaterial { Shader = shader },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-400.0f, -200.0f, -400.0f), new Vector3(800.0f, 400.0f, 800.0f)),
            Visible = false,
        };
        AddChild(_birds);
        _flocks = new Flock[Mathf.Max(Flocks, 0)];
    }

    private void MoveBirds(Vector3 eye, float dt, bool flying)
    {
        if (_birds?.Multimesh == null)
        {
            return;
        }
        if (_birds.Visible != flying)
        {
            _birds.Visible = flying;
        }
        if (!flying)
        {
            return;
        }
        MultiMesh multimesh = _birds.Multimesh;
        for (int f = 0; f < _flocks.Length; f++)
        {
            ref Flock flock = ref _flocks[f];
            Vector2 offset = new(flock.Centre.X - eye.X, flock.Centre.Z - eye.Z);
            if (flock.Radius <= 0.0f || offset.Length() > FlockDistance.Y * 1.6f)
            {
                PlaceFlock(ref flock, eye);
            }
            flock.Angle += flock.Speed * dt;
            Vector3 centre = flock.Centre + new Vector3(Mathf.Cos(flock.Angle) * flock.Radius, flock.Height, Mathf.Sin(flock.Angle) * flock.Radius);
            Vector3 heading = new Vector3(-Mathf.Sin(flock.Angle), 0.0f, Mathf.Cos(flock.Angle)) * Mathf.Sign(flock.Speed);
            Basis facing = Basis.LookingAt(heading, Vector3.Up);
            for (int b = 0; b < BirdsPerFlock; b++)
            {
                int i = f * BirdsPerFlock + b;
                if (i >= multimesh.InstanceCount)
                {
                    break;
                }
                float sway = Mathf.Sin(flock.Angle * 3.0f + b) * 0.8f;
                Vector3 local = new(_birdOffsets[i * 3] + sway, _birdOffsets[i * 3 + 1], _birdOffsets[i * 3 + 2]);
                multimesh.SetInstanceTransform(i, new Transform3D(facing, centre + facing * local));
            }
        }
    }

    private void PlaceFlock(ref Flock flock, Vector3 eye)
    {
        float angle = (float)(_random.NextDouble() * Mathf.Tau);
        float distance = Mathf.Lerp(FlockDistance.X, FlockDistance.Y, (float)_random.NextDouble());
        Vector3 at = eye + new Vector3(Mathf.Cos(angle) * distance, 0.0f, Mathf.Sin(angle) * distance);
        float ground = eye.Y - 2.0f;
        TerrainGenerator? generator = Streamer?.Generator;
        if (generator != null)
        {
            ground = generator.Height(at.X, at.Z);
        }
        flock.Centre = new Vector3(at.X, ground, at.Z);
        flock.Radius = Mathf.Lerp(12.0f, 30.0f, (float)_random.NextDouble());
        flock.Height = Mathf.Lerp(FlockHeight.X, FlockHeight.Y, (float)_random.NextDouble());
        flock.Angle = (float)(_random.NextDouble() * Mathf.Tau);
        float speed = Mathf.Lerp(0.12f, 0.25f, (float)_random.NextDouble());
        if (_random.NextDouble() < 0.5)
        {
            speed = -speed;
        }
        flock.Speed = speed;
    }

    /// <summary>Soft banks of mist: big billboards that melt into the ground and fade away as one comes close.</summary>
    private void BuildMist()
    {
        Gradient soft = new()
        {
            Offsets = new[] { 0.0f, 0.45f, 1.0f },
            Colors = new[] { Colors.White, new Color(1, 1, 1, 0.45f), new Color(1, 1, 1, 0) },
        };
        StandardMaterial3D material = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.86f, 0.88f, 0.9f, MistOpacity),
            AlbedoTexture = new GradientTexture2D
            {
                Gradient = soft,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(0.5f, 0.0f),
                Width = 64,
                Height = 64,
            },
            BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
            ProximityFadeEnabled = true,
            ProximityFadeDistance = 3.0f,
            DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha,
            DistanceFadeMinDistance = 12.0f,
            DistanceFadeMaxDistance = 30.0f,
            DisableReceiveShadows = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _mist = new MeshInstance3D[Mathf.Max(MistBanks, 0)];
        for (int i = 0; i < _mist.Length; i++)
        {
            float width = Mathf.Lerp(14.0f, 26.0f, (float)_random.NextDouble());
            _mist[i] = new MeshInstance3D
            {
                Name = "Mist",
                Mesh = new QuadMesh { Size = new Vector2(width, width * 0.35f), Material = material },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            AddChild(_mist[i]);
        }
    }

    private void MoveMist(Vector3 eye)
    {
        TerrainGenerator? generator = Streamer?.Generator;
        if (generator == null)
        {
            return;
        }
        int shown = Mathf.RoundToInt(_mist.Length * Mathf.Lerp(0.5f, 1.0f, _cold) * QualityScale());
        for (int i = 0; i < _mist.Length; i++)
        {
            MeshInstance3D bank = _mist[i];
            bool wanted = i < shown;
            if (!wanted)
            {
                bank.Visible = false;
                continue;
            }
            Vector3 offset = bank.GlobalPosition - eye;
            float reach = new Vector2(offset.X, offset.Z).Length();
            if (!bank.Visible || reach > MistDistance.Y * 1.3f)
            {
                // Lie in a hollow: of a few spots round the camera, the one lowest against the slope.
                Vector3 best = eye;
                float lowest = float.MaxValue;
                for (int k = 0; k < 4; k++)
                {
                    float angle = (float)(_random.NextDouble() * Mathf.Tau);
                    float distance = Mathf.Lerp(MistDistance.X, MistDistance.Y, (float)_random.NextDouble());
                    float x = eye.X + Mathf.Cos(angle) * distance;
                    float z = eye.Z + Mathf.Sin(angle) * distance;
                    float y = generator.Height(x, z);
                    float relative = y - eye.Y - (z - eye.Z) * 0.36f;
                    if (relative < lowest)
                    {
                        lowest = relative;
                        best = new Vector3(x, y, z);
                    }
                }
                QuadMesh? quad = bank.Mesh as QuadMesh;
                float lift = 1.5f;
                if (quad != null)
                {
                    lift = quad.Size.Y * 0.35f;
                }
                bank.GlobalPosition = best + Vector3.Up * lift;
                bank.Visible = true;
            }
        }
    }

    private float QualityScale()
    {
        int quality = (int)GameOptions.Quality;
        if (quality < 0 || quality >= QualityShare.Length)
        {
            return 1.0f;
        }
        return Mathf.Clamp(QualityShare[quality], 0.0f, 1.0f);
    }
}
