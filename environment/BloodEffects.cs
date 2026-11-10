using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// Blood: a blade that cuts into a body (GameEvents.Wounded) throws a spray of drops along the blow
/// and stains the ground where they fall, and leaves the body bleeding (Bleeding): it loses health
/// and drips a trail until the wound closes or it goes down, where it leaves a pool. The neck bleeds
/// worst, then the torso; limbs least. A blow to the carrier (GameEvents.DamageRequested) sprays too.
/// Stains are decals on the ground that fade after a while; only so many stay at once.
/// </summary>
public partial class BloodEffects : Node3D
{
    [ExportGroup("Spray")]
    [Export] public Color BloodColour { get; set; } = new(0.32f, 0.01f, 0.02f);
    /// <summary>Drops per blow: a base plus this many per point of damage, times the zone's flow.</summary>
    [Export] public int SprayDrops { get; set; } = 14;
    [Export] public float DropsPerDamage { get; set; } = 0.7f;
    [Export] public int MaxDrops { get; set; } = 80;
    [Export] public Vector2 SpraySpeed { get; set; } = new(1.2f, 4.2f);
    [Export] public float SpraySpread { get; set; } = 32.0f;
    [Export] public float DropSize { get; set; } = 0.022f;
    [Export] public float SprayLifetime { get; set; } = 0.9f;

    [ExportGroup("Stains")]
    /// <summary>A stain's size (m): a blow's at least the first, at most the second; a drip's; a body's pool.</summary>
    [Export] public Vector2 SplatSize { get; set; } = new(0.3f, 1.1f);
    [Export] public float SplatPerDamage { get; set; } = 0.018f;
    [Export] public float DripSize { get; set; } = 0.14f;
    [Export] public float DeathPoolSize { get; set; } = 1.6f;
    /// <summary>How far the spray carries its stain along the blow (m).</summary>
    [Export] public float SplatThrow { get; set; } = 0.5f;
    [Export] public int MaxStains { get; set; } = 90;
    [Export] public float StainSeconds { get; set; } = 90.0f;
    [Export] public float FadeSeconds { get; set; } = 6.0f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    [ExportGroup("Bleeding")]
    /// <summary>Health per second a wound drains, as a share of the blow's damage, by zone.</summary>
    [Export] public float NeckBleed { get; set; } = 0.14f;
    [Export] public float TorsoBleed { get; set; } = 0.07f;
    [Export] public float HeadBleed { get; set; } = 0.06f;
    [Export] public float LimbBleed { get; set; } = 0.04f;
    [Export] public float BodyBleed { get; set; } = 0.05f;
    [Export] public float MaxBleedRate { get; set; } = 6.0f;
    [Export] public float BleedSeconds { get; set; } = 14.0f;
    [Export] public float BleedTick { get; set; } = 0.5f;
    /// <summary>A drip every DripSeconds at DripRate health per second (more often when it flows faster).</summary>
    [Export] public float DripSeconds { get; set; } = 1.0f;
    [Export] public float MinDripSeconds { get; set; } = 0.45f;
    [Export] public float DripRate { get; set; } = 1.5f;

    [ExportGroup("Carrier")]
    /// <summary>A blow to the carrier this hard or harder sprays his blood.</summary>
    [Export] public float CarrierSprayFrom { get; set; } = 4.0f;
    [Export] public float CarrierChestHeight { get; set; } = 1.25f;

    public int Sprays { get; private set; }
    public int StainCount => _stains.Count;

    private readonly List<Decal> _stains = new();
    private readonly List<float> _ages = new();
    private readonly RandomNumberGenerator _random = new();
    private StandardMaterial3D? _dropMaterial;
    private SphereMesh? _dropMesh;

    public override void _Ready()
    {
        _random.Randomize();
        GameEvents.Wounded += OnWounded;
        GameEvents.DamageRequested += OnCarrierHurt;
    }

