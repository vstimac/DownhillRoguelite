using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// The carrier's trail: every time a foot plants (GameEvents.FootPlacementUpdated) a footprint is
/// pressed into the ground where it landed, a decal following the slope and pointing the way the
/// carrier walks, mirrored for the left foot. How it looks and how long it lasts depends on the
/// ground: deep and blue-shadowed in snow, dark and wet in mud, a faint flattened mark in grass,
/// nothing on rock and wood. A small puff kicks up with each step: powder in snow, spatter in
/// mud, dust on scree and dry ground. Prints fade as they age, faster while snow is falling (it
/// fills them) and in rain. Up to MaxPrints at once; the oldest go first. Visual only; skipped in
/// headless runs.
/// </summary>
public partial class FootTrail : Node3D
{
    [Export] public int MaxPrints { get; set; } = 80;
    /// <summary>Footprint size: width, depth of the projection, length, in m.</summary>
    [Export] public Vector3 PrintSize { get; set; } = new(0.14f, 0.4f, 0.3f);
    [Export] public float SnowLife { get; set; } = 120.0f;
    [Export] public float MudLife { get; set; } = 90.0f;
    [Export] public float GrassLife { get; set; } = 25.0f;
    /// <summary>Prints age this many times faster while it snows or rains hard.</summary>
    [Export] public float WeatherFade { get; set; } = 3.0f;
    [Export] public bool Puffs { get; set; } = true;

    public int Count { get; private set; }

    private Decal[] _prints = System.Array.Empty<Decal>();
    private float[] _age = System.Array.Empty<float>();
    private float[] _life = System.Array.Empty<float>();
    private float[] _alpha = System.Array.Empty<float>();
    private int _next;
    private bool _leftPlanted;
    private bool _rightPlanted;
    private string _surface = "grass";
    private Vector3 _forward = Vector3.Forward;
    private CpuParticles3D? _puffLeft;
    private CpuParticles3D? _puffRight;
    private Texture2D? _texture;
    private Texture2D? _normal;
    private bool _enabled;

    public override void _Ready()
    {
        TopLevel = true;
        _enabled = DisplayServer.GetName() != "headless";
        if (!_enabled)
        {
            return;
        }
        _texture = MakePrintTexture(out _normal);
        _prints = new Decal[MaxPrints];
        _age = new float[MaxPrints];
        _life = new float[MaxPrints];
        _alpha = new float[MaxPrints];
        for (int i = 0; i < MaxPrints; i++)
        {
            _prints[i] = new Decal
            {
                TextureAlbedo = _texture,
                TextureNormal = _normal,
                Size = PrintSize,
                Visible = false,
                UpperFade = 0.2f,
                LowerFade = 0.4f,
                CullMask = 1,
            };
            AddChild(_prints[i]);
        }
        _puffLeft = MakePuff();
        _puffRight = MakePuff();
        GameEvents.FootPlacementUpdated += OnFeet;
        GameEvents.LocomotionUpdated += OnLocomotion;
        GameEvents.RespawnRequested += Clear;
    }

    public override void _ExitTree()
    {
        if (!_enabled)
        {
            return;
        }
        GameEvents.FootPlacementUpdated -= OnFeet;
        GameEvents.LocomotionUpdated -= OnLocomotion;
        GameEvents.RespawnRequested -= Clear;
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        _surface = snapshot.Surface.ToLowerInvariant();
        Vector3 forward = snapshot.Forward;
        forward.Y = 0.0f;
        if (!forward.IsZeroApprox())
        {
            _forward = forward.Normalized();
        }
    }

    private void OnFeet(FootPlacementSnapshot snapshot)
    {
        if (snapshot.Left.Planted && !_leftPlanted && snapshot.Left.HasGround)
        {
            Step(snapshot.Left, true);
        }
        if (snapshot.Right.Planted && !_rightPlanted && snapshot.Right.HasGround)
        {
            Step(snapshot.Right, false);
        }
        _leftPlanted = snapshot.Left.Planted;
        _rightPlanted = snapshot.Right.Planted;
    }

    /// <summary>Presses a print where a foot landed and kicks up a puff, by the ground underfoot.</summary>
    private void Step(FootSnapshot foot, bool left)
    {
        (Color tint, float life, Color puff) = Look(_surface);
        if (life > 0.0f)
        {
            Decal print = _prints[_next];
            Vector3 up = foot.GroundNormal;
            if (up.IsZeroApprox())
            {
                up = Vector3.Up;
            }
            up = up.Normalized();
            Vector3 along = (_forward - up * _forward.Dot(up)).Normalized();
            if (along.IsZeroApprox())
            {
                along = Vector3.Forward;
            }
            Vector3 across = along.Cross(up).Normalized();
            if (left)
            {
                across = -across;
            }
            // A decal projects along its -Y; its texture's length runs along Z.
            // Turned 180° so the toe points the way he walks.
            print.GlobalTransform = new Transform3D(new Basis(-across, up, -along), foot.GroundPoint + up * 0.05f);
            print.Modulate = tint;
            print.Visible = true;
            _age[_next] = 0.0f;
            _life[_next] = life;
            _alpha[_next] = tint.A;
            _next = (_next + 1) % _prints.Length;
            Count = Mathf.Min(Count + 1, _prints.Length);
        }
        CpuParticles3D? emitter = _puffRight;
        if (left)
        {
            emitter = _puffLeft;
        }
        if (Puffs && emitter != null && puff.A > 0.0f)
        {
            emitter.GlobalPosition = foot.GroundPoint + Vector3.Up * 0.03f;
            emitter.Color = puff;
            emitter.Restart();
            emitter.Emitting = true;
        }
    }

