using Godot;

namespace DownhillRoguelite.Map;

/// <summary>
/// A waypoint set on the map, standing in the world: a tall pole with a red flag and a light, and
/// its number and distance drawn through fog and terrain so it can always be found.
/// </summary>
public partial class WaypointMarker : Node3D
{
    [Export] public Color FlagColor { get; set; } = new(0.85f, 0.15f, 0.12f);
    [Export] public float PoleHeight { get; set; } = 3.0f;

    public int Number { get; set; }
    public string LabelText => _label?.Text ?? "";

    private Label3D? _label;

    public override void _Ready()
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = PoleHeight, RadialSegments = 6 },
            Position = Vector3.Up * PoleHeight * 0.5f,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.25f, 0.15f) },
        });
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.45f, 0.02f) },
            Position = new Vector3(0.35f, PoleHeight - 0.3f, 0.0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = FlagColor, EmissionEnabled = true, Emission = FlagColor, EmissionEnergyMultiplier = 0.6f, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
        });
        AddChild(new OmniLight3D { LightColor = FlagColor, LightEnergy = 1.2f, OmniRange = 6.0f, Position = Vector3.Up * (PoleHeight + 0.2f) });
        _label = new Label3D
        {
            FontSize = 28,
            PixelSize = 0.004f,
            FixedSize = true,
            NoDepthTest = true,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color(1.0f, 0.75f, 0.7f),
            OutlineSize = 8,
            Position = Vector3.Up * (PoleHeight + 0.6f),
        };
        AddChild(_label);
    }

    /// <summary>Updates the label with the distance from the carrier.</summary>
    public void ShowDistance(Vector3 from)
    {
        if (_label == null)
        {
            return;
        }
        Vector3 offset = GlobalPosition - from;
        float distance = new Vector2(offset.X, offset.Z).Length();
        _label.Text = $"Cairn {Number} · {distance:0} m away";
    }
}
