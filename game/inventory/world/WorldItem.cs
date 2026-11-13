using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// An item lying in the world, drawn as the shape it takes in the backpack: a translucent block of
/// its cells with every cell edge glowing in its category's colour (food warm, water blue, supplies
/// pale, cargo violet), gently pulsing so it stands out on the slope. When the carrier comes within
/// IndicatorRadius an arrow bobs above it, pointing down at it. Close and facing it, an in-world
/// label shows; E packs it into the carrier's inventory if there's room, otherwise the label says so.
/// </summary>
public partial class WorldItem : Node3D, IInteractable, IDescribed
{
    [Export] public ItemDefinition? Definition { get; set; }
    /// <summary>Per-copy wear carried through a drop and pickup; -1 means newly found gear.</summary>
    [Export] public float Durability { get; set; } = -1.0f;
    public ItemAppearance Appearance { get; set; }
    public string InstanceId { get; set; } = System.Guid.NewGuid().ToString("N");
    /// <summary>Visual scale of the box compared to the item's real size.</summary>
    [Export] public float VisualScale { get; set; } = 3.0f;
    [Export] public float LabelHeight { get; set; } = 0.45f;
    [Export] public int LabelFontSize { get; set; } = 36;
    [Export] public float LabelPixelSize { get; set; } = 0.004f;
    [Export] public float NoRoomMessageTime { get; set; } = 1.5f;
    /// <summary>Cell size used to turn the item's size in cells into metres.</summary>
    [Export] public float CellSize { get; set; } = 0.05f;
    /// <summary>The arrow shows when the carrier is this close, in m.</summary>
    [Export] public float IndicatorRadius { get; set; } = 28.0f;
    /// <summary>...and hides again this close (the label takes over), in m.</summary>
    [Export] public float IndicatorHideDistance { get; set; } = 2.0f;
    [Export] public float IndicatorHeight { get; set; } = 1.3f;
    [Export] public Color FoodColor { get; set; } = new(1.0f, 0.65f, 0.25f);
    [Export] public Color WaterColor { get; set; } = new(0.35f, 0.7f, 1.0f);
    [Export] public Color SupplyColor { get; set; } = new(0.9f, 0.9f, 0.8f);
    [Export] public Color CargoColor { get; set; } = new(0.75f, 0.5f, 1.0f);

    public bool IndicatorVisible => _arrow != null && _arrow.Visible;
    /// <summary>The fitted model (null when the item has none and shows a block).</summary>
    public Node3D? ModelNode { get; private set; }

    /// <summary>Raised when the item has been packed and is about to disappear.</summary>
    public event System.Action<WorldItem>? Taken;

    /// <summary>Spawner bookkeeping: which generated slot this item came from.</summary>
    public string SlotKey { get; set; } = "";
    /// <summary>Shown while the co-op host is asked whether this one is still free.</summary>
    [Export] public string ClaimingText { get; set; } = "Reaching for it...";

    public Vector3 InteractionPoint => GlobalPosition;
    /// <summary>The group the field map reads for supply dots, and the meta key holding each one's colour.</summary>
    public const string MapLootGroup = "map_loot";
    public const string MapColourMeta = "map_colour";

    public string Title => Definition?.DisplayName ?? "";
    public string Description => ItemText.Describe(Definition);
    public string Details => ItemText.Properties(Definition);
    public string Prompt => "E  Pick up";
    public Resource? IconSource => Definition;

    private Label3D? _label;
    private float _noRoomTimer;
    private bool _focused;
    private MeshInstance3D? _arrow;
    private StandardMaterial3D? _glow;
    private float _time;

