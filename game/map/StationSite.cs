using DownhillRoguelite.Environment;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Map;

/// <summary>
/// A station on its terrace: a camp round a fire with flickering light (stone ring, log seats), a
/// tent with a bedroll (SleepSpot), graves, a trader's corner, banners at the way in with the
/// station's name, and a tall faint beacon that can be seen
/// from far down the slope and through the dark. The fire crackles (audio/sonniss/fire/campfire_loop.ogg), so
/// you can hear a station before you see it.
/// </summary>
public partial class StationSite : Node3D
{
    [Export] public int LabelFontSize { get; set; } = 56;
    [Export] public float LabelPixelSize { get; set; } = 0.006f;
    /// <summary>Beyond this the station's name fades; the beacon still shows the way.</summary>
    [Export] public float LabelRange { get; set; } = 60.0f;
    /// <summary>The line under the name: what a station is for.</summary>
    public const string PurposeText = "Rest by the fire · your run is saved here";

    [Export] public Color FireColor { get; set; } = new(1.0f, 0.55f, 0.2f);
    [Export] public float FireEnergy { get; set; } = 3.0f;
    [Export] public float FireRange { get; set; } = 16.0f;
    /// <summary>The particle flames' size (1: about 0.8 m tall).</summary>
    [Export] public float FlameSize { get; set; } = 1.0f;
    [Export] public float FlickerAmount { get; set; } = 0.25f;
    [Export] public float FlickerSpeed { get; set; } = 9.0f;
    [Export] public Color BeaconColor { get; set; } = new(1.0f, 0.75f, 0.4f, 0.35f);
    [Export] public float BeaconHeight { get; set; } = 40.0f;
    /// <summary>The tent (with the bedroll) from the fire, in m, and its turn so it opens toward the fire (degrees).</summary>
    [Export] public Vector3 TentAt { get; set; } = new(6.2f, 0.0f, 0.6f);
    [Export] public float TentYaw { get; set; } = -90.0f;
    /// <summary>The way in: the banners stand this far uphill of the fire, this far either side of the path (m).</summary>
    [Export] public float GateZ { get; set; } = 6.0f;
    [Export] public float GateHalfWidth { get; set; } = 2.4f;
    /// <summary>The station's name between the banners, this high (m).</summary>
    [Export] public float LabelHeight { get; set; } = 3.2f;
    /// <summary>The beacon fades in from the first distance to the second (m), so it does not stand in the camp.</summary>
    [Export] public Vector2 BeaconFade { get; set; } = new(14.0f, 40.0f);
    /// <summary>The station's hand cart: where it stands (from the fire, m), its turn (degrees), and how high above the ground it is set before settling (m).</summary>
    [Export] public Vector3 CartAt { get; set; } = new(-6.4f, 0.0f, 5.2f);
    /// <summary>Share of a prop's width and depth its collision box takes (models are rounder than their bounds).</summary>
    [Export] public float PropFootprint { get; set; } = 0.8f;
    /// <summary>Loose props start this high above the terrace and settle onto it (m).</summary>
    [Export] public float LooseLift { get; set; } = 0.05f;
    /// <summary>Layer of the camp's fixed props (stall, fence, graves, posts): the carrier and the cart bump into them.</summary>
    [Export(PropertyHint.Layers3DPhysics)] public uint SolidLayer { get; set; } = 16;

    /// <summary>What a camp prop is to the physics: walked through, a fixed solid, or a loose rigid body.</summary>
    private enum PropBody
    {
        None,
        Solid,
        Loose,
    }
    [Export] public float CartYaw { get; set; } = 25.0f;
    [Export] public float CartDrop { get; set; } = 0.3f;
    [Export] public Color WoodColor { get; set; } = new(0.4f, 0.28f, 0.18f);
    [Export] public Color CanvasColor { get; set; } = new(0.45f, 0.42f, 0.36f);
    [Export] public Color BedrollColor { get; set; } = new(0.35f, 0.3f, 0.45f);
    [Export(PropertyHint.File, "*.ogg,*.wav")] public string FireSoundPath { get; set; } = "res://audio/sonniss/fire/campfire_loop.ogg";
    [Export] public float FireVolumeDb { get; set; } = 2.0f;
    /// <summary>Distance the fire can be heard from, in m.</summary>
    [Export] public float FireHearing { get; set; } = 35.0f;
    /// <summary>The terrace's tilt where the props stand, in degrees (0: the terrace core is level).</summary>
    [Export] public float TerraceSlope { get; set; } = 0.0f;

