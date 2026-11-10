using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// A plain health bar over an enemy: a dark strip with a red fill that shrinks from the right, always
/// turned to the camera and drawn over everything.
/// </summary>
public partial class EnemyHealthBar : Node3D
{
    [Export] public float Width { get; set; } = 0.8f;
    [Export] public float Thickness { get; set; } = 0.08f;
    /// <summary>Margin of the dark strip around the fill (m).</summary>
    [Export] public float Border { get; set; } = 0.015f;
    [Export] public Color Back { get; set; } = new(0.05f, 0.05f, 0.05f, 0.85f);
    [Export] public Color Fill { get; set; } = new(0.8f, 0.12f, 0.1f, 1.0f);

    /// <summary>Health left, 0..1.</summary>
    public float Fraction { get; private set; } = 1.0f;

    private MeshInstance3D? _fill;

    public override void _Ready()
    {
        AddChild(new MeshInstance3D
        {
            Name = "Back",
            Mesh = new QuadMesh { Size = new Vector2(Width + Border * 2.0f, Thickness + Border * 2.0f) },
            MaterialOverride = Flat(Back, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        // The fill's origin is its left end, so scaling it shrinks it toward the left.
        _fill = new MeshInstance3D
        {
            Name = "Fill",
            Mesh = new QuadMesh { Size = new Vector2(Width, Thickness), CenterOffset = new Vector3(Width * 0.5f, 0.0f, 0.001f) },
            MaterialOverride = Flat(Fill, 1),
            Position = Vector3.Left * Width * 0.5f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_fill);
        Set(Fraction);
    }

    public void Set(float fraction)
    {
        Fraction = Mathf.Clamp(fraction, 0.0f, 1.0f);
        if (_fill != null)
        {
            _fill.Scale = new Vector3(Mathf.Max(Fraction, 0.001f), 1.0f, 1.0f);
            _fill.Visible = Fraction > 0.0f;
        }
    }

    public override void _Process(double delta)
    {
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera == null)
        {
            return;
        }
        // Face the camera (a quad's front is +Z), upright.
        Vector3 toCamera = camera.GlobalPosition - GlobalPosition;
        toCamera.Y = 0.0f;
        if (toCamera.LengthSquared() < 0.0001f)
        {
            return;
        }
        GlobalBasis = Basis.LookingAt(-toCamera.Normalized(), Vector3.Up);
    }

    private static StandardMaterial3D Flat(Color colour, int priority)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = colour,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = priority,
        };
    }
}
