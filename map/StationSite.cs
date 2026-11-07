using Godot;

namespace DownhillRoguelite.Map;

/// <summary>
/// A station on its terrace, built from primitives until real models exist: a fire with flickering
/// light, a lean-to with a bedroll (SleepSpot), a sign, and a tall faint beacon that can be seen
/// from far down the slope and through the dark. The fire crackles (audio/ambience/fire.wav), so
/// you can hear a station before you see it.
/// </summary>
public partial class StationSite : Node3D
{
    [Export] public Color FireColor { get; set; } = new(1.0f, 0.55f, 0.2f);
    [Export] public float FireEnergy { get; set; } = 3.0f;
    [Export] public float FireRange { get; set; } = 16.0f;
    [Export] public float FlickerAmount { get; set; } = 0.25f;
    [Export] public float FlickerSpeed { get; set; } = 9.0f;
    [Export] public Color BeaconColor { get; set; } = new(1.0f, 0.75f, 0.4f, 0.35f);
    [Export] public float BeaconHeight { get; set; } = 40.0f;
    /// <summary>Lean-to offset from the fire, sideways from the path, in m.</summary>
    [Export] public float ShelterOffset { get; set; } = 4.5f;
    [Export] public Color WoodColor { get; set; } = new(0.4f, 0.28f, 0.18f);
    [Export] public Color CanvasColor { get; set; } = new(0.45f, 0.42f, 0.36f);
    [Export] public Color BedrollColor { get; set; } = new(0.35f, 0.3f, 0.45f);
    [Export(PropertyHint.File, "*.wav")] public string FireSoundPath { get; set; } = "res://audio/ambience/fire.wav";
    [Export] public float FireVolumeDb { get; set; } = 2.0f;
    /// <summary>Distance the fire can be heard from, in m.</summary>
    [Export] public float FireHearing { get; set; } = 35.0f;

    public int Index { get; private set; }

    private OmniLight3D? _fire;
    private float _time;

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
        StandardMaterial3D canvas = new() { AlbedoColor = CanvasColor, Roughness = 1.0f };

        // Fire: crossed logs, glowing embers, light.
        AddMesh(new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 1.0f, RadialSegments = 6 },
            new Transform3D(new Basis(Vector3.Back, Mathf.Pi * 0.5f), new Vector3(0.0f, 0.1f, 0.0f)), wood);
        AddMesh(new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 1.0f, RadialSegments = 6 },
            new Transform3D(new Basis(Vector3.Right, Mathf.Pi * 0.5f), new Vector3(0.0f, 0.12f, 0.0f)), wood);
        AddMesh(new SphereMesh { Radius = 0.22f, Height = 0.3f, RadialSegments = 8, Rings = 4 },
            new Transform3D(Basis.Identity, new Vector3(0.0f, 0.18f, 0.0f)),
            new StandardMaterial3D { AlbedoColor = FireColor, EmissionEnabled = true, Emission = FireColor, EmissionEnergyMultiplier = 3.0f });
        _fire = new OmniLight3D { LightColor = FireColor, LightEnergy = FireEnergy, OmniRange = FireRange, Position = new Vector3(0.0f, 0.8f, 0.0f), ShadowEnabled = true };
        AddChild(_fire);
        AddFireSound();

        // Lean-to: two posts and a sloping roof, open toward the fire, with the bedroll under it.
        Vector3 shelter = new(ShelterOffset, 0.0f, 0.0f);
        AddMesh(new BoxMesh { Size = new Vector3(0.12f, 1.6f, 0.12f) }, new Transform3D(Basis.Identity, shelter + new Vector3(-0.8f, 0.8f, -1.1f)), wood);
        AddMesh(new BoxMesh { Size = new Vector3(0.12f, 1.6f, 0.12f) }, new Transform3D(Basis.Identity, shelter + new Vector3(-0.8f, 0.8f, 1.1f)), wood);
        AddMesh(new BoxMesh { Size = new Vector3(2.0f, 0.05f, 2.6f) },
            new Transform3D(new Basis(Vector3.Back, Mathf.DegToRad(40.0f)), shelter + new Vector3(0.0f, 1.0f, 0.0f)), canvas);
        SleepSpot bed = new() { Name = "Bedroll", Position = shelter + new Vector3(0.2f, 0.1f, 0.0f) };
        bed.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.15f, 2.0f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = BedrollColor } });
        AddChild(bed);

        AddChild(new Label3D
        {
            Text = $"Station {index}",
            FontSize = 96,
            PixelSize = 0.01f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            OutlineSize = 12,
            Position = shelter + new Vector3(0.0f, 2.6f, 0.0f),
        });

        // Beacon: a faint column of light seen from afar.
        AddMesh(new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.4f, Height = BeaconHeight, RadialSegments = 8 },
            new Transform3D(Basis.Identity, new Vector3(0.0f, BeaconHeight * 0.5f, 0.0f)),
            new StandardMaterial3D
            {
                AlbedoColor = BeaconColor,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
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

    private void AddMesh(Mesh mesh, Transform3D transform, Material material)
    {
        AddChild(new MeshInstance3D { Mesh = mesh, Transform = transform, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.On });
    }
}