    public int Index { get; private set; }

    private OmniLight3D? _fire;
    private float _time;
    private bool _tentPlaced;

    private void AddFireSound()
    {
        // Headless runs (tests, smoke) have no real audio output and never release playing streams.
        if (DisplayServer.GetName() == "headless" || !ResourceLoader.Exists(FireSoundPath))
        {
            return;
        }
        AudioStream? stream = ResourceLoader.Load<AudioStream>(FireSoundPath);
        if (stream is AudioStreamWav wav && wav.LoopMode != AudioStreamWav.LoopModeEnum.Forward)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = Mathf.FloorToInt((float)wav.GetLength() * wav.MixRate);
        }
        else if (stream is AudioStreamOggVorbis ogg)
        {
            ogg.Loop = true;
        }
        AudioStreamPlayer3D sound = new()
        {
            Stream = stream,
            Bus = "Ambience",
            VolumeDb = FireVolumeDb,
            MaxDistance = FireHearing,
            UnitSize = 4.0f,
            Position = new Vector3(0.0f, 0.4f, 0.0f),
            Autoplay = true,
        };
        AddChild(sound);
    }

    /// <summary>Creates the station's pieces around its centre (this node's position on the ground).</summary>
    public void Build(int index)
    {
        Index = index;
        Name = $"Station{index}";

        StandardMaterial3D wood = new() { AlbedoColor = WoodColor, Roughness = 1.0f };

        // Fire: a stone ring and logs (models), glowing embers and a flickering light.
        AddMesh(new SphereMesh { Radius = 0.18f, Height = 0.22f, RadialSegments = 8, Rings = 4 },
            new Transform3D(Basis.Identity, new Vector3(0.0f, 0.12f, 0.0f)),
            new StandardMaterial3D { AlbedoColor = FireColor, EmissionEnabled = true, Emission = FireColor, EmissionEnergyMultiplier = 3.0f });
        _fire = new OmniLight3D { LightColor = FireColor, LightEnergy = FireEnergy, OmniRange = FireRange, Position = new Vector3(0.0f, 0.8f, 0.0f), ShadowEnabled = true };
        AddChild(_fire);
        // Flames, sparks and smoke over the embers (the light above already flickers).
        AddChild(new ParticleFire { Name = "Flames", Size = FlameSize, WithLight = false, Position = new Vector3(0.0f, 0.12f, 0.0f) });
        AddFireSound();
        AddProps();

        // The bedroll inside the tent.
        SleepSpot bed = new() { Name = "Bedroll", Position = TentAt + new Vector3(-0.2f, 0.08f, 0.0f) };
        bed.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.9f, 0.12f, 0.75f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = BedrollColor } });
        AddChild(bed);
        if (!_tentPlaced)
        {
            // No tent model: the old lean-to over the bedroll.
            StandardMaterial3D canvas = new() { AlbedoColor = CanvasColor, Roughness = 1.0f };
            AddMesh(new BoxMesh { Size = new Vector3(0.12f, 1.6f, 0.12f) }, new Transform3D(Basis.Identity, TentAt + new Vector3(-0.8f, 0.8f, -1.1f)), wood);
            AddMesh(new BoxMesh { Size = new Vector3(0.12f, 1.6f, 0.12f) }, new Transform3D(Basis.Identity, TentAt + new Vector3(-0.8f, 0.8f, 1.1f)), wood);
            AddMesh(new BoxMesh { Size = new Vector3(2.0f, 0.05f, 2.6f) },
                new Transform3D(new Basis(Vector3.Back, Mathf.DegToRad(40.0f)), TentAt + new Vector3(0.0f, 1.0f, 0.0f)), canvas);
        }

        // The station's name hangs between the two banners at the way in.
        AddChild(WorldLabel.Readable(new Label3D
        {
            Text = $"Station {index}",
            FontSize = LabelFontSize,
            PixelSize = LabelPixelSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            OutlineSize = 10,
            Position = new Vector3(0.0f, LabelHeight, GateZ),
        }, LabelRange));
        // Under the name, what a station is for (the run saves on arrival, see RunKeeper).
        AddChild(WorldLabel.Readable(new Label3D
        {
            Name = "StationPurpose",
            Text = PurposeText,
            FontSize = LabelFontSize / 2,
            PixelSize = LabelPixelSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            OutlineSize = 8,
            Position = new Vector3(0.0f, LabelHeight - LabelFontSize * LabelPixelSize * 0.85f, GateZ),
        }, LabelRange * 0.5f));

        // Beacon: a faint column of light seen from afar, gone once you are in the camp.
        AddMesh(new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.4f, Height = BeaconHeight, RadialSegments = 8 },
            new Transform3D(Basis.Identity, new Vector3(0.0f, BeaconHeight * 0.5f, 0.0f)),
            new StandardMaterial3D
            {
                AlbedoColor = BeaconColor,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha,
                DistanceFadeMinDistance = BeaconFade.X,
                DistanceFadeMaxDistance = BeaconFade.Y,
            });
    }

    public override void _Process(double delta)
    {
        if (_fire == null)
        {
            return;
        }
        _time += (float)delta;
        float flicker = Mathf.Sin(_time * FlickerSpeed) * 0.5f + Mathf.Sin(_time * FlickerSpeed * 2.3f + 1.7f) * 0.5f;
        _fire.LightEnergy = FireEnergy * (1.0f + FlickerAmount * flicker);
    }

    /// <summary>
    /// The camp round the fire (x across the slope, -z downhill, metres from the fire): a stone ring
    /// and logs, log seats, a tent facing the fire (the bedroll inside), the graves of carriers who
    /// came before behind it and a fence, a trader's corner (stall, crates, a log pile, a cart) on the
    /// far side, lanterns, and two banners at the way in. Kenney nature, graveyard, town and castle
    /// kits and Quaternius' medieval village. Heights follow the terrace's tilt down the slope.
    /// </summary>
    private void AddProps()
    {
        const string Nature = "res://art/props/kenney_nature_kit/GLB format/";
        const string Kit = "res://art/props/kenney_graveyard_kit/GLB format/";
        const string Town = "res://art/props/kenney_fantasy_town_kit/GLB format/";
        const string Castle = "res://art/props/kenney_castle_kit/GLB format/";
        const string Village = "res://art/props/quaternius_medieval_village/";
        (string Model, Vector3 At, float Yaw, float Height, PropBody Body, float Mass)[] props =
        {
            (Nature + "campfire_stones.glb", Vector3.Zero, 0.0f, 0.3f, PropBody.None, 0.0f),
            (Nature + "campfire_logs.glb", Vector3.Zero, 20.0f, 0.45f, PropBody.None, 0.0f),
            // Seats: a log on each side and a stump, round the fire.
            (Nature + "log.glb", new Vector3(-2.4f, 0.0f, 0.4f), 90.0f, 0.4f, PropBody.Loose, 45.0f),
            (Nature + "log.glb", new Vector3(2.0f, 0.0f, 1.6f), 50.0f, 0.4f, PropBody.Loose, 45.0f),
            (Nature + "stump_round.glb", new Vector3(0.4f, 0.0f, 2.5f), 0.0f, 0.45f, PropBody.Loose, 30.0f),
            (Kit + "lantern-candle.glb", new Vector3(-1.3f, 0.0f, 1.3f), 0.0f, 0.5f, PropBody.Loose, 3.0f),
            // The tent, open toward the fire.
            (Nature + "tent_detailedOpen.glb", TentAt, TentYaw, 2.2f, PropBody.None, 0.0f),
            // Graves behind the tent, a fence behind them.
            (Kit + "gravestone-round.glb", new Vector3(9.4f, 0.0f, 2.2f), -75.0f, 1.0f, PropBody.Solid, 0.0f),
            (Kit + "gravestone-cross.glb", new Vector3(9.8f, 0.0f, 0.4f), -95.0f, 1.2f, PropBody.Solid, 0.0f),
            (Kit + "gravestone-broken.glb", new Vector3(9.3f, 0.0f, -1.4f), -60.0f, 0.7f, PropBody.Solid, 0.0f),
            (Town + "fence.glb", new Vector3(11.6f, 0.0f, 1.4f), 90.0f, 0.9f, PropBody.Solid, 0.0f),
            (Town + "fence-broken.glb", new Vector3(11.7f, 0.0f, -1.0f), 96.0f, 0.9f, PropBody.Solid, 0.0f),
            // The trader's corner across the fire.
            (Town + "stall-green.glb", new Vector3(-8.2f, 0.0f, 0.4f), 90.0f, 2.4f, PropBody.Solid, 0.0f),
            (Village + "Prop_Crate.gltf", new Vector3(-8.4f, 0.0f, 2.8f), 15.0f, 0.6f, PropBody.Loose, 18.0f),
            (Village + "Prop_Crate.gltf", new Vector3(-7.7f, 0.0f, 3.4f), -20.0f, 0.5f, PropBody.Loose, 18.0f),
            (Nature + "log_stack.glb", new Vector3(-7.8f, 0.0f, -2.4f), 90.0f, 0.7f, PropBody.Solid, 0.0f),
            (Town + "lantern.glb", new Vector3(-3.6f, 0.0f, -3.4f), 0.0f, 2.2f, PropBody.Solid, 0.0f),
            (Town + "lantern.glb", new Vector3(4.2f, 0.0f, 3.0f), 0.0f, 2.2f, PropBody.Solid, 0.0f),
            // The way in (uphill): two banners with the station's name between them.
            (Castle + "flag-banner-long.glb", new Vector3(-GateHalfWidth, 0.0f, GateZ), 0.0f, 3.8f, PropBody.None, 0.0f),
            (Castle + "flag-banner-long.glb", new Vector3(GateHalfWidth, 0.0f, GateZ), 0.0f, 3.8f, PropBody.None, 0.0f),
        };
        float tilt = Mathf.Tan(Mathf.DegToRad(TerraceSlope));
        // A hand cart by the trader's corner the carrier can take along (E at the handles).
        HandCart cart = new() { Name = "HandCart" };
        AddChild(cart);
        cart.Position = new Vector3(CartAt.X, CartAt.Z * tilt + cart.WheelRadius + CartDrop, CartAt.Z);
        cart.RotationDegrees = new Vector3(0.0f, CartYaw, 0.0f);
        foreach ((string path, Vector3 at, float yaw, float height, PropBody kind, float mass) in props)
        {
            if (!ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not PackedScene scene)
            {
                continue;
            }
            Node3D holder = new() { Name = path.GetFile().GetBaseName() };
            holder.AddChild(scene.Instantiate<Node3D>());
            Aabb bounds = DownhillRoguelite.Core.ModelFit.Bounds(holder);
            float scale = height / Mathf.Max(bounds.Size.Y, 0.01f);
            holder.Transform = DownhillRoguelite.Core.ModelFit.FitOnGround(bounds, bounds.Size * scale, true);
            Vector3 fitted = bounds.Size * scale;
            Vector3 spot = new(at.X, at.Z * tilt, at.Z);
            Vector3 turn = new(0.0f, yaw, 0.0f);
            if (kind == PropBody.Loose)
            {
                // Rests a hair above the terrace and settles onto it once the ground is there.
                LooseProp loose = new() { Name = holder.Name, Mass = mass, Position = spot + Vector3.Up * LooseLift, RotationDegrees = turn };
                loose.SetBox(fitted, PropFootprint);
                loose.AddChild(holder);
                AddChild(loose);
            }
            else
            {
                Node3D place = new() { Position = spot, RotationDegrees = turn };
                place.AddChild(holder);
                if (kind == PropBody.Solid)
                {
                    StaticBody3D solid = new() { Name = "Solid", CollisionLayer = SolidLayer, CollisionMask = 0 };
                    Vector3 box = new(fitted.X * PropFootprint, fitted.Y, fitted.Z * PropFootprint);
                    solid.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = box }, Position = new Vector3(0.0f, box.Y * 0.5f, 0.0f) });
                    place.AddChild(solid);
                }
                AddChild(place);
            }
            if (at == TentAt && path.Contains("tent"))
            {
                _tentPlaced = true;
            }
        }
    }

    private void AddMesh(Mesh mesh, Transform3D transform, Material material)
    {
        AddChild(new MeshInstance3D { Mesh = mesh, Transform = transform, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.On });
    }
}
