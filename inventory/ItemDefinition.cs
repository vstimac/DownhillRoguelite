using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// A kind of item that can go in the backpack. Shapes are boxes measured in grid cells for now
/// (see docs/inventory-design.md); irregular shapes come later.
/// </summary>
[GlobalClass]
public partial class ItemDefinition : Resource
{
    [Export] public string ItemId { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public float MassKg { get; set; } = 1.0f;

    /// <summary>Size in grid cells in the item's default orientation (x across, y up, z deep).</summary>
    [Export] public Vector3I SizeCells { get; set; } = Vector3I.One;

    /// <summary>Centre of mass relative to the box centre, in m, in the default orientation.</summary>
    [Export] public Vector3 CenterOfMassOffset { get; set; } = Vector3.Zero;

    [Export] public ItemCategory Category { get; set; } = ItemCategory.Supply;
    /// <summary>Hunger restored when eaten (Food), out of 100.</summary>
    [Export] public float Nutrition { get; set; }
    /// <summary>Thirst restored when drunk (Water), out of 100.</summary>
    [Export] public float Hydration { get; set; }

    [Export] public ItemTags Tags { get; set; } = ItemTags.None;
    [Export] public OrientationRule Orientation { get; set; } = OrientationRule.Any;

    /// <summary>Colour of the placeholder box until real models exist.</summary>
    [Export] public Color DebugColor { get; set; } = new(0.6f, 0.6f, 0.6f);
    [ExportGroup("Gear (M14)")]
    /// <summary>Where it is worn; None for things that aren't.</summary>
    [Export] public EquipSlot Slot { get; set; } = EquipSlot.None;
    /// <summary>Hit points when new (worn gear only).</summary>
    [Export] public float MaxDurability { get; set; }
    /// <summary>Fraction of cold and chill it keeps off (summed over everything worn, capped).</summary>
    [Export] public float Warmth { get; set; }
    /// <summary>Fraction of the rain's chill it keeps off.</summary>
    [Export] public float RainProof { get; set; }
    /// <summary>Extra grip on every surface, as a fraction (boots).</summary>
    [Export] public float GripBonus { get; set; }
    /// <summary>Extra area the wind catches, in m² (a cape).</summary>
    [Export] public float WindCatch { get; set; }
    /// <summary>Pockets: how many small items it holds.</summary>
    [Export] public int Pockets { get; set; }
    /// <summary>What it's made of, for mending ("cloth" or "leather").</summary>
    [Export] public string MendType { get; set; } = "";
    /// <summary>Materials: what they can mend ("cloth", "leather", "backpack").</summary>
    [Export] public string[] MendsTypes { get; set; } = System.Array.Empty<string>();
    /// <summary>Materials: hit points a mend restores.</summary>
    [Export] public float MendAmount { get; set; }

    /// <summary>The item's model (an art-pack scene), fitted into its cell block; none shows a coloured block.</summary>
    [Export] public PackedScene? Model { get; set; }
    /// <summary>Or a piece of another model: the scene (e.g. the carrier) and the mesh's name in it.</summary>
    [Export(PropertyHint.File, "*.fbx,*.glb,*.tscn")] public string ModelSource { get; set; } = "";
    [Export] public string ModelMesh { get; set; } = "";
    /// <summary>The piece lies on its back on the ground (clothes), instead of standing as modelled.</summary>
    [Export] public bool ModelLieFlat { get; set; }

    /// <summary>
    /// Contract cargo only: the contract this physical copy belongs to (empty for ordinary items).
    /// Ordinary items of one kind share one cached resource; cargo is its own copy
    /// (CarrierInventory.GrantCargo), and that copy is the item's identity through drops and repacks.
    /// Not saved: it is set on the runtime copy.
    /// </summary>
    public string CargoInstanceId { get; set; } = "";

    public bool IsContractCargo => CargoInstanceId.Length > 0;

    public int Volume => SizeCells.X * SizeCells.Y * SizeCells.Z;
}
