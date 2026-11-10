using Godot;

namespace DownhillRoguelite.Environment.Fog;

/// <summary>
/// Draws the simulated fog: a box around the patch (from just under its lowest ground to above
/// its highest fog) with ground_fog.gdshader ray-marching through it. Each simulation step is
/// uploaded as a float texture (density, flow X, flow Z, sampled) and the two newest are kept, so
/// the shader blends between steps at the frame rate (no stepping at 30 Hz); the ground heights
/// go up as a second texture. Wisps come from a tiling 3D noise texture made once.
/// </summary>
public partial class GroundFogRenderer : MeshInstance3D
{
    public const string ShaderPath = "res://environment/fog/ground_fog.gdshader";

    /// <summary>Uploads so far (one per simulation step drawn).</summary>
    public int Uploads { get; private set; }
    public double LastUploadMs { get; private set; }
    public ShaderMaterial? Material { get; private set; }
    /// <summary>Whether the fog is wanted at all (it's also hidden until some ground is known).</summary>
    public bool Shown { get; set; } = true;

    private GroundFogSimulation? _simulation;
    private float[] _fog = System.Array.Empty<float>();
    private float[] _heights = System.Array.Empty<float>();
    private byte[] _fogBytes = System.Array.Empty<byte>();
    private byte[] _heightBytes = System.Array.Empty<byte>();
    private ImageTexture? _a;
    private ImageTexture? _b;
    private ImageTexture? _ground;
    private Image? _fogImage;
    private Image? _heightImage;
    private bool _aIsNow;
    private Vector2 _nowOrigin;
    private readonly System.Diagnostics.Stopwatch _watch = new();

    public void Setup(GroundFogSimulation simulation)
    {
        _simulation = simulation;
        int n = simulation.Size;
        _fog = new float[n * n * 4];
        _heights = new float[n * n];
        _fogBytes = new byte[n * n * 16];
        _heightBytes = new byte[n * n * 4];
        _fogImage = Image.CreateFromData(n, n, false, Image.Format.Rgbaf, _fogBytes);
        _heightImage = Image.CreateFromData(n, n, false, Image.Format.Rf, _heightBytes);
        _a = ImageTexture.CreateFromImage(_fogImage);
        _b = ImageTexture.CreateFromImage(_fogImage);
        _ground = ImageTexture.CreateFromImage(_heightImage);
        Material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
        Material.SetShaderParameter("wisps", MakeWisps());
        Material.SetShaderParameter("ground", _ground);
        Material.SetShaderParameter("fog_now", _a);
        Material.SetShaderParameter("fog_prev", _b);
        Mesh = new BoxMesh { Size = Vector3.One };
        MaterialOverride = Material;
        CastShadow = ShadowCastingSetting.Off;
        TopLevel = true;
        Upload();
        Upload();
    }

    /// <summary>The newest step goes up; the one before it becomes the step the shader blends from.</summary>
    public void Upload()
    {
        if (_simulation == null || _fogImage == null || _heightImage == null || Material == null)
        {
            return;
        }
        _watch.Restart();
        GroundFogSimulation sim = _simulation;
        sim.Export(_fog, _heights);
        System.Buffer.BlockCopy(_fog, 0, _fogBytes, 0, _fogBytes.Length);
        System.Buffer.BlockCopy(_heights, 0, _heightBytes, 0, _heightBytes.Length);
        _fogImage.SetData(sim.Size, sim.Size, false, Image.Format.Rgbaf, _fogBytes);
        _heightImage.SetData(sim.Size, sim.Size, false, Image.Format.Rf, _heightBytes);
        _aIsNow = !_aIsNow;
        ImageTexture now = _b!;
        ImageTexture before = _a!;
        if (_aIsNow)
        {
            now = _a!;
            before = _b!;
        }
        now.Update(_fogImage);
        _ground!.Update(_heightImage);
        Vector2 origin = new(sim.OriginX * sim.Cell, sim.OriginZ * sim.Cell);
        Material.SetShaderParameter("fog_now", now);
        Material.SetShaderParameter("fog_prev", before);
        Material.SetShaderParameter("now_origin", origin);
        Material.SetShaderParameter("prev_origin", _nowOrigin);
        _nowOrigin = origin;
        FitBox(sim, origin);
        Uploads++;
        LastUploadMs = _watch.Elapsed.TotalMilliseconds;
    }

