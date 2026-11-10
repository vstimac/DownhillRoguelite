using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// Where a relic lies: a small worn stone with two candles burning beside it, so a relic is seen
/// from the path at dusk. Only scenery; the relic before it is the item. Models from the Kenney
/// graveyard kit (a plain block and a lamp stand in when a file is missing).
/// </summary>
public partial class RelicShrine : Node3D
{
    [Export(PropertyHint.File, "*.glb")] public string StoneModel { get; set; } = "res://art/props/kenney_graveyard_kit/GLB format/gravestone-round.glb";
    [Export(PropertyHint.File, "*.glb")] public string CandleModel { get; set; } = "res://art/props/kenney_graveyard_kit/GLB format/candle-multiple.glb";
    [Export] public float StoneHeight { get; set; } = 0.9f;
    [Export] public float CandleHeight { get; set; } = 0.28f;
    [Export] public Color StoneColor { get; set; } = new(0.55f, 0.53f, 0.5f);
    [Export] public Color CandleColor { get; set; } = new(1.0f, 0.75f, 0.4f);
    [Export] public float CandleEnergy { get; set; } = 0.8f;
    [Export] public float CandleRange { get; set; } = 4.0f;

    public override void _Ready()
    {
        if (AddModel(StoneModel, StoneHeight, new Vector3(0.0f, 9.0f, -4.0f), Vector3.Zero) <= 0.0f)
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.5f, StoneHeight, 0.18f) },
                Position = Vector3.Up * StoneHeight * 0.5f,
                MaterialOverride = new StandardMaterial3D { AlbedoColor = StoneColor, Roughness = 1.0f },
            });
        }
        AddCandle(new Vector3(-0.45f, 0.0f, 0.35f));
        AddCandle(new Vector3(0.5f, 0.0f, 0.3f));
    }

    private void AddCandle(Vector3 at)
    {
        AddModel(CandleModel, CandleHeight, Vector3.Zero, at);
        AddChild(new OmniLight3D { LightColor = CandleColor, LightEnergy = CandleEnergy, OmniRange = CandleRange, Position = at + Vector3.Up * (CandleHeight + 0.15f) });
    }

    private float AddModel(string path, float height, Vector3 rotation, Vector3 at)
    {
        if (!ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not PackedScene scene)
        {
            return 0.0f;
        }
        Node3D holder = new() { Name = "Model" };
        holder.AddChild(scene.Instantiate<Node3D>());
        Aabb bounds = ModelFit.Bounds(holder);
        float scale = height / Mathf.Max(bounds.Size.Y, 0.01f);
        holder.Transform = ModelFit.FitOnGround(bounds, bounds.Size * scale, true);
        Node3D place = new() { Position = at, RotationDegrees = rotation };
        place.AddChild(holder);
        AddChild(place);
        return height;
    }
}
