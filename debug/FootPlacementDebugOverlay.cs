using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug overlay for foot IK: IK targets (green when planted, yellow when swinging), predicted
/// landing points, ground normals under each foot and knee pole lines. Reads FootPlacementSnapshot
/// events only. Hidden with F3.
/// </summary>
public partial class FootPlacementDebugOverlay : Node
{
    [Export] public Color PlantedColor { get; set; } = new(0.2f, 1.0f, 0.3f);
    [Export] public Color SwingColor { get; set; } = new(1.0f, 0.85f, 0.2f);
    [Export] public Color LandingColor { get; set; } = new(1.0f, 0.4f, 1.0f);
    [Export] public Color NormalColor { get; set; } = new(0.3f, 0.6f, 1.0f);
    [Export] public Color PoleColor { get; set; } = new(0.6f, 0.6f, 0.6f);
    [Export] public float MarkerSize { get; set; } = 0.06f;
    [Export] public float NormalLength { get; set; } = 0.3f;

    public override void _Ready()
    {
        GameEvents.FootPlacementUpdated += OnFootPlacementUpdated;
    }

    public override void _ExitTree()
    {
        GameEvents.FootPlacementUpdated -= OnFootPlacementUpdated;
    }

    private void OnFootPlacementUpdated(FootPlacementSnapshot snapshot)
    {
        DrawFoot(snapshot.Left);
        DrawFoot(snapshot.Right);
    }

    private void DrawFoot(FootSnapshot foot)
    {
        if (foot.Weight <= 0.0f)
        {
            return;
        }

        Color targetColor = SwingColor;
        if (foot.Planted)
        {
            targetColor = PlantedColor;
        }
        DebugDraw3D.Cross(foot.Target, targetColor, MarkerSize);
        DebugDraw3D.Line(foot.Target, foot.Pole, PoleColor);

        if (foot.HasGround)
        {
            DebugDraw3D.Arrow(foot.GroundPoint, foot.GroundPoint + foot.GroundNormal * NormalLength, NormalColor, MarkerSize);
        }
        if (!foot.Planted)
        {
            DebugDraw3D.Cross(foot.Landing, LandingColor, MarkerSize);
        }
    }
}
