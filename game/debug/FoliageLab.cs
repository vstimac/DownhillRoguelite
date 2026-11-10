using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The foliage in the wind (debug/FoliageLab.tscn): a clear meadow of the forest's grass with its
/// pines and a few broadleaf trees round it, no fog, the camera at eye height. Keys 0 to 5 set the
/// wind (calm to a full gale and beyond), W turns it, and a walker crosses the meadow so the grass
/// parts round him. Right-drag orbits, the wheel zooms. `-- --wind=0.8` sets the wind (renders).
/// </summary>
public partial class FoliageLab : Node3D
{
    [Export] public string GrassShader { get; set; } = "res://terrain/forest_grass.gdshader";
    [Export] public string[] GrassModels { get; set; } =
    {
        "res://art/environment/forest/grass_0.tscn", "res://art/environment/forest/grass_1.tscn", "res://art/environment/forest/grass_2.tscn",
    };
    [Export] public string[] PineModels { get; set; } =
    {
        "res://art/environment/forest/pine_0.tscn", "res://art/environment/forest/pine_1.tscn", "res://art/environment/forest/pine_2.tscn",
    };
    [Export] public string[] BroadleafModels { get; set; } =
    {
        "res://art/props/kenney_nature_kit/GLB format/tree_oak_dark.glb", "res://art/props/kenney_nature_kit/GLB format/tree_default_fall.glb",
        "res://art/props/kenney_nature_kit/GLB format/tree_fat_darkh.glb",
    };
    [Export] public float MeadowRadius { get; set; } = 22.0f;
    [Export] public int Tufts { get; set; } = 9000;
    [Export] public Vector2 GrassHeight { get; set; } = new(0.15f, 0.42f);
    [Export] public Color GrassColour { get; set; } = new(0.5f, 0.62f, 0.4f);
    [Export] public Color GroundColour { get; set; } = new(0.3f, 0.36f, 0.22f);
    [Export] public float WalkerSpeed { get; set; } = 1.4f;

    public FoliageWind? Wind => _wind;

    private FoliageWind? _wind;
    private Camera3D? _camera;
    private MeshInstance3D? _walker;
    private Label? _info;
    private float _yaw = 20.0f;
    private float _pitch = -6.0f;
    private float _distance = 9.0f;
    private bool _orbiting;
    private float _walk;
    private readonly RandomNumberGenerator _random = new() { Seed = 4127 };