    /// <summary>How far from the older step toward the newest the shader draws (0-1).</summary>
    public void SetBlend(float share)
    {
        Material?.SetShaderParameter("blend", Mathf.Clamp(share, 0.0f, 1.0f));
    }

    /// <summary>The look: settings, the light of the hour, and which debug view (0 none, 1 density, 2 flow, 3 ground).</summary>
    public void Look(GroundFogSettings s, float daylight, int debugMode)
    {
        if (Material == null || _simulation == null)
        {
            return;
        }
        Material.SetShaderParameter("patch_size", _simulation.Size * _simulation.Cell);
        Material.SetShaderParameter("depth", s.Depth);
        Material.SetShaderParameter("density", s.Density);
        Material.SetShaderParameter("max_density", s.MaxDensity);
        Material.SetShaderParameter("fog_colour", s.NightColor.Lerp(s.DayColor, Mathf.Clamp(daylight, 0.0f, 1.0f)));
        Material.SetShaderParameter("top_light", s.TopLight);
        Material.SetShaderParameter("noise_size", s.NoiseSize);
        Material.SetShaderParameter("noise_amount", s.NoiseAmount);
        Material.SetShaderParameter("flow_period", s.FlowPeriod);
        Material.SetShaderParameter("ragged_top", s.RaggedTop);
        Material.SetShaderParameter("steps", s.Steps);
        Material.SetShaderParameter("max_distance", s.MaxDistance);
        Material.SetShaderParameter("fine_step", s.FineStep);
        Material.SetShaderParameter("distance_step", s.DistanceStep);
        Material.SetShaderParameter("edge_fade", s.EdgeFade);
        Material.SetShaderParameter("debug_mode", debugMode);
    }

    /// <summary>The box spans the patch, from just under its lowest known ground to above its tallest possible fog.</summary>
    private void FitBox(GroundFogSimulation sim, Vector2 origin)
    {
        float low = float.MaxValue;
        float high = float.MinValue;
        for (int i = 0; i < sim.Height.Length; i++)
        {
            if (sim.Ground[i] != GroundFogSimulation.Known)
            {
                continue;
            }
            low = Mathf.Min(low, sim.Height[i]);
            high = Mathf.Max(high, sim.Height[i]);
        }
        Visible = Shown && low <= high;
        if (low > high)
        {
            return;
        }
        GroundFogSettings s = sim.Settings;
        low -= 1.0f;
        high += s.Depth * s.MaxDensity * (1.0f + s.RaggedTop) + 0.5f;
        float size = sim.Size * sim.Cell;
        Vector3 min = new(origin.X, low, origin.Y);
        Vector3 max = new(origin.X + size, high, origin.Y + size);
        Material!.SetShaderParameter("box_min", min);
        Material.SetShaderParameter("box_max", max);
        GlobalTransform = new Transform3D(Basis.FromScale(max - min), (min + max) * 0.5f);
    }

    /// <summary>A tiling 3D noise (64³, soft cells) for the wisps.</summary>
    private static NoiseTexture3D MakeWisps()
    {
        FastNoiseLite noise = new()
        {
            NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin,
            Frequency = 0.08f,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 3,
            Seed = 1907,
        };
        return new NoiseTexture3D
        {
            Width = 64,
            Height = 64,
            Depth = 64,
            Seamless = true,
            Noise = noise,
            Normalize = true,
        };
    }
}
