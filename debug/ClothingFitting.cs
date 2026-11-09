using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Hand-fitting worn clothes, in the editor (debug/ClothingFitting.tscn): shows a character at rest
/// wearing <see cref="Outfit"/>, with one handle (a Marker3D under Fits) per fittable part. Move, turn
/// and scale a handle with the gizmo and the part follows; its position, rotation and scale are the
/// fit in the body's frame (X right, Y up, Z back), in metres and degrees about the part's centre.
/// Save (or Ctrl+S) writes every fit into player/clothing_fit.tres, which the game applies to that
/// character. Rebuild after changing the character or the outfit.
/// </summary>
[Tool]
public partial class ClothingFitting : Node3D
{
    private const string KeyMeta = "clothing_fit_key";

    /// <summary>The character to fit (CarrierCharacter.Id: default, modular, plenki, vucic…).</summary>
    [Export] public string CharacterId { get; set; } = "modular";
    /// <summary>Item ids worn together (one per slot). Empty: the base clothes (shirt, trousers, boots, pack).</summary>
    [Export] public string[] Outfit { get; set; } = System.Array.Empty<string>();
    [Export(PropertyHint.Dir)] public string ItemFolder { get; set; } = "res://inventory/items";
    /// <summary>The model's own hard hat and vest show for these items (as in CarrierModel).</summary>
    [Export] public string HelmetItem { get; set; } = "hard_hat";
    [Export] public string VestItem { get; set; } = "hi_vis_vest";
    /// <summary>Size of the handles' crosses, in m.</summary>
    [Export] public float HandleSize { get; set; } = 0.12f;
    /// <summary>A handle closer than this to no change (m, degrees, scale) saves as no fit.</summary>
    [Export] public float IdentityTolerance { get; set; } = 0.0005f;

    [ExportToolButton("Rebuild")] public Callable RebuildButton => Callable.From(Rebuild);
    [ExportToolButton("Save")] public Callable SaveButton => Callable.From(Save);
    [ExportToolButton("Reset all handles")] public Callable ResetButton => Callable.From(ResetAll);

    private Node3D? _preview;
    private readonly List<Marker3D> _handles = new();
    private readonly Dictionary<Marker3D, ClothingFitPart> _parts = new();
    private readonly Dictionary<Marker3D, Transform3D> _applied = new();