    public override void _Ready()
    {
        if (SlotKey.Length > 0 && GameEvents.WasSharedPickupTaken(SlotKey)) { QueueFree(); return; }
        AddToGroup(IInteractable.Group);
        if (Definition == null)
        {
            return;
        }

        Vector3 size = (Vector3)Definition.SizeCells * CellSize * VisualScale;
        Color glow = CategoryColor(Definition.Category);
        // The field map draws seen supplies as dots in their colour (a group, so the map needn't know items).
        AddToGroup(MapLootGroup);
        SetMeta(MapColourMeta, glow);
        _time = (float)(GetInstanceId() % 1000) * 0.01f;
        Node3D? holder = ItemVisual.BuildModel(Definition);
        if (holder != null)
        {
            // The item's model (or a piece of one), fitted (proportions kept) into its cell block.
            holder.Transform = ModelFit.FitOnGround(ModelFit.Bounds(holder), size, true);
            AddChild(holder);
            ModelNode = holder;
        }
        else
        {
            // No model yet: an ink outline drawing of what it is, standing in its block.
            Node3D outline = ItemVisual.BuildPlaceholder(Definition, size);
            AddChild(outline);
            ModelNode = outline;
        }
        _glow = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = glow,
            VertexColorUseAsAlbedo = false,
        };
        AddChild(new MeshInstance3D
        {
            Mesh = CellGrid(Definition.SizeCells, CellSize * VisualScale),
            Position = new Vector3(-size.X * 0.5f, 0.0f, -size.Z * 0.5f),
            MaterialOverride = _glow,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        // A downward arrow (a cone, point down) that bobs above the item when the carrier is near.
        _arrow = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.28f, BottomRadius = 0.0f, Height = 0.55f, RadialSegments = 4 },
            Position = Vector3.Up * (size.Y + IndicatorHeight),
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = glow },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_arrow);

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
        UpdateHighlight((float)delta);
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
        // In co-op the host says who gets a shared supply (the label waits while it's asked).
        if (!GameEvents.MayPickUp(SlotKey))
        {
            if (_label != null)
            {
                _label.Text = ClaimingText;
                _label.Visible = true;
                _noRoomTimer = NoRoomMessageTime;
            }
            return false;
        }
        CarrierInventory? inventory = interactor.GetNodeOrNull<CarrierInventory>("Inventory");
        if (inventory == null || !inventory.TryAdd(Definition, Durability, true, Appearance, InstanceId))
        {
            if (_label != null)
            {
                _label.Text = "No room in the pack";
                _label.Visible = true;
                _noRoomTimer = NoRoomMessageTime;
            }
            return false;
        }

        if (SlotKey.Length > 0)
        {
            GameEvents.PublishSupplyPickedUp(SlotKey);
        }
        Taken?.Invoke(this);
        RemoveFromGroup(IInteractable.Group);
        QueueFree();
        return true;
    }

    /// <summary>Pulses the cell lines and shows the bobbing arrow when the carrier is within reach of seeing it.</summary>
    private void UpdateHighlight(float dt)
    {
        _time += dt;
        if (_glow != null && Definition != null)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(_time * 3.0f);
            Color colour = CategoryColor(Definition.Category);
            _glow.AlbedoColor = new Color(colour.R * pulse, colour.G * pulse, colour.B * pulse);
        }
        if (_arrow == null)
        {
            return;
        }
        Vector3 offset = GlobalPosition - GameEvents.CarrierPosition;
        float distance = offset.Length();
        bool show = distance <= IndicatorRadius && distance >= IndicatorHideDistance && !_focused;
        _arrow.Visible = show;
        if (show)
        {
            float top = 0.0f;
            if (Definition != null)
            {
                top = Definition.SizeCells.Y * CellSize * VisualScale;
            }
            _arrow.Position = Vector3.Up * (top + IndicatorHeight + 0.12f * Mathf.Sin(_time * 4.0f));
            _arrow.Rotation = new Vector3(0.0f, _time * 1.5f, 0.0f);
        }
    }

    private Color CategoryColor(ItemCategory category)
    {
        if (category == ItemCategory.Food)
        {
            return FoodColor;
        }
        if (category == ItemCategory.Water)
        {
            return WaterColor;
        }
        if (category == ItemCategory.Cargo)
        {
            return CargoColor;
        }
        return SupplyColor;
    }

    /// <summary>
    /// Lines along every cell edge on the outside of a block of <paramref name="cells"/> (the item's
    /// shape in the backpack grid), from its bottom corner. Built once per item.
    /// </summary>
    public static ArrayMesh CellGrid(Vector3I cells, float cell)
    {
        List<Vector3> lines = new();
        Vector3 size = (Vector3)cells * cell;
        // Lines parallel to X: at every (y, z) cell boundary on the block's outer faces.
        for (int y = 0; y <= cells.Y; y++)
        {
            for (int z = 0; z <= cells.Z; z++)
            {
                if (y == 0 || y == cells.Y || z == 0 || z == cells.Z)
                {
                    lines.Add(new Vector3(0.0f, y * cell, z * cell));
                    lines.Add(new Vector3(size.X, y * cell, z * cell));
                }
            }
        }
        // Parallel to Y.
        for (int x = 0; x <= cells.X; x++)
        {
            for (int z = 0; z <= cells.Z; z++)
            {
                if (x == 0 || x == cells.X || z == 0 || z == cells.Z)
                {
                    lines.Add(new Vector3(x * cell, 0.0f, z * cell));
                    lines.Add(new Vector3(x * cell, size.Y, z * cell));
                }
            }
        }
        // Parallel to Z.
        for (int x = 0; x <= cells.X; x++)
        {
            for (int y = 0; y <= cells.Y; y++)
            {
                if (x == 0 || x == cells.X || y == 0 || y == cells.Y)
                {
                    lines.Add(new Vector3(x * cell, y * cell, 0.0f));
                    lines.Add(new Vector3(x * cell, y * cell, size.Z));
                }
            }
        }
        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = lines.ToArray();
        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        return mesh;
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
