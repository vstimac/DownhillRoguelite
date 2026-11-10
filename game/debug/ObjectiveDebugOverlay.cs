using System.Text;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Progression;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Purpose-patch debug: the goals as the director publishes them (kind, title, progress, done), the
/// guide step, which station the carrier is sheltered at and the highest reached, the nearest point
/// of interest, and the points planned for the current stage (used or not, how far). A post over each
/// planned point (green unused, grey used). Hidden with F3 like the other overlays.
/// </summary>
public partial class ObjectiveDebugOverlay : CanvasLayer
{
    [Export] public ObjectiveDirector? Director { get; set; }
    [Export] public WaysidePlacer? Wayside { get; set; }
    [Export] public int FontSize { get; set; } = 15;
    [Export] public Vector2 Margin { get; set; } = new(16.0f, 300.0f);

    public string Text => _label?.Text ?? "";

    private Label? _label;
    private RunState? _run;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _label = new Label { Position = Margin };
        _label.AddThemeFontSizeOverride("font_size", FontSize);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", FontSize / 4);
        AddChild(_label);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_label == null)
        {
            return;
        }
        Visible = DebugDraw3D.Enabled;
        if (!Visible)
        {
            return;
        }
        StringBuilder text = new();
        text.Append("Goals\n");
        foreach (ObjectiveNote note in GameEvents.CurrentObjectives)
        {
            text.Append("  ").Append(note.Kind).Append(": ").Append(note.Title);
            if (note.Progress.Length > 0)
            {
                text.Append(" [").Append(note.Progress).Append(']');
            }
            if (note.Failed)
            {
                text.Append(" (failed)");
            }
            else if (note.Done)
            {
                text.Append(" (done)");
            }
            text.Append('\n');
        }
        if (Director != null)
        {
            ObjectiveBook book = Director.Book;
            string step = "none";
            if (book.CurrentStep() != null)
            {
                step = book.CurrentStep()!.Value.ToString();
            }
            string guide = "off";
            if (book.GuideOn)
            {
                guide = "on";
            }
            text.Append("  guide ").Append(guide).Append(", step ").Append(step)
                .Append(" · highest station ").Append(book.Highest).Append(" · sheltered at ").Append(book.Shelter)
                .Append(" · finished ").Append(book.Finished.Count).Append('\n');
        }
        WaysideNote? near = GameEvents.NearestWayside;
        if (near != null)
        {
            text.Append("Nearest wayside: ").Append(near.Value.Title).Append(", ").Append(near.Value.Distance.ToString("0")).Append(" m ").Append(near.Value.Side).Append('\n');
        }
        IRouteQuery? route = _run?.Route;
        if (Wayside != null && route != null)
        {
            Vector3 here = GameEvents.CarrierPosition;
            int stage = route.StageAt(here.Z);
            text.Append("Stage ").Append(stage).Append(" wayside:");
            foreach (WaysidePlacer.Placement placement in Wayside.PlanFor(route, stage))
            {
                bool used = Wayside.IsUsed(placement.Key);
                text.Append("  ").Append(placement.Definition.Id).Append(' ').Append(placement.Position.DistanceTo(here).ToString("0")).Append(" m");
                if (used)
                {
                    text.Append(" (used)");
                }
                Color colour = new(0.3f, 0.9f, 0.3f);
                if (used)
                {
                    colour = new Color(0.5f, 0.5f, 0.5f);
                }
                DebugDraw3D.Line(placement.Position, placement.Position + Vector3.Up * 6.0f, colour);
            }
            text.Append('\n');
        }
        _label.Text = text.ToString();
    }
}
