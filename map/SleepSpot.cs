using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Map;

/// <summary>
/// The bedroll in a station's lean-to. E sleeps until dawn once it's late enough (WorldClock
/// SleepFromHour, or any time at night); before that the in-world prompt says when.
/// </summary>
public partial class SleepSpot : Node3D, IInteractable
{
    [Export] public int LabelFontSize { get; set; } = 40;
    [Export] public float LabelPixelSize { get; set; } = 0.004f;
    [Export] public float LabelHeight { get; set; } = 0.9f;

    public Vector3 InteractionPoint => GlobalPosition;

    private Label3D? _label;
    private WorldClock? _clock;
    private bool _focused;

    public override void _Ready()
    {
        AddToGroup(IInteractable.Group);
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        _label = new Label3D
        {
            FontSize = LabelFontSize,
            PixelSize = LabelPixelSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            OutlineSize = 8,
            Position = Vector3.Up * LabelHeight,
            Visible = false,
        };
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        if (_label == null || !_focused || _clock == null)
        {
            return;
        }
        if (_clock.CanSleepNow)
        {
            _label.Text = "E  Sleep until dawn";
        }
        else
        {
            _label.Text = $"Too early to sleep (from {_clock.SleepFromHour:00}:00)";
        }
    }

    public void SetFocused(bool focused)
    {
        _focused = focused;
        if (_label != null)
        {
            _label.Visible = focused;
        }
    }

    public bool Interact(Node3D interactor)
    {
        if (_clock == null || !GameEvents.InShelter)
        {
            return false;
        }
        return _clock.SleepUntilMorning() > 0.0f;
    }
}
