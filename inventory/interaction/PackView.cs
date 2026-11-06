using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// Everything visible while packing, in the station's local space: the open pack (grid centred on
/// x and z, bottom at y = 0, open at the top), packed items, loose items on a mat in front, the
/// ghost of the held item, and an in-world readout: a centre-of-mass marker inside the pack and a
/// tag with weight and balance figures. Also answers which item a ray points at.
/// </summary>
public partial class PackView : Node3D
{
    [Export] public float MatGap { get; set; } = 0.15f;
    [Export] public float MatWidth { get; set; } = 1.0f;
    [Export] public float MatDepth { get; set; } = 0.7f;
    /// <summary>Gap between the pack's left side and the centre of the readout tag, in m.</summary>
    [Export] public float TagGap { get; set; } = 0.6f;
    [Export] public float ItemSpacing { get; set; } = 0.04f;
    /// <summary>Shrinks each drawn item slightly so neighbours read as separate boxes, in m.</summary>
    [Export] public float ItemInset { get; set; } = 0.004f;
    [Export] public float WallThickness { get; set; } = 0.008f;
    [Export] public Color WallColor { get; set; } = new(0.25f, 0.3f, 0.35f, 0.22f);
    [Export] public Color MatColor { get; set; } = new(0.35f, 0.28f, 0.2f);
    [Export] public Color ValidColor { get; set; } = new(0.3f, 1.0f, 0.4f, 0.45f);
    [Export] public Color InvalidColor { get; set; } = new(1.0f, 0.25f, 0.2f, 0.45f);
    [Export] public Color HoverEmission { get; set; } = new(0.35f, 0.35f, 0.35f);
    [Export] public Color MarkerColor { get; set; } = new(1.0f, 0.2f, 0.9f);
    [Export] public float MarkerRadius { get; set; } = 0.018f;
    [Export] public int TagFontSize { get; set; } = 40;
    [Export] public float TagPixelSize { get; set; } = 0.0015f;

    private BackpackDefinition? _pack;
    private Node3D? _contents;
    private MeshInstance3D? _ghost;
    private MeshInstance3D? _marker;
    private Label3D? _tag;
    private Label3D? _hoverLabel;
    private StandardMaterial3D? _ghostMaterial;

    private readonly List<(PackedItem Item, Aabb Box)> _packedBoxes = new();
    private readonly List<(ItemDefinition Item, Aabb Box)> _matBoxes = new();
    private readonly Dictionary<ItemDefinition, StandardMaterial3D> _materials = new();
    private readonly Dictionary<object, MeshInstance3D> _meshes = new();
    private object? _hovered;

    public override void _ExitTree()
    {
        foreach (StandardMaterial3D material in _materials.Values)
        {
            material.Dispose();
        }
        _materials.Clear();
        _ghostMaterial?.Dispose();
        _ghostMaterial = null;
        _packedBoxes.Clear();
        _matBoxes.Clear();
        _meshes.Clear();
    }

    /// <summary>Builds the empty pack, mat, ghost and readout. Call once.</summary>
    public void Build(BackpackDefinition pack)
    {
        _pack = pack;
        Vector3 size = pack.SizeMeters;
        StandardMaterial3D walls = new()
        {
            AlbedoColor = WallColor,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        AddBox(new Vector3(size.X, WallThickness, size.Z), new Vector3(0.0f, -WallThickness * 0.5f, 0.0f), walls);
        AddBox(new Vector3(WallThickness, size.Y, size.Z), new Vector3(-size.X * 0.5f, size.Y * 0.5f, 0.0f), walls);
        AddBox(new Vector3(WallThickness, size.Y, size.Z), new Vector3(size.X * 0.5f, size.Y * 0.5f, 0.0f), walls);
        AddBox(new Vector3(size.X, size.Y, WallThickness), new Vector3(0.0f, size.Y * 0.5f, -size.Z * 0.5f), walls);
        AddBox(new Vector3(size.X, size.Y, WallThickness), new Vector3(0.0f, size.Y * 0.5f, size.Z * 0.5f), walls);
        AddBox(new Vector3(MatWidth, WallThickness, MatDepth),
            new Vector3(0.0f, -WallThickness * 0.5f, size.Z * 0.5f + MatGap + MatDepth * 0.5f),
            new StandardMaterial3D { AlbedoColor = MatColor });

        _contents = new Node3D { Name = "Contents" };
        AddChild(_contents);

        _ghostMaterial = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = ValidColor };
        _ghost = new MeshInstance3D { Name = "Ghost", Visible = false, MaterialOverride = _ghostMaterial };
        AddChild(_ghost);

        _marker = new MeshInstance3D
        {
            Name = "CentreOfMass",
            Mesh = new SphereMesh { Radius = MarkerRadius, Height = MarkerRadius * 2.0f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = MarkerColor,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                NoDepthTest = true,
            },
        };
        AddChild(_marker);

        _tag = new Label3D
        {
            Name = "Tag",
            FontSize = TagFontSize,
            PixelSize = TagPixelSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(-size.X * 0.5f - TagGap, size.Y * 0.85f, 0.0f),
            HorizontalAlignment = HorizontalAlignment.Left,
            OutlineSize = 8,
        };
        AddChild(_tag);

        _hoverLabel = new Label3D
        {
            Name = "HoverLabel",
            FontSize = TagFontSize,
            PixelSize = TagPixelSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            OutlineSize = 8,
            Visible = false,
        };
        AddChild(_hoverLabel);
    }

