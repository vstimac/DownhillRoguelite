using DownhillRoguelite.Core;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// A two-wheeled hand cart the carrier can pull behind them. E at the handles takes hold (and E again
/// lets go); while held, a spring pulls the handles toward a point behind the carrier, so the cart
/// trails, swings round bends and rolls on its own weight. The wheels roll forward but not sideways.
/// Things dropped onto its bed ride along as cargo (pick them up again with E), and the cart's whole
/// weight slows the carrier (more uphill) through GameEvents.TowMass. Let go, it settles onto its
/// front leg and stays parked.
/// Local frame: origin at the axle, handles toward -Z, up +Y.
/// </summary>
public partial class HandCart : RigidBody3D, IInteractable
{
    [ExportGroup("Build")]
    /// <summary>Wheel radius, in m; the axle sits this high above the ground.</summary>
    [Export] public float WheelRadius { get; set; } = 0.5f;
    /// <summary>How far each wheel is from the middle, in m.</summary>
    [Export] public float WheelTrack { get; set; } = 0.72f;
    [Export] public float WheelWidth { get; set; } = 0.1f;
    /// <summary>The bed box: size (m) and its centre from the axle.</summary>
    [Export] public Vector3 BedSize { get; set; } = new(1.2f, 0.4f, 1.6f);
    [Export] public Vector3 BedCentre { get; set; } = new(0.0f, 0.25f, 0.1f);
    /// <summary>Where the handles meet the bed and where their grips end, from the axle.</summary>
    [Export] public Vector3 HandleRoot { get; set; } = new(0.45f, 0.15f, -0.7f);
    [Export] public Vector3 HandleTip { get; set; } = new(0.4f, 0.4f, -1.9f);
    /// <summary>The leg under the front of the bed it rests on when let go (centre and radius).</summary>
    [Export] public Vector3 LegAt { get; set; } = new(0.0f, -0.25f, -0.75f);
    [Export] public float LegRadius { get; set; } = 0.1f;
    [Export] public Color WoodColor { get; set; } = new(0.45f, 0.3f, 0.18f);
    [Export] public Color DarkWoodColor { get; set; } = new(0.3f, 0.2f, 0.12f);
    [Export] public Color IronColor { get; set; } = new(0.25f, 0.25f, 0.27f);

    [ExportGroup("Pulling")]
    /// <summary>The cart on its own, in kg.</summary>
    [Export] public float BaseMass { get; set; } = 45.0f;
    /// <summary>The handles are held this far behind the carrier's feet and this high, in m.</summary>
    [Export] public float HitchDistance { get; set; } = 1.3f;
    [Export] public float HitchHeight { get; set; } = 0.9f;
    /// <summary>Spring pulling the handles to the hitch, per kg of cart (N/m/kg), and its damping (N·s/m/kg).</summary>
    [Export] public float Stiffness { get; set; } = 60.0f;
    [Export] public float Damping { get; set; } = 9.0f;
    /// <summary>Strongest pull, per kg of cart (N/kg).</summary>
    [Export] public float MaxPullPerKg { get; set; } = 40.0f;
    /// <summary>The handles slip out of the hands if they end up this far from the hitch, in m.</summary>
    [Export] public float BreakDistance { get; set; } = 3.5f;

    [ExportGroup("Wheels")]
    /// <summary>How quickly sideways sliding is stopped while a wheel touches ground (per s).</summary>
    [Export] public float LateralGrip { get; set; } = 12.0f;
    /// <summary>Rolling resistance along the wheels while pulled (per s).</summary>
    [Export] public float RollingDrag { get; set; } = 0.25f;
    /// <summary>Let go: brakes this hard (per s) for this long (s), then parks still until taken again.</summary>
    [Export] public float ParkDrag { get; set; } = 6.0f;
    [Export] public float SettleSeconds { get; set; } = 1.2f;
    /// <summary>Ground the cart rolls on and is set down on (terrain).</summary>
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    [ExportGroup("Cargo")]
    /// <summary>Dropped things this close above the bed (m, beyond its edges by Margin) go on as cargo.</summary>
    [Export] public float CargoReachAbove { get; set; } = 1.2f;
    [Export] public float CargoMargin { get; set; } = 0.15f;
    /// <summary>How often the bed is checked for new cargo, in s.</summary>
    [Export] public float CargoCheckSeconds { get; set; } = 0.3f;

    [ExportGroup("Prompt")]
    [Export] public string TakePrompt { get; set; } = "E: take the cart";
    [Export] public string LetGoPrompt { get; set; } = "E: let go of the cart";
    [Export] public float PromptHeight { get; set; } = 0.6f;
    [Export] public int PromptFontSize { get; set; } = 36;
    [Export] public float PromptPixelSize { get; set; } = 0.004f;

