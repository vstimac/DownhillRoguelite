using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Map;

/// <summary>
/// A station on its terrace: a camp round a fire with flickering light (stone ring, log seats), a
/// tent with a bedroll (SleepSpot), graves, a trader's corner, banners at the way in with the
/// station's name, and a tall faint beacon that can be seen
/// from far down the slope and through the dark. The fire crackles (audio/ambience/fire.wav), so
/// you can hear a station before you see it.
/// </summary>
public partial class StationSite : Node3D
{
    [Export] public int LabelFontSize { get; set; } = 56;
    [Export] public float LabelPixelSize { get; set; } = 0.006f;
    /// <summary>Beyond this the station's name fades; the beacon still shows the way.</summary>
    [Export] public float LabelRange { get; set; } = 60.0f;

    [Export] public Color FireColor { get; set; } = new(1.0f, 0.55f, 0.2f);
    [Export] public float FireEnergy { get; set; } = 3.0f;
    [Export] public float FireRange { get; set; } = 16.0f;
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
    [Export] public Color WoodColor { get; set; } = new(0.4f, 0.28f, 0.18f);
    [Export] public Color CanvasColor { get; set; } = new(0.45f, 0.42f, 0.36f);
    [Export] public Color BedrollColor { get; set; } = new(0.35f, 0.3f, 0.45f);
    [Export(PropertyHint.File, "*.wav")] public string FireSoundPath { get; set; } = "res://audio/ambience/fire.wav";
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
        (string Model, Vector3 At, float Yaw, float Height)[] props =
        {
            (Nature + "campfire_stones.glb", Vector3.Zero, 0.0f, 0.3f),
            (Nature + "campfire_logs.glb", Vector3.Zero, 20.0f, 0.45f),
            // Seats: a log on each side and a stump, round the fire.
            (Nature + "log.glb", new Vector3(-2.4f, 0.0f, 0.4f), 90.0f, 0.4f),
            (Nature + "log.glb", new Vector3(2.0f, 0.0f, 1.6f), 50.0f, 0.4f),
            (Nature + "stump_round.glb", new Vector3(0.4f, 0.0f, 2.5f), 0.0f, 0.45f),
            (Kit + "lantern-candle.glb", new Vector3(-1.3f, 0.0f, 1.3f), 0.0f, 0.5f),
            // The tent, open toward the fire.
            (Nature + "tent_detailedOpen.glb", TentAt, TentYaw, 2.2f),
            // Graves behind the tent, a fence behind them.
            (Kit + "gravestone-round.glb", new Vector3(9.4f, 0.0f, 2.2f), -75.0f, 1.0f),
            (Kit + "gravestone-cross.glb", new Vector3(9.8f, 0.0f, 0.4f), -95.0f, 1.2f),
            (Kit + "gravestone-broken.glb", new Vector3(9.3f, 0.0f, -1.4f), -60.0f, 0.7f),
            (Town + "fence.glb", new Vector3(11.6f, 0.0f, 1.4f), 90.0f, 0.9f),
            (Town + "fence-broken.glb", new Vector3(11.7f, 0.0f, -1.0f), 96.0f, 0.9f),
            // The trader's corner across the fire.
            (Town + "stall-green.glb", new Vector3(-8.2f, 0.0f, 0.4f), 90.0f, 2.4f),
            (Village + "Prop_Crate.gltf", new Vector3(-8.4f, 0.0f, 2.8f), 15.0f, 0.6f),
            (Village + "Prop_Crate.gltf", new Vector3(-7.7f, 0.0f, 3.4f), -20.0f, 0.5f),
            (Nature + "log_stack.glb", new Vector3(-7.8f, 0.0f, -2.4f), 90.0f, 0.7f),
            (Town + "cart.glb", new Vector3(-6.4f, 0.0f, 5.2f), 25.0f, 1.0f),
            (Town + "lantern.glb", new Vector3(-3.6f, 0.0f, -3.4f), 0.0f, 2.2f),
            (Town + "lantern.glb", new Vector3(4.2f, 0.0f, 3.0f), 0.0f, 2.2f),
            // The way in (uphill): two banners with the station's name between them.
            (Castle + "flag-banner-long.glb", new Vector3(-GateHalfWidth, 0.0f, GateZ), 0.0f, 3.8f),
            (Castle + "flag-banner-long.glb", new Vector3(GateHalfWidth, 0.0f, GateZ), 0.0f, 3.8f),
        };
        float tilt = Mathf.Tan(Mathf.DegToRad(TerraceSlope));
        foreach ((string path, Vector3 at, float yaw, float height) in props)
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
            Node3D place = new() { Position = new Vector3(at.X, at.Z * tilt, at.Z), RotationDegrees = new Vector3(0.0f, yaw, 0.0f) };
            place.AddChild(holder);
            AddChild(place);
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