    /// <summary>Redraws packed and loose items and the readout. <paramref name="held"/> is left off the mat.</summary>
    public void Refresh(CarrierInventory inventory, ItemDefinition? held)
    {
        if (_pack == null || _contents == null || inventory.Grid == null)
        {
            return;
        }

        foreach (Node child in _contents.GetChildren())
        {
            child.QueueFree();
        }
        _packedBoxes.Clear();
        _matBoxes.Clear();
        _meshes.Clear();
        _hovered = null;

        foreach (PackedItem item in inventory.Grid.Items)
        {
            Aabb box = ItemBox(item.Origin, item.Size);
            _packedBoxes.Add((item, box));
            _meshes[item] = AddItemMesh(item.Definition, box);
        }

        Vector3 packSize = _pack.SizeMeters;
        float rowStart = -MatWidth * 0.5f;
        float x = rowStart;
        float z = packSize.Z * 0.5f + MatGap;
        float rowDepth = 0.0f;
        foreach (ItemDefinition item in inventory.LooseItems)
        {
            if (item == held)
            {
                continue;
            }
            Vector3 size = (Vector3)item.SizeCells * _pack.CellSize;
            if (x + size.X > MatWidth * 0.5f && x > rowStart)
            {
                x = rowStart;
                z += rowDepth + ItemSpacing;
                rowDepth = 0.0f;
            }
            Aabb box = new(new Vector3(x, 0.0f, z), size);
            _matBoxes.Add((item, box));
            _meshes[item] = AddItemMesh(item, box);
            x += size.X + ItemSpacing;
            rowDepth = Mathf.Max(rowDepth, size.Z);
        }

        UpdateReadout(inventory);
    }

    public void ShowGhost(Vector3I origin, Vector3I size, bool valid)
    {
        if (_ghost == null || _ghostMaterial == null)
        {
            return;
        }
        Aabb box = ItemBox(origin, size);
        _ghost.Mesh = new BoxMesh { Size = box.Size };
        _ghost.Position = box.GetCenter();
        if (valid)
        {
            _ghostMaterial.AlbedoColor = ValidColor;
        }
        else
        {
            _ghostMaterial.AlbedoColor = InvalidColor;
        }
        _ghost.Visible = true;
    }

    public void HideGhost()
    {
        if (_ghost != null)
        {
            _ghost.Visible = false;
        }
    }

    /// <summary>Where a ray (station space) crosses the plane of the pack's open top.</summary>
    public bool TopPlaneHit(Vector3 from, Vector3 direction, out Vector3 point)
    {
        point = Vector3.Zero;
        if (_pack == null || Mathf.IsZeroApprox(direction.Y))
        {
            return false;
        }
        float t = (_pack.SizeMeters.Y - from.Y) / direction.Y;
        if (t < 0.0f)
        {
            return false;
        }
        point = from + direction * t;
        return true;
    }

    /// <summary>Converts a top-plane point to the grid column where a box of <paramref name="size"/> would be centred.</summary>
    public Vector2I ColumnAt(Vector3 point, Vector3I size)
    {
        if (_pack == null)
        {
            return Vector2I.Zero;
        }
        Vector3 grid = point + new Vector3(_pack.SizeMeters.X * 0.5f, 0.0f, _pack.SizeMeters.Z * 0.5f);
        int x = Mathf.RoundToInt(grid.X / _pack.CellSize - size.X * 0.5f);
        int z = Mathf.RoundToInt(grid.Z / _pack.CellSize - size.Z * 0.5f);
        return new Vector2I(
            Mathf.Clamp(x, 0, _pack.GridSize.X - size.X),
            Mathf.Clamp(z, 0, _pack.GridSize.Z - size.Z));
    }

