using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>One visual layer per equipped copy; restores native body visibility when a layer is removed.</summary>
public partial class ClothingVisualController : Node
{
    public Skeleton3D? Skeleton { get; set; }
    public string CharacterId { get; set; } = "";
    [Export] public float BasePackBackOffset { get; set; } = 0.24f;
    [Export] public float BasePackHeight { get; set; } = 0.05f;
    public bool HasBaseOutfit => _baseShirt != null && _basePack != null;
    public IReadOnlyDictionary<EquipSlot, Node3D> Visuals => _visuals;
    private readonly Dictionary<EquipSlot, Node3D> _visuals = new();
    private readonly Dictionary<EquipSlot, ItemDefinition> _definitions = new();
    private readonly Dictionary<MeshInstance3D, bool> _originalVisibility = new();
    private readonly Dictionary<EquipSlot, ShaderMaterial> _materials = new();
    private readonly ClothingVisual _base = new() { Kind = ClothingVisual.VisualKind.PrototypeShirt };
    private MeshInstance3D? _baseShirt;
    private MeshInstance3D? _nativeShirt;
    private Node3D? _basePack;
    private Shader? _shader;

    public override void _Ready()
    {
        if (Skeleton == null) { return; }
        _shader = GD.Load<Shader>("res://player/clothing_condition.gdshader");
        foreach (Node node in Skeleton.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is MeshInstance3D mesh && mesh.Name == "Ch17_Shirt") { _nativeShirt = mesh; }
        }
        _baseShirt = ClothingPrototypes.Garment(Skeleton, _base);
        if (_baseShirt != null) { _baseShirt.Name = "BaseShirtPrototype"; _baseShirt.MaterialOverride = Material(_base.Colour); }
        _basePack = Attach(ClothingPrototypes.Backpack(), "Spine1", new Vector3(0, BasePackHeight, BasePackBackOffset), Vector3.Zero, Vector3.One);
        if (_basePack != null) { _basePack.Name = "BaseBackpackPrototype"; }
        GameEvents.GearChanged += OnGear;
        OnGear(GameEvents.CurrentGear);
    }

    public override void _ExitTree()
    {
        GameEvents.GearChanged -= OnGear;
        RestoreBody();
        foreach (Node3D node in _visuals.Values) { Remove(node); }
        Remove(_baseShirt);
        Remove(_basePack);
    }

    private static void Remove(Node? node)
    {
        if (node != null && IsInstanceValid(node) && !node.IsQueuedForDeletion()) { node.QueueFree(); }
    }

    private ShaderMaterial Material(Color colour)
    {
        ShaderMaterial material = new() { Shader = _shader };
        material.SetShaderParameter("cloth_colour", colour);
        return material;
    }

    private void OnGear(GearSnapshot gear)
    {
        if (Skeleton == null) { return; }
        foreach (EquipSlot slot in EquipmentSlots.All)
        {
            WornPiece piece = gear.In(slot);
            ItemDefinition? item = piece.Definition;
            if (item == null && !piece.Empty) { item = ItemText.Find(piece.ItemId); }
            if (!_definitions.TryGetValue(slot, out ItemDefinition? previous) || previous != item)
            {
                if (_visuals.Remove(slot, out Node3D? old)) { Remove(old); }
                _definitions.Remove(slot);
                _materials.Remove(slot);
                if (item?.Clothing != null)
                {
                    _definitions[slot] = item;
                    Node3D? visual = Build(item);
                    if (visual != null)
                    {
                        _visuals[slot] = visual;
                        ShaderMaterial material = Material(item.Clothing.Colour);
                        _materials[slot] = material;
                        ApplyMaterial(visual, material);
                    }
                }
            }
            if (item?.Clothing != null && _materials.TryGetValue(slot, out ShaderMaterial? cloth))
            {
                Color colour = item.Clothing.Colour;
                if (item.Clothing.MaterialColours.Length > 0)
                {
                    colour = item.Clothing.MaterialColours[Math.Clamp(piece.Appearance.Variant, 0, item.Clothing.MaterialColours.Length - 1)];
                }
                if (piece.Appearance.Tint.A > 0) { colour = piece.Appearance.Tint; }
                cloth.SetShaderParameter("cloth_colour", colour);
                cloth.SetShaderParameter("wetness", Mathf.Clamp(piece.Appearance.Wetness, 0, 1));
                cloth.SetShaderParameter("dirt", Mathf.Clamp(piece.Appearance.Dirt, 0, 1));
                cloth.SetShaderParameter("wear", 1.0f - Mathf.Clamp(piece.Fraction, 0, 1));
            }
        }
        RestoreBody();
        foreach ((EquipSlot slot, Node3D _) in _visuals)
        {
            foreach (string name in _definitions[slot].Clothing!.HideBodyMeshes)
            {
                foreach (Node node in Skeleton.FindChildren("*", "MeshInstance3D", true, false))
                {
                    if (node is MeshInstance3D mesh && mesh.Name == name)
                    {
                        _originalVisibility.TryAdd(mesh, mesh.Visible);
                        mesh.Visible = false;
                    }
                }
            }
        }
        if (_baseShirt != null) { _baseShirt.Visible = _nativeShirt == null && !_visuals.ContainsKey(EquipSlot.InnerTorso) && !_visuals.ContainsKey(EquipSlot.OuterTorso); }
        if (_basePack != null) { _basePack.Visible = !_visuals.ContainsKey(EquipSlot.Back); }
    }

    private void RestoreBody()
    {
        foreach ((MeshInstance3D mesh, bool visible) in _originalVisibility)
        {
            if (IsInstanceValid(mesh)) { mesh.Visible = visible; }
        }
        _originalVisibility.Clear();
    }

    private Node3D? Build(ItemDefinition item)
    {
        ClothingVisual visual = item.Clothing!;
        if (visual.CharacterIds.Length > 0 && Array.IndexOf(visual.CharacterIds, CharacterId) < 0)
        { GD.PushWarning(item.ItemId + ": no fitted variant for " + CharacterId); return null; }
        if (visual.Kind <= ClothingVisual.VisualKind.PrototypeGloves) { return ClothingPrototypes.Garment(Skeleton!, visual); }
        if (visual.Kind == ClothingVisual.VisualKind.PrototypeBackpack)
        { return Attach(ClothingPrototypes.Backpack(), visual.AttachmentBone, visual.Offset, visual.RotationDegrees, visual.Scale); }
        if (visual.Scene == null) { GD.PushWarning(item.ItemId + ": missing clothing scene"); return null; }
        Node3D source = visual.Scene.Instantiate<Node3D>();
        if (visual.Kind == ClothingVisual.VisualKind.Rigid)
        { return Attach(source, visual.AttachmentBone, visual.Offset, visual.RotationDegrees, visual.Scale); }
        Skeleton3D? sourceRig = source as Skeleton3D;
        foreach (Node node in source.FindChildren("*", "Skeleton3D", true, false)) { sourceRig = node as Skeleton3D; break; }
        Node3D holder = new() { Name = "SkinnedGarment" };
        Skeleton!.AddChild(holder);
        string problem = "No source skeleton or requested meshes";
        foreach (Node node in source.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mesh || Array.IndexOf(visual.MeshNames, mesh.Name.ToString()) < 0) { continue; }
            if (sourceRig == null || mesh.GetParent() != sourceRig
                || !ClothingCompatibility.TryBind(mesh, sourceRig, Skeleton, out Skin skin, out problem))
            { source.Free(); holder.QueueFree(); GD.PushWarning(item.ItemId + ": " + problem); return null; }
            MeshInstance3D copy = new() { Mesh = mesh.Mesh, Skin = skin, Transform = mesh.Transform };
            holder.AddChild(copy);
            copy.Skeleton = copy.GetPathTo(Skeleton);
        }
        source.Free();
        if (holder.GetChildCount() == 0) { holder.QueueFree(); GD.PushWarning(item.ItemId + ": " + problem); return null; }
        return holder;
    }

    private Node3D? Attach(Node3D model, string bone, Vector3 offset, Vector3 rotation, Vector3 scale)
    {
        if (Skeleton == null || !ClothingRig.Frame(Skeleton, out Basis frame, out float metres)) { model.Free(); return null; }
        int index = ClothingRig.Bone(Skeleton, bone);
        if (index < 0) { model.Free(); return null; }
        BoneAttachment3D attachment = new() { BoneName = Skeleton.GetBoneName(index), Name = "ClothingAttachment" };
        Skeleton.AddChild(attachment);
        attachment.AddChild(model);
        Transform3D rest = Skeleton.GetBoneGlobalRest(index);
        Basis rotationBasis = Basis.FromEuler(rotation * Mathf.Pi / 180.0f);
        model.Transform = rest.AffineInverse() * new Transform3D((frame * rotationBasis).Scaled(scale / metres), rest.Origin + frame * offset / metres);
        return attachment;
    }

    private static void ApplyMaterial(Node3D root, ShaderMaterial material)
    {
        if (root is MeshInstance3D own) { own.MaterialOverride = material; }
        foreach (Node node in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is MeshInstance3D mesh) { mesh.MaterialOverride = material; }
        }
    }
}