    /// <summary>How a print looks on a surface: its tint, how long it lasts (0: no print), and the puff's colour (clear: none).</summary>
    private (Color Tint, float Life, Color Puff) Look(string surface)
    {
        return surface switch
        {
            "snow" => (new Color(0.9f, 0.94f, 1.0f, 0.9f), SnowLife, new Color(0.95f, 0.97f, 1.0f, 0.8f)),
            "mud" => (new Color(0.36f, 0.25f, 0.14f, 0.9f), MudLife, new Color(0.3f, 0.2f, 0.1f, 0.8f)),
            "grass" => (new Color(0.3f, 0.4f, 0.18f, 0.45f), GrassLife, new Color(0.35f, 0.5f, 0.2f, 0.7f)),
            "scree" => (new Color(0.45f, 0.42f, 0.38f, 0.4f), GrassLife, new Color(0.65f, 0.6f, 0.52f, 0.6f)),
            "sand" => (new Color(0.7f, 0.58f, 0.4f, 0.6f), MudLife, new Color(0.8f, 0.7f, 0.5f, 0.6f)),
            "rock" => (Colors.Transparent, 0.0f, new Color(0.6f, 0.58f, 0.55f, 0.35f)),
            _ => (Colors.Transparent, 0.0f, new Color(0.0f, 0.0f, 0.0f, 0.0f)),
        };
    }

    public override void _Process(double delta)
    {
        if (!_enabled || Count == 0)
        {
            return;
        }
        WeatherSnapshot weather = GameEvents.CurrentWeather;
        float speed = 1.0f;
        if (weather.Snow > 0.3f || weather.Rain > 0.5f)
        {
            speed = WeatherFade;
        }
        for (int i = 0; i < _prints.Length; i++)
        {
            Decal print = _prints[i];
            if (!print.Visible)
            {
                continue;
            }
            _age[i] += (float)delta * speed;
            float left = 1.0f - _age[i] / Mathf.Max(_life[i], 0.01f);
            if (left <= 0.0f)
            {
                print.Visible = false;
                Count = Mathf.Max(0, Count - 1);
                continue;
            }
            Color tint = print.Modulate;
            tint.A = _alpha[i] * Mathf.Min(1.0f, left * 3.0f);
            print.Modulate = tint;
        }
    }

    private void Clear()
    {
        foreach (Decal print in _prints)
        {
            print.Visible = false;
        }
        Count = 0;
    }

    private CpuParticles3D MakePuff()
    {
        CpuParticles3D puff = new()
        {
            Emitting = false,
            OneShot = true,
            Amount = 10,
            Lifetime = 0.6,
            Explosiveness = 0.9f,
            Direction = Vector3.Up,
            Spread = 70.0f,
            InitialVelocityMin = 0.4f,
            InitialVelocityMax = 1.1f,
            Gravity = new Vector3(0.0f, -2.5f, 0.0f),
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1.2f,
            Mesh = new SphereMesh { Radius = 0.025f, Height = 0.05f, RadialSegments = 6, Rings = 3 },
            TopLevel = true,
        };
        puff.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        AddChild(puff);
        return puff;
    }

    /// <summary>
    /// A deep boot print drawn once from a height field: the sole and heel pressed in (darker), a
    /// raised rim of pushed-out snow or mud round them (full ground colour), and a normal map so the
    /// sun and the moon shade the walls of the dent. White and grey, tinted per surface.
    /// </summary>
    private static Texture2D MakePrintTexture(out Texture2D normal)
    {
        const int w = 32;
        const int h = 64;
        float[,] height = new float[w, h];
        float[,] cover = new float[w, h];
        float[,] shade = new float[w, h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2.0f - 1.0f;
                float v = (y + 0.5f) / h;
                // Sole: an ellipse in the front two thirds; heel: a smaller one at the back.
                float sole = 1.0f - ((u * u) / 0.62f + (v - 0.33f) * (v - 0.33f) / 0.055f);
                float heel = 1.0f - ((u * u) / 0.42f + (v - 0.8f) * (v - 0.8f) / 0.02f);
                float inside = Mathf.Max(sole, heel);
                float pressed = Mathf.Clamp(inside * 4.0f, 0.0f, 1.0f);
                // The rim: a band just outside the print, fading out.
                float rim = Mathf.Clamp(1.0f - Mathf.Abs(inside + 0.22f) / 0.2f, 0.0f, 1.0f);
                height[x, y] = rim * 0.45f - pressed;
                cover[x, y] = Mathf.Max(pressed, rim * 0.7f);
                // Inside the print is darker (shadowed, packed); the rim keeps the ground's colour.
                shade[x, y] = Mathf.Lerp(1.0f, 0.62f, pressed);
            }
        }
        Image albedo = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        Image normals = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        const float strength = 2.2f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                albedo.SetPixel(x, y, new Color(shade[x, y], shade[x, y], shade[x, y], cover[x, y]));
                float dx = height[Mathf.Min(x + 1, w - 1), y] - height[Mathf.Max(x - 1, 0), y];
                float dy = height[x, Mathf.Min(y + 1, h - 1)] - height[x, Mathf.Max(y - 1, 0)];
                Vector3 n = new Vector3(-dx * strength, dy * strength, 1.0f).Normalized();
                normals.SetPixel(x, y, new Color(n.X * 0.5f + 0.5f, n.Y * 0.5f + 0.5f, n.Z * 0.5f + 0.5f, 1.0f));
            }
        }
        normal = ImageTexture.CreateFromImage(normals);
        return ImageTexture.CreateFromImage(albedo);
    }
}