    public override void _Ready()
    {
        Callable.From(Rebuild).CallDeferred();
    }

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        if (property["name"].AsStringName() != PropertyName.CharacterId)
        {
            return;
        }
        List<string> ids = new();
        CarrierRoster? roster = CarrierRoster.Load();
        if (roster != null)
        {
            foreach (CarrierCharacter character in roster.Available())
            {
                ids.Add(character.Id);
            }
        }
        property["hint"] = (int)PropertyHint.EnumSuggestion;
        property["hint_string"] = string.Join(",", ids);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationEditorPreSave && _handles.Count > 0)
        {
            Save();
        }
    }

    public override void _Process(double delta)
    {
        foreach (Marker3D handle in _handles)
        {
            if (!IsInstanceValid(handle) || !_parts.TryGetValue(handle, out ClothingFitPart? part))
            {
                continue;
            }
            Transform3D now = handle.Transform;
            if (!now.IsEqualApprox(_applied.GetValueOrDefault(handle, Transform3D.Identity)))
            {
                part.Apply(now);
                _applied[handle] = now;
            }
        }
    }

    /// <summary>Shows the character in the outfit again, with handles holding the saved fits.</summary>
    public void Rebuild()
    {
        Clear();
        CarrierCharacter? character = CarrierRoster.Load()?.Pick(CharacterId);
        if (character == null || character.Id != CharacterId)
        {
            GD.PushWarning($"ClothingFitting: no available character '{CharacterId}'.");
            return;
        }
        if (ResourceLoader.Load(character.ModelPath) is not PackedScene scene || !scene.CanInstantiate())
        {
            GD.PushWarning($"ClothingFitting: can't load {character.ModelPath}.");
            return;
        }
        _preview = new Node3D { Name = "Preview" };
        AddChild(_preview);
        Node instance = scene.Instantiate();
        _preview.AddChild(instance);
        Skeleton3D? skeleton = instance as Skeleton3D;
        foreach (Node node in instance.FindChildren("*", "Skeleton3D", true, false))
        {
            skeleton = node as Skeleton3D;
            break;
        }
        if (skeleton == null)
        {
            GD.PushWarning($"ClothingFitting: {character.ModelPath} has no skeleton.");
            return;
        }
        GearSnapshot gear = Snapshot();
        MeshInstance3D? helmet = Piece(skeleton, character.HelmetMesh);
        MeshInstance3D? hair = Piece(skeleton, character.HairMesh);
        MeshInstance3D? vest = Piece(skeleton, character.VestMesh);
        string helmetItem = "";
        if (helmet != null)
        {
            helmetItem = HelmetItem;
            helmet.Visible = gear.In(EquipSlot.Head).ItemId == HelmetItem;
        }
        if (hair != null)
        {
            hair.Visible = gear.In(EquipSlot.Head).Empty;
        }
        string vestItem = "";
        if (vest != null)
        {
            vestItem = VestItem;
            vest.Visible = gear.In(EquipSlot.Torso).ItemId == VestItem;
        }

        CarrierWornGear worn = new() { Name = "WornGear", ListenToGear = false, Skeleton = skeleton, CharacterId = character.Id, Vest = vest, HelmetItem = helmetItem, VestItem = vestItem };
        _preview.AddChild(worn);
        worn.Setup(false);
        worn.ShowGear(gear);
        ClothingVisualController clothing = new() { Name = "ClothingVisuals", ListenToGear = false, Skeleton = skeleton, CharacterId = character.Id };
        _preview.AddChild(clothing);
        clothing.Setup(false);
        clothing.ShowGear(gear);

        Node3D fits = Fits();
        Node owner = this;
        if (Owner != null)
        {
            owner = Owner;
        }
        ClothingFit saved = ClothingFit.Load();
        List<KeyValuePair<string, Node3D>> pieces = worn.Pieces();
        pieces.AddRange(clothing.Pieces());
        foreach ((string key, Node3D node) in pieces)
        {
            foreach (ClothingFitPart part in ClothingFitter.Parts(skeleton, key, node))
            {
                Node3D pivot = new() { Name = part.Key + "_pivot" };
                fits.AddChild(pivot);
                pivot.Owner = owner;
                pivot.GlobalTransform = skeleton.GlobalTransform * part.Anchor;
                pivot.SetMeta("_edit_lock_", true);
                Marker3D handle = new() { Name = part.Key, GizmoExtents = HandleSize };
                handle.SetMeta(KeyMeta, part.Key);
                pivot.AddChild(handle);
                handle.Owner = owner;
                ClothingFitEntry? entry = saved.Find(character.Id, part.Key);
                if (entry != null)
                {
                    handle.Position = entry.Offset;
                    handle.RotationDegrees = entry.RotationDegrees;
                    handle.Scale = entry.Scale;
                }
                _handles.Add(handle);
                _parts[handle] = part;
                part.Apply(handle.Transform);
                _applied[handle] = handle.Transform;
            }
        }
        GD.Print($"ClothingFitting: {character.Id} wearing [{string.Join(", ", Outfit)}]: {_handles.Count} parts to fit under Fits.");
    }

    /// <summary>Writes every handle's fit for this character into player/clothing_fit.tres.</summary>
    public void Save()
    {
        ClothingFit fit = ClothingFit.Load();
        foreach (Marker3D handle in _handles)
        {
            if (!IsInstanceValid(handle))
            {
                continue;
            }
            string key = handle.GetMeta(KeyMeta, handle.Name.ToString()).AsString();
            fit.Set(CharacterId, key, handle.Position, handle.RotationDegrees, handle.Scale, IdentityTolerance);
            if (fit.Find(CharacterId, key) is ClothingFitEntry entry)
            {
                GD.Print($"  {CharacterId} {key}: offset {entry.Offset} m, rotation {entry.RotationDegrees}°, scale {entry.Scale}");
            }
        }
        Error error = ResourceSaver.Save(fit, ClothingFit.DefaultPath, ResourceSaver.SaverFlags.ChangePath);
        if (error != Error.Ok)
        {
            GD.PushError($"ClothingFitting: saving {ClothingFit.DefaultPath} failed: {error}");
            return;
        }
        GD.Print($"ClothingFitting: saved {fit.Entries.Count} fits to {ClothingFit.DefaultPath}.");
    }

    /// <summary>Puts every handle back to no fit (Save to keep that).</summary>
    public void ResetAll()
    {
        foreach (Marker3D handle in _handles)
        {
            if (IsInstanceValid(handle))
            {
                handle.Transform = Transform3D.Identity;
            }
        }
    }

    private void Clear()
    {
        _handles.Clear();
        _parts.Clear();
        _applied.Clear();
        if (_preview != null && IsInstanceValid(_preview))
        {
            RemoveChild(_preview);
            _preview.QueueFree();
        }
        _preview = null;
        Node3D? fits = GetNodeOrNull<Node3D>("Fits");
        if (fits == null)
        {
            return;
        }
        foreach (Node child in fits.GetChildren())
        {
            fits.RemoveChild(child);
            child.QueueFree();
        }
    }

    private Node3D Fits()
    {
        Node3D? fits = GetNodeOrNull<Node3D>("Fits");
        if (fits != null)
        {
            return fits;
        }
        fits = new Node3D { Name = "Fits" };
        AddChild(fits);
        Node owner = this;
        if (Owner != null)
        {
            owner = Owner;
        }
        fits.Owner = owner;
        return fits;
    }

    /// <summary>The outfit as worn gear, new and clean.</summary>
    private GearSnapshot Snapshot()
    {
        WornPiece[] slots = new WornPiece[EquipmentSlots.Count];
        foreach (string id in Outfit)
        {
            string path = ItemFolder.TrimEnd('/') + "/" + id + ".tres";
            if (!ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not ItemDefinition item)
            {
                GD.PushWarning($"ClothingFitting: no item {path}.");
                continue;
            }
            int index = (int)item.Slot;
            if (index < 0 || index >= slots.Length)
            {
                continue;
            }
            slots[index] = new WornPiece(item.ItemId, item.DisplayName, item.MaxDurability, item.MaxDurability, item);
        }
        return GearSnapshot.None with { Slots = slots };
    }

    private static MeshInstance3D? Piece(Skeleton3D skeleton, string name)
    {
        if (name.Length == 0)
        {
            return null;
        }
        return skeleton.GetNodeOrNull<MeshInstance3D>(name);
    }
}