    public override void _ExitTree()
    {
        GameEvents.Wounded -= OnWounded;
        GameEvents.DamageRequested -= OnCarrierHurt;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = _stains.Count - 1; i >= 0; i--)
        {
            _ages[i] += dt;
            Decal stain = _stains[i];
            if (!IsInstanceValid(stain))
            {
                _stains.RemoveAt(i);
                _ages.RemoveAt(i);
                continue;
            }
            float fade = Mathf.Clamp((StainSeconds - _ages[i]) / Mathf.Max(FadeSeconds, 0.01f), 0.0f, 1.0f);
            stain.Modulate = new Color(BloodColour, fade);
            if (fade <= 0.0f)
            {
                stain.QueueFree();
                _stains.RemoveAt(i);
                _ages.RemoveAt(i);
            }
        }
    }

    private void OnWounded(Node target, Vector3 point, Vector3 direction, BodyZone zone, float damage)
    {
        float flow = Flow(zone);
        Spray(point, direction, damage * flow);
        Vector3 landing = point + Flat(direction) * SplatThrow;
        Splat(landing, Mathf.Clamp(SplatSize.X + damage * SplatPerDamage * flow, SplatSize.X, SplatSize.Y));
        if (target is IMeleeTarget body && target is Node3D && IsInstanceValid(target))
        {
            Bleeding? wound = target.GetNodeOrNull<Bleeding>(Bleeding.NodeName);
            if (wound == null)
            {
                wound = new Bleeding { Name = Bleeding.NodeName };
                wound.Setup(this, body);
                target.AddChild(wound);
            }
            wound.Open(damage * BleedShare(zone));
        }
    }

    private void OnCarrierHurt(float amount, string cause)
    {
        if (amount < CarrierSprayFrom)
        {
            return;
        }
        Vector3 chest = GameEvents.CarrierPosition + Vector3.Up * CarrierChestHeight;
        Vector3 away = new(_random.RandfRange(-1.0f, 1.0f), 0.2f, _random.RandfRange(-1.0f, 1.0f));
        Spray(chest, away.Normalized(), amount);
        Splat(GameEvents.CarrierPosition + Flat(away) * 0.3f, Mathf.Clamp(SplatSize.X + amount * SplatPerDamage, SplatSize.X, SplatSize.Y));
    }

    /// <summary>How hard a part of the body bleeds compared with the torso (the spray's size).</summary>
    public static float Flow(BodyZone zone)
    {
        switch (zone)
        {
            case BodyZone.Neck:
                return 1.7f;
            case BodyZone.Head:
                return 1.2f;
            case BodyZone.Arm:
            case BodyZone.Leg:
                return 0.75f;
            default:
                return 1.0f;
        }
    }

    /// <summary>The share of a blow's damage a wound in this part drains per second.</summary>
    public float BleedShare(BodyZone zone)
    {
        switch (zone)
        {
            case BodyZone.Neck:
                return NeckBleed;
            case BodyZone.Torso:
                return TorsoBleed;
            case BodyZone.Head:
                return HeadBleed;
            case BodyZone.Arm:
            case BodyZone.Leg:
                return LimbBleed;
            default:
                return BodyBleed;
        }
    }

    /// <summary>A burst of drops from <paramref name="point"/> along <paramref name="direction"/>.</summary>
    public void Spray(Vector3 point, Vector3 direction, float strength)
    {
        Sprays++;
        if (!IsInsideTree())
        {
            return;
        }
        _dropMesh ??= new SphereMesh { Radius = DropSize, Height = DropSize * 2.0f, RadialSegments = 6, Rings = 3 };
        _dropMaterial ??= new StandardMaterial3D
        {
            AlbedoColor = BloodColour,
            Roughness = 0.25f,
            Metallic = 0.0f,
        };
        _dropMesh.Material = _dropMaterial;
        Vector3 aim = (direction.Normalized() + Vector3.Up * 0.35f).Normalized();
        if (aim.LengthSquared() < 0.0001f)
        {
            aim = Vector3.Up;
        }
        CpuParticles3D spray = new()
        {
            Name = "BloodSpray",
            Mesh = _dropMesh,
            Amount = Mathf.Clamp(SprayDrops + Mathf.RoundToInt(strength * DropsPerDamage), 4, MaxDrops),
            Lifetime = SprayLifetime,
            OneShot = true,
            Explosiveness = 0.92f,
            Randomness = 0.6f,
            Direction = aim,
            Spread = SpraySpread,
            InitialVelocityMin = SpraySpeed.X,
            InitialVelocityMax = SpraySpeed.Y,
            Gravity = new Vector3(0.0f, -9.8f, 0.0f),
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1.4f,
            LocalCoords = false,
        };
        AddChild(spray);
        spray.GlobalPosition = point;
        spray.Emitting = true;
        GetTree().CreateTimer(SprayLifetime + 0.5f).Timeout += () =>
        {
            if (IsInstanceValid(spray))
            {
                spray.QueueFree();
            }
        };
    }

    /// <summary>A small drop on the ground below <paramref name="point"/> (a bleeding body's trail).</summary>
    public void Drip(Vector3 point)
    {
        Vector3 jitter = new(_random.RandfRange(-0.25f, 0.25f), 0.0f, _random.RandfRange(-0.25f, 0.25f));
        Splat(point + jitter, DripSize * _random.RandfRange(0.6f, 1.4f));
    }

    /// <summary>A body's pool where it fell.</summary>
    public void Pool(Vector3 point, float size)
    {
        Splat(point, size);
    }

    /// <summary>A stain on the ground under <paramref name="point"/>, <paramref name="size"/> m across, turned to the slope.</summary>
    public void Splat(Vector3 point, float size)
    {
        if (!IsInsideTree())
        {
            return;
        }
        Vector3 ground = point;
        Vector3 normal = Vector3.Up;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        Godot.Collections.Dictionary hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(point + Vector3.Up * 1.0f, point + Vector3.Down * 4.0f, GroundMask));
        if (hit.Count > 0)
        {
            ground = (Vector3)hit["position"];
            normal = ((Vector3)hit["normal"]).Normalized();
        }
        // The decal projects along its own -Y: stand it on the ground's normal, turned at random.
        Vector3 side = normal.Cross(Vector3.Forward);
        if (side.LengthSquared() < 0.0001f)
        {
            side = normal.Cross(Vector3.Right);
        }
        side = side.Normalized().Rotated(normal, _random.RandfRange(0.0f, Mathf.Tau));
        Basis basis = new(side, normal, side.Cross(normal));
        Decal stain = new()
        {
            Name = "BloodStain",
            Size = new Vector3(size, 0.6f, size * _random.RandfRange(0.8f, 1.2f)),
            TextureAlbedo = BloodTexture.Splat(),
            Modulate = BloodColour,
            AlbedoMix = 1.0f,
            UpperFade = 0.3f,
            LowerFade = 0.3f,
            CullMask = 1,
        };
        AddChild(stain);
        stain.GlobalTransform = new Transform3D(basis, ground);
        _stains.Add(stain);
        _ages.Add(0.0f);
        while (_stains.Count > MaxStains)
        {
            if (IsInstanceValid(_stains[0]))
            {
                _stains[0].QueueFree();
            }
            _stains.RemoveAt(0);
            _ages.RemoveAt(0);
        }
    }

    private static Vector3 Flat(Vector3 direction)
    {
        Vector3 flat = new(direction.X, 0.0f, direction.Z);
        if (flat.LengthSquared() < 0.0001f)
        {
            return Vector3.Zero;
        }
        return flat.Normalized();
    }
}