    /// <summary>Who holds the handles, or null.</summary>
    public Node3D? Puller { get; private set; }
    public bool Towing => Puller != null;
    /// <summary>Set down on the ground and standing still (frozen) until taken again.</summary>
    public bool Parked => Freeze && _onGround && !Towing;
    /// <summary>Cargo on the bed, in kg.</summary>
    public float CargoMass { get; private set; }
    /// <summary>The handles' grip point in the world.</summary>
    public Vector3 HandlePoint => ToGlobal(new Vector3(0.0f, HandleTip.Y, HandleTip.Z));
    public Node3D? Cargo => _cargo;

    private readonly Node3D[] _wheels = new Node3D[2];
    private Node3D? _cargo;
    private Label3D? _prompt;
    private bool _focused;
    private float _settleTimer;
    private float _cargoTimer;
    private bool _onGround;

    public override void _Ready()
    {
        AddToGroup(IInteractable.Group);
        Mass = BaseMass;
        CollisionLayer = 1;
        CollisionMask = GroundMask;
        // The wheels slide freely; rolling and grip are applied by hand in _IntegrateForces.
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.0f, Bounce = 0.0f };
        ContactMonitor = true;
        MaxContactsReported = 6;
        CanSleep = false;
        AngularDamp = 1.5f;
        CenterOfMassMode = CenterOfMassModeEnum.Custom;
        CenterOfMass = new Vector3(0.0f, BedCentre.Y * 0.5f, BedCentre.Z);
        Build();
        _cargo = new Node3D { Name = "Cargo" };
        AddChild(_cargo);
        // Placed somewhere new: hold still until there is ground below to settle onto.
        Freeze = true;
        FreezeMode = FreezeModeEnum.Static;
        GameEvents.RespawnRequested += LetGo;
    }

    public override void _ExitTree()
    {
        GameEvents.RespawnRequested -= LetGo;
        if (Towing)
        {
            GameEvents.PublishTow(0.0f);
        }
    }

    // --- IInteractable ---

    public Vector3 InteractionPoint
    {
        get
        {
            // Held: always within reach (the handles are behind the carrier), so E lets go.
            if (Puller != null && IsInstanceValid(Puller))
            {
                return Puller.GlobalPosition;
            }
            return HandlePoint;
        }
    }

    public void SetFocused(bool focused)
    {
        _focused = focused;
        UpdatePrompt();
    }

    public bool Interact(Node3D interactor)
    {
        if (Towing)
        {
            LetGo();
            return true;
        }
        Take(interactor);
        return true;
    }

    /// <summary>Takes hold of the handles (tests and automation can call it directly).</summary>
    public void Take(Node3D puller)
    {
        Puller = puller;
        // Out of the station (or wherever it was), so it outlives the place it was found.
        Node? scene = GetTree().CurrentScene;
        if (scene != null && GetParent() != scene)
        {
            Reparent(scene, true);
        }
        Freeze = false;
        _onGround = true;
        _settleTimer = 0.0f;
        GameEvents.PublishTow(Mass);
        UpdatePrompt();
    }

    public void LetGo()
    {
        if (Puller == null)
        {
            return;
        }
        Puller = null;
        Freeze = false;
        _settleTimer = SettleSeconds;
        GameEvents.PublishTow(0.0f);
        UpdatePrompt();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (Freeze && !Towing && _settleTimer <= 0.0f && !_onGround)
        {
            // Not yet set down: wait for ground below, then let it settle onto it.
            Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(
                PhysicsRayQueryParameters3D.Create(GlobalPosition + Vector3.Up, GlobalPosition + Vector3.Down * 3.0f, GroundMask, new Godot.Collections.Array<Rid> { GetRid() }));
            if (hit.Count > 0)
            {
                _onGround = true;
                Freeze = false;
                _settleTimer = SettleSeconds;
            }
        }
        if (Towing)
        {
            if (Puller == null || !IsInstanceValid(Puller) || !Puller.IsInsideTree() || GameEvents.IsDowned
                || HandlePoint.DistanceTo(Hitch()) > BreakDistance)
            {
                LetGo();
            }
        }
        else if (_settleTimer > 0.0f)
        {
            _settleTimer -= dt;
            if (_settleTimer <= 0.0f && LinearVelocity.Length() < 0.5f)
            {
                Freeze = true;
            }
            else if (_settleTimer <= 0.0f)
            {
                _settleTimer = 0.3f;
            }
        }

        _cargoTimer -= dt;
        if (_cargoTimer <= 0.0f)
        {
            _cargoTimer = CargoCheckSeconds;
            GatherCargo();
        }

        // The wheels turn with the ground going by.
        float roll = LinearVelocity.Dot(-GlobalBasis.Z) / Mathf.Max(WheelRadius, 0.05f) * dt;
        foreach (Node3D wheel in _wheels)
        {
            wheel?.RotateObjectLocal(Vector3.Right, -roll);
        }
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        float dt = state.Step;
        Basis basis = state.Transform.Basis;
        Vector3 v = state.LinearVelocity;

        bool grounded = state.GetContactCount() > 0;
        if (grounded)
        {
            // Wheels: no sliding sideways; along them, a little rolling resistance (or the brake when let go).
            Vector3 axle = basis.X.Normalized();
            Vector3 along = -basis.Z.Normalized();
            v -= axle * v.Dot(axle) * Mathf.Min(1.0f, LateralGrip * dt);
            float drag = RollingDrag;
            if (!Towing)
            {
                drag = ParkDrag;
            }
            v -= along * v.Dot(along) * Mathf.Min(1.0f, drag * dt);
            state.LinearVelocity = v;
        }

        if (Towing && Puller != null && IsInstanceValid(Puller))
        {
            // The handles are pulled toward the hitch behind the carrier like a stiff spring.
            Vector3 grip = new(0.0f, HandleTip.Y, HandleTip.Z);
            Vector3 offset = state.Transform.Basis * grip;
            Vector3 handle = state.Transform.Origin + offset;
            Vector3 handleVelocity = state.LinearVelocity + state.AngularVelocity.Cross(offset - state.CenterOfMass);
            Vector3 pullerVelocity = Vector3.Zero;
            if (Puller is CharacterBody3D body)
            {
                pullerVelocity = body.Velocity;
            }
            float mass = Mass;
            Vector3 force = (Hitch() - handle) * Stiffness * mass - (handleVelocity - pullerVelocity) * Damping * mass;
            force = force.LimitLength(MaxPullPerKg * mass);
            state.ApplyImpulse(force * dt, offset);
        }
    }

    /// <summary>
    /// Where the handles are held: behind the carrier on the line from them to the cart (like a tow
    /// bar), so the cart trails on bends instead of jumping round when the carrier turns.
    /// </summary>
    public Vector3 Hitch()
    {
        if (Puller == null || !IsInstanceValid(Puller))
        {
            return HandlePoint;
        }
        Vector3 feet = Puller.GlobalPosition;
        Vector3 back = GlobalPosition - feet;
        back.Y = 0.0f;
        if (back.LengthSquared() < 0.0001f)
        {
            back = Puller.GlobalBasis.Z;
            back.Y = 0.0f;
        }
        return feet + back.Normalized() * HitchDistance + Vector3.Up * HitchHeight;
    }

    /// <summary>Dropped things lying on the bed become cargo and ride along; their weight is the cart's.</summary>
    public void GatherCargo()
    {
        if (_cargo == null)
        {
            return;
        }
        Vector3 half = BedSize * 0.5f;
        float floor = BedCentre.Y - half.Y;
        foreach (Node node in GetTree().GetNodesInGroup(IInteractable.Group))
        {
            if (node is not WorldItem item || item.GetParent() == _cargo || !IsInstanceValid(item))
            {
                continue;
            }
            Vector3 local = ToLocal(item.GlobalPosition) - BedCentre;
            if (Mathf.Abs(local.X) > half.X + CargoMargin || Mathf.Abs(local.Z) > half.Z + CargoMargin)
            {
                continue;
            }
            float height = local.Y + BedCentre.Y - floor;
            if (height < -CargoMargin || height > CargoReachAbove)
            {
                continue;
            }
            item.Reparent(_cargo, true);
        }

        float cargo = 0.0f;
        foreach (Node child in _cargo.GetChildren())
        {
            if (child is WorldItem carried && carried.Definition != null && !carried.IsQueuedForDeletion())
            {
                cargo += carried.Definition.MassKg;
            }
        }
        if (!Mathf.IsEqualApprox(cargo, CargoMass))
        {
            CargoMass = cargo;
            Mass = BaseMass + cargo;
            if (Towing)
            {
                GameEvents.PublishTow(Mass);
            }
        }
    }

    private void UpdatePrompt()
    {
        if (_prompt == null)
        {
            return;
        }
        _prompt.Visible = _focused;
        if (Towing)
        {
            _prompt.Text = LetGoPrompt;
        }
        else
        {
            _prompt.Text = TakePrompt;
        }
    }

    /// <summary>The cart from simple shapes: plank bed with low sides, two spoked wheels, two handles, a leg.</summary>
    private void Build()
    {
        StandardMaterial3D wood = new() { AlbedoColor = WoodColor, Roughness = 1.0f };
        StandardMaterial3D dark = new() { AlbedoColor = DarkWoodColor, Roughness = 1.0f };
        StandardMaterial3D iron = new() { AlbedoColor = IronColor, Roughness = 0.6f, Metallic = 0.6f };
        Vector3 half = BedSize * 0.5f;
        const float Plank = 0.06f;

        // Bed: floor and four low sides.
        AddBox(new Vector3(BedSize.X, Plank, BedSize.Z), BedCentre + new Vector3(0.0f, -half.Y + Plank * 0.5f, 0.0f), wood);
        AddBox(new Vector3(Plank, BedSize.Y, BedSize.Z), BedCentre + new Vector3(-half.X + Plank * 0.5f, 0.0f, 0.0f), wood);
        AddBox(new Vector3(Plank, BedSize.Y, BedSize.Z), BedCentre + new Vector3(half.X - Plank * 0.5f, 0.0f, 0.0f), wood);
        AddBox(new Vector3(BedSize.X, BedSize.Y, Plank), BedCentre + new Vector3(0.0f, 0.0f, -half.Z + Plank * 0.5f), wood);
        AddBox(new Vector3(BedSize.X, BedSize.Y * 0.6f, Plank), BedCentre + new Vector3(0.0f, -BedSize.Y * 0.2f, half.Z - Plank * 0.5f), wood);
        // Axle.
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = WheelTrack * 2.0f + WheelWidth, RadialSegments = 8 },
            Rotation = new Vector3(0.0f, 0.0f, Mathf.Pi * 0.5f),
            MaterialOverride = iron,
        });

        // Handles: two rails from the bed's front to the grips, and a cross bar to hold.
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 root = new(HandleRoot.X * side, HandleRoot.Y, HandleRoot.Z);
            Vector3 tip = new(HandleTip.X * side, HandleTip.Y, HandleTip.Z);
            Vector3 mid = (root + tip) * 0.5f;
            float length = root.DistanceTo(tip);
            MeshInstance3D rail = new()
            {
                Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.06f, length + 0.1f) },
                MaterialOverride = dark,
            };
            AddChild(rail);
            rail.Transform = new Transform3D(Basis.LookingAt((tip - root).Normalized(), Vector3.Up), mid);
        }
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = HandleTip.X * 2.0f + 0.1f, RadialSegments = 8 },
            Position = new Vector3(0.0f, HandleTip.Y, HandleTip.Z),
            Rotation = new Vector3(0.0f, 0.0f, Mathf.Pi * 0.5f),
            MaterialOverride = dark,
        });
        // The leg it rests on when let go.
        AddBox(new Vector3(0.08f, -LegAt.Y + BedCentre.Y - half.Y, 0.08f), new Vector3(LegAt.X, (LegAt.Y + BedCentre.Y - half.Y) * 0.5f, LegAt.Z), dark);

        // Wheels: rim, hub and spokes, each spun in _PhysicsProcess.
        for (int i = 0; i < 2; i++)
        {
            float x = WheelTrack;
            if (i == 0)
            {
                x = -WheelTrack;
            }
            Node3D wheel = new() { Name = $"Wheel{i}", Position = new Vector3(x, 0.0f, 0.0f) };
            wheel.AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = WheelRadius - 0.07f, OuterRadius = WheelRadius, Rings = 24, RingSegments = 6 },
                Rotation = new Vector3(0.0f, 0.0f, Mathf.Pi * 0.5f),
                MaterialOverride = dark,
            });
            wheel.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.1f, Height = WheelWidth * 1.4f, RadialSegments = 10 },
                Rotation = new Vector3(0.0f, 0.0f, Mathf.Pi * 0.5f),
                MaterialOverride = iron,
            });
            for (int s = 0; s < 4; s++)
            {
                wheel.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.04f, WheelRadius * 2.0f - 0.08f, 0.05f) },
                    Rotation = new Vector3(Mathf.Pi * 0.25f * s, 0.0f, 0.0f),
                    MaterialOverride = wood,
                });
            }
            AddChild(wheel);
            _wheels[i] = wheel;

            // A flat cylinder per wheel to roll on.
            AddChild(new CollisionShape3D
            {
                Shape = new CylinderShape3D { Radius = WheelRadius, Height = WheelWidth },
                Position = new Vector3(x, 0.0f, 0.0f),
                Rotation = new Vector3(0.0f, 0.0f, Mathf.Pi * 0.5f),
            });
        }

        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = BedSize }, Position = BedCentre });
        AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = LegRadius }, Position = LegAt });

        _prompt = WorldLabel.Readable(new Label3D
        {
            Text = TakePrompt,
            FontSize = PromptFontSize,
            PixelSize = PromptPixelSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            OutlineSize = 8,
            Visible = false,
            Position = new Vector3(0.0f, HandleTip.Y + PromptHeight, HandleTip.Z),
        });
        AddChild(_prompt);
    }

    private void AddBox(Vector3 size, Vector3 at, Material material)
    {
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = at, MaterialOverride = material });
    }
}