    /// <summary>The nearest packed or loose item a ray (station space) points at: a PackedItem, an ItemDefinition, or null.</summary>
    public object? ItemUnder(Vector3 from, Vector3 direction)
    {
        object? nearest = null;
        float best = float.MaxValue;
        foreach ((PackedItem item, Aabb box) in _packedBoxes)
        {
            if (RayHits(box, from, direction, out float distance) && distance < best)
            {
                best = distance;
                nearest = item;
            }
        }
        foreach ((ItemDefinition item, Aabb box) in _matBoxes)
        {
            if (RayHits(box, from, direction, out float distance) && distance < best)
            {
                best = distance;
                nearest = item;
            }
        }
        return nearest;
    }

    /// <summary>Highlights an item returned by ItemUnder and labels it with its name and mass.</summary>
    public void SetHover(object? target)
    {
        if (target == _hovered)
        {
            return;
        }
        SetEmission(_hovered, false);
        _hovered = target;
        SetEmission(_hovered, true);

        if (_hoverLabel == null)
        {
            return;
        }
        ItemDefinition? item = null;
        if (target is PackedItem packed)
        {
            item = packed.Definition;
        }
        else if (target is ItemDefinition loose)
        {
            item = loose;
        }
        if (item != null && _meshes.TryGetValue(target!, out MeshInstance3D? mesh))
        {
            _hoverLabel.Text = $"{item.DisplayName}  {item.MassKg:0.0} kg";
            _hoverLabel.Position = mesh.Position + Vector3.Up * 0.12f;
            _hoverLabel.Visible = true;
        }
        else
        {
            _hoverLabel.Visible = false;
        }
    }

    private void SetEmission(object? target, bool on)
    {
        if (target == null || !_meshes.TryGetValue(target, out MeshInstance3D? mesh) || mesh.MaterialOverride is not StandardMaterial3D material)
        {
            return;
        }
        material.EmissionEnabled = on;
        material.Emission = HoverEmission;
    }

    private void UpdateReadout(CarrierInventory inventory)
    {
        if (_pack == null || inventory.Grid == null || _marker == null || _tag == null)
        {
            return;
        }

        Vector3 centre = LoadCalculator.CenterOfMass(_pack, inventory.Grid, out float mass);
        _marker.Position = GridToLocal(centre);
        Vector3 worn = LoadCalculator.GridToCarrier(_pack, centre);

        string left = "";
        if (inventory.LooseItems.Count > 0)
        {
            left = $"\n{inventory.LooseItems.Count} left out";
        }
        _tag.Text = $"{_pack.DisplayName}\n{mass:0.0} kg\nCOM height {worn.Y:0.00} m\nCOM side {worn.X * 100.0f:+0;-0} cm{left}";
    }

    /// <summary>Box in station space for an item at a grid origin with a size in cells.</summary>
    private Aabb ItemBox(Vector3I origin, Vector3I size)
    {
        float cell = _pack!.CellSize;
        Vector3 inset = Vector3.One * ItemInset;
        return new Aabb(GridToLocal((Vector3)origin * cell) + inset, (Vector3)size * cell - inset * 2.0f);
    }

    private Vector3 GridToLocal(Vector3 gridMeters)
    {
        Vector3 size = _pack!.SizeMeters;
        return gridMeters - new Vector3(size.X * 0.5f, 0.0f, size.Z * 0.5f);
    }

    private MeshInstance3D AddItemMesh(ItemDefinition item, Aabb box)
    {
        if (!_materials.TryGetValue(item, out StandardMaterial3D? material))
        {
            material = new StandardMaterial3D { AlbedoColor = item.DebugColor, Roughness = 0.8f };
            _materials[item] = material;
        }
        MeshInstance3D mesh = new()
        {
            Mesh = new BoxMesh { Size = box.Size },
            Position = box.GetCenter(),
            MaterialOverride = material,
        };
        _contents!.AddChild(mesh);
        return mesh;
    }

    private void AddBox(Vector3 size, Vector3 position, Material material)
    {
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = position, MaterialOverride = material });
    }

    /// <summary>Slab test: whether a ray hits a box, and how far along the ray.</summary>
    private static bool RayHits(Aabb box, Vector3 from, Vector3 direction, out float distance)
    {
        float near = float.MinValue;
        float far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float origin = from[axis];
            float dir = direction[axis];
            float min = box.Position[axis];
            float max = box.End[axis];
            if (Mathf.IsZeroApprox(dir))
            {
                if (origin < min || origin > max)
                {
                    distance = 0.0f;
                    return false;
                }
                continue;
            }
            float t1 = (min - origin) / dir;
            float t2 = (max - origin) / dir;
            near = Mathf.Max(near, Mathf.Min(t1, t2));
            far = Mathf.Min(far, Mathf.Max(t1, t2));
        }
        distance = near;
        return far >= near && far >= 0.0f;
    }
}
