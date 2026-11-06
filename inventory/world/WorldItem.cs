using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// An item lying in the world. Shows a placeholder box in its colour (scaled up a little so it's
/// visible on the slope) and, when the carrier is close and facing it, an in-world label. E packs it
/// into the carrier's inventory if there's room; otherwise the label says so and it stays.
/// </summary>
public partial class WorldItem : Node3D, IInteractable
{
    [Export] public ItemDefinition? Definition { get; set; }
    /// <summary>Visual scale of the box compared to the item's real size.</summary>
    [Export] public float VisualScale { get; set; } = 1.6f;
    [Export] public float LabelHeight { get; set; } = 0.45f;
    [Export] public int LabelFontSize { get; set; } = 36;
    [Export] public float LabelPixelSize { get; set; } = 0.004f;
    [Export] public float NoRoomMessageTime { get; set; } = 1.5f;
    /// <summary>Cell size used to turn the item's size in cells into metres.</summary>
    [Export] public float CellSize { get; set; } = 0.05f;

    /// <summary>Raised when the item has been packed and is about to disappear.</summary>
    public event System.Action<WorldItem>? Taken;

    /// <summary>Spawner bookkeeping: which generated slot this item came from.</summary>
    public string SlotKey { get; set; } = "";

    public Vector3 InteractionPoint => GlobalPosition;

    private Label3D? _label;
    private float _noRoomTimer;
    private bool _focused;

    public override void _Ready()
    {
        AddToGroup(IInteractable.Group);
        if (Definition == null)
        {
            return;
        }

        Vector3 size = (Vector3)Definition.SizeCells * CellSize * VisualScale;
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            Position = Vector3.Up * size.Y * 0.5f,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = Definition.DebugColor,
                EmissionEnabled = true,
                Emission = Definition.DebugColor * 0.25f,
            },
        });

        _label = new Label3D
        {
            Text = LabelText(),
            FontSize = LabelFontSize,
            PixelSize = LabelPixelSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            OutlineSize = 8,
            Position = Vector3.Up * (size.Y + LabelHeight),
            Visible = false,
        };
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        if (_noRoomTimer > 0.0f)
        {
            _noRoomTimer -= (float)delta;
            if (_noRoomTimer <= 0.0f && _label != null)
            {
                _label.Text = LabelText();
                _label.Visible = _focused;
            }
        }
    }

    public void SetFocused(bool focused)
    {
        _focused = focused;
        if (_label != null && _noRoomTimer <= 0.0f)
        {
            _label.Visible = focused;
        }
    }

    public bool Interact(Node3D interactor)
    {
        if (Definition == null)
        {
            return false;
        }
        CarrierInventory? inventory = interactor.GetNodeOrNull<CarrierInventory>("Inventory");
        if (inventory == null || !inventory.TryAdd(Definition))
        {
            if (_label != null)
            {
                _label.Text = "No room in the pack";
                _label.Visible = true;
                _noRoomTimer = NoRoomMessageTime;
            }
            return false;
        }

        Taken?.Invoke(this);
        RemoveFromGroup(IInteractable.Group);
        QueueFree();
        return true;
    }

    private string LabelText()
    {
        if (Definition == null)
        {
            return "";
        }
        return $"E  {Definition.DisplayName}  {Definition.MassKg:0.0} kg";
    }
}