    public override void _Ready()
    {
        Godot.Environment environment = new()
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
            RotationDegrees = new Vector3(-38.0f, -140.0f, 0.0f),
        });
        AddChild(new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(MeadowRadius * 4.0f, MeadowRadius * 4.0f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = GroundColour, Roughness = 1.0f },
        });
        _wind = new FoliageWind { Name = "FoliageWind" };
        AddChild(_wind);
        PlantGrass();
        PlantTrees();
        _walker = new MeshInstance3D
        {
            Name = "Walker",
            Mesh = new CapsuleMesh { Radius = 0.25f, Height = 1.75f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.25f, 0.2f) },
        };
        AddChild(_walker);
        _camera = new Camera3D { Fov = 60.0f, Near = 0.05f, Far = 400.0f };
        AddChild(_camera);
        _camera.MakeCurrent();
        CanvasLayer layer = new() { Layer = 50 };
        AddChild(layer);
        _info = new Label { Position = new Vector2(12, 12) };
        _info.AddThemeFontSizeOverride("font_size", 14);
        _info.AddThemeColorOverride("font_outline_color", Colors.Black);
        _info.AddThemeConstantOverride("outline_size", 4);
        layer.AddChild(_info);
        if (_wind.Override < 0.0f)
        {
            _wind.Override = 0.5f;
        }
    }

    private void PlantGrass()
    {
        Shader? shader = ResourceLoader.Load<Shader>(GrassShader);
        ShaderMaterial material = new() { Shader = shader };
        material.SetShaderParameter("grass_colour", GrassColour);
        material.SetShaderParameter("draw_distance", 60.0f);
        int each = Tufts / Mathf.Max(GrassModels.Length, 1);
        foreach (string model in GrassModels)
        {
            Mesh? mesh = ModelFit.FirstMesh(model, out Transform3D local);
            if (mesh == null)
            {
                continue;
            }
            Aabb bounds = local * mesh.GetAabb();
            MultiMesh batch = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = each };
            for (int i = 0; i < each; i++)
            {
                float radius = MeadowRadius * Mathf.Sqrt(_random.Randf());
                float angle = _random.Randf() * Mathf.Tau;
                Vector3 at = new(Mathf.Cos(angle) * radius, 0.0f, Mathf.Sin(angle) * radius);
                float scale = _random.RandfRange(GrassHeight.X, GrassHeight.Y) / Mathf.Max(bounds.Size.Y, 0.01f);
                Basis basis = new Basis(Vector3.Up, _random.Randf() * Mathf.Tau).Scaled(Vector3.One * scale);
                batch.SetInstanceTransform(i, new Transform3D(basis, at) * local);
            }
            AddChild(new MultiMeshInstance3D { Multimesh = batch, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }

    private void PlantTrees()
    {
        for (int i = 0; i < 26; i++)
        {
            bool pine = i % 4 != 3;
            string[] models = BroadleafModels;
            if (pine)
            {
                models = PineModels;
            }
            string model = models[_random.RandiRange(0, models.Length - 1)];
            Mesh? mesh = ModelFit.FirstMesh(model, out Transform3D local);
            if (mesh == null)
            {
                continue;
            }
            float height = _random.RandfRange(8.0f, 16.0f);
            if (!pine)
            {
                height = _random.RandfRange(5.0f, 8.0f);
            }
            float angle = i * Mathf.Tau / 26.0f + _random.RandfRange(-0.1f, 0.1f);
            float radius = MeadowRadius + _random.RandfRange(0.0f, 10.0f);
            Aabb bounds = local * mesh.GetAabb();
            float scale = height / Mathf.Max(bounds.Size.Y, 0.01f);
            Basis basis = new Basis(Vector3.Up, _random.Randf() * Mathf.Tau).Scaled(Vector3.One * scale);
            Vector3 at = new(Mathf.Cos(angle) * radius, -bounds.Position.Y * scale, Mathf.Sin(angle) * radius);
            AddChild(new MeshInstance3D { Mesh = FoliageMaterials.WindReady(mesh), Transform = new Transform3D(basis, at) * local });
        }
    }

    public override void _Process(double delta)
    {
        _walk += (float)delta * WalkerSpeed;
        // The walker crosses the meadow back and forth, in front of the camera.
        Vector3 feet = new(Mathf.Sin(_walk / 6.0f) * 6.0f, 0.0f, 2.0f);
        if (_walker != null)
        {
            _walker.Position = feet + Vector3.Up * 0.875f;
        }
        if (_wind != null)
        {
            _wind.Pusher = feet;
        }
        if (_camera != null)
        {
            Basis turn = Basis.FromEuler(new Vector3(Mathf.DegToRad(_pitch), Mathf.DegToRad(_yaw), 0.0f));
            Vector3 focus = new(0.0f, 1.2f, 0.0f);
            _camera.LookAtFromPosition(focus + turn * Vector3.Back * _distance, focus, Vector3.Up);
        }
        if (_info != null && _wind != null)
        {
            _info.Text = $"Foliage lab   wind {_wind.Strength:0.00} (target {_wind.Override:0.00})   direction {_wind.Direction:0.00}\n"
                + "0-5: wind (calm .. gale and beyond)   W: turn the wind   Right-drag: orbit   Wheel: zoom";
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Right)
            {
                _orbiting = button.Pressed;
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp)
            {
                _distance = Mathf.Max(_distance * 0.9f, 1.5f);
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown)
            {
                _distance = Mathf.Min(_distance * 1.1f, 60.0f);
            }
            return;
        }
        if (@event is InputEventMouseMotion motion && _orbiting)
        {
            _yaw -= motion.Relative.X * 0.3f;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * 0.3f, -80.0f, 20.0f);
            return;
        }
        if (@event is not InputEventKey key || !key.Pressed || _wind == null)
        {
            return;
        }
        int number = (int)key.Keycode - (int)Key.Key0;
        if (number >= 0 && number <= 5)
        {
            _wind.Override = number * 0.25f;
        }
        if (key.Keycode == Key.W)
        {
            Vector2 direction = _wind.StartDirection.Rotated(Mathf.DegToRad(45.0f));
            _wind.StartDirection = direction;
            GameEvents.PublishWeather(GameEvents.CurrentWeather with { Wind = new Vector3(direction.X, 0.0f, direction.Y) * 5.0f });
        }
    }
}
