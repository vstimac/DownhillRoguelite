using DownhillRoguelite.Core;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>Run-local finite resupply. Only an acknowledged inventory transfer spends a flask.</summary>
public partial class StationWaterReserve : Node3D, IInteractable, IDescribed
{
    [Export] public int Capacity { get; set; } = 3;
    [Export(PropertyHint.File, "*.tres")] public string WaterItemPath { get; set; } = "res://inventory/items/water_flask.tres";
    public bool Restored { get; private set; }
    public int Remaining { get; private set; }
    private Label3D? _label;
    public Vector3 InteractionPoint => GlobalPosition + Vector3.Up * 0.8f;
    public string Title => "Station 1 water reserve";
    public string Description => "Deliver the recovered pump to the delivery table to restore this reserve. The reserve lasts for this run.";
    public string Details => Remaining + " / " + Capacity + " filled flasks available. Each flask takes real space and weight. No stock is spent if your pack is full.";
    public string Prompt
    {
        get
        {
            if (!Restored) { return "Pump missing: recover it from the orange-flagged workshop"; }
            if (Remaining <= 0) { return "Reserve empty"; }
            return "E  Take one water flask";
        }
    }
    public Resource? IconSource => null;

    public override void _Ready()
    {
        AddToGroup(IInteractable.Group);
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0.4f, Height = 1.0f },
            Position = Vector3.Up * 0.5f, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.45f, 0.55f), Metallic = 0.5f } });
        _label = new Label3D { FontSize = 32, PixelSize = 0.005f, Position = Vector3.Up * 1.6f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, OutlineSize = 8 };
        AddChild(WorldLabel.Readable(_label, 35));
        Refresh();
    }

    public bool Restore()
    {
        if (Restored) { return false; }
        Restored = true;
        Remaining = System.Math.Max(Capacity, 0);
        Refresh();
        return true;
    }

    private void Refresh()
    {
        if (_label != null)
        {
            _label.Text = "Water reserve · dry: its pump is up at the workshop";
            if (Restored && Remaining > 0) { _label.Text = "Water reserve · E: take a flask (" + Remaining + " left)"; }
            else if (Restored) { _label.Text = "Water reserve · empty"; }
        }
        if (ReferenceEquals(GameEvents.Focused, this)) { GameEvents.PublishFocus(this); }
    }
    public void SetFocused(bool focused) { }
    public bool Interact(Node3D interactor)
    {
        if (!Restored || Remaining <= 0) { return false; }
        if (!GameEvents.TryGrantItem(WaterItemPath))
        {
            GameEvents.PublishHint("No room for a flask. The water stays in the reserve.");
            return false;
        }
        Remaining--;
        Refresh();
        return true;
    }
}
