using System;
using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>One visual layer per equipped copy; restores native body visibility when a layer is removed.</summary>
public partial class ClothingVisualController : Node
{
    /// <summary>Disable before adding a fitting preview to the tree.</summary>
    public bool ListenToGear { get; set; } = true;
    public Skeleton3D? Skeleton { get; set; }
    public string CharacterId { get; set; } = "";
    [Export] public float BasePackBackOffset { get; set; } = 0.24f;
    [Export] public float BasePackHeight { get; set; } = -0.08f;
    [Export] public string BasePackBone { get; set; } = "Spine2";
    [Export] public bool FitBasePackToBody { get; set; } = true;
    [Export] public float BasePackClearance { get; set; } = 0.015f;
    [Export] public Vector3 BasePackRotationDegrees { get; set; }
    [Export] public Vector3 BasePackScale { get; set; } = Vector3.One;
    /// <summary>Colours of the trousers and boots cut from a base character's body (see BodyGarment).</summary>
    [Export] public Color BodyTrousersColour { get; set; } = new(0.27f, 0.25f, 0.21f);
    [Export] public Color BodyBootsColour { get; set; } = new(0.22f, 0.14f, 0.08f);
    /// <summary>How far each body-cut garment stands off the skin, in the body mesh's units (metres).</summary>
    [Export] public float BodyShirtInflate { get; set; } = 0.006f;
    [Export] public float BodyTrousersInflate { get; set; } = 0.009f;
    [Export] public float BodyBootsInflate { get; set; } = 0.012f;
    public bool HasBaseOutfit => _baseShirt != null && (_basePack != null || _visuals.ContainsKey(EquipSlot.Back));
    public BackpackAttachment? BackpackMount => _basePack as BackpackAttachment ?? _visuals.GetValueOrDefault(EquipSlot.Back) as BackpackAttachment;
    private MeshInstance3D? _bodyTrousers;
    private MeshInstance3D? _bodyBoots;
    public IReadOnlyDictionary<EquipSlot, Node3D> Visuals => _visuals;
    private readonly Dictionary<EquipSlot, Node3D> _visuals = new();
    private readonly Dictionary<EquipSlot, ItemDefinition> _definitions = new();
    private readonly Dictionary<MeshInstance3D, bool> _originalVisibility = new();
    private readonly Dictionary<EquipSlot, ClothingVisual> _looks = new();
    private readonly Dictionary<EquipSlot, List<ShaderMaterial>> _materials = new();
    /// <summary>The body, and the copy of it without the skin worn modelled clothes cover (BodyGarment.Keep).</summary>
    private MeshInstance3D? _body;
    private MeshInstance3D? _keptBody;
    private string _keptBones = "";
    private readonly ClothingVisual _base = new() { Kind = ClothingVisual.VisualKind.PrototypeShirt };
    private MeshInstance3D? _baseShirt;
    private MeshInstance3D? _nativeShirt;
    private Node3D? _basePack;
    private BaseMaterial3D? _nativeShirtMaterial;
    private ShaderMaterial? _packMaterial;
    private Shader? _shader;
    private bool _setUp;

    public override void _Ready()
    {
        Setup(ListenToGear);
    }

    /// <summary>
    /// Builds the base outfit (once); with <paramref name="listen"/> it then follows the worn gear and the
    /// options. The clothing fitting scene calls it without listening and shows gear through ShowGear.
    /// </summary>
    public void Setup(bool listen)
    {
        if (Skeleton == null || _setUp) { return; }
        _setUp = true;
        _shader = GD.Load<Shader>("res://player/clothing_condition.gdshader");
        foreach (Node node in Skeleton.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is MeshInstance3D mesh && mesh.Name == "Ch17_Shirt") { _nativeShirt = mesh; }
        }
        _body = BodyGarment.FindBody(Skeleton);
        CutBodyGarments();
        if (_baseShirt == null)
        {
            _baseShirt = ClothingPrototypes.Garment(Skeleton, _base);
            if (_baseShirt != null) { _baseShirt.Name = "BaseShirtPrototype"; _baseShirt.MaterialOverride = Material(_base.Colour); }
        }
        if (_nativeShirt?.GetActiveMaterial(0) is BaseMaterial3D native)
        {
            _nativeShirtMaterial = (BaseMaterial3D)native.Duplicate();
            _nativeShirt.MaterialOverride = _nativeShirtMaterial;
        }
        _packMaterial = Material(GameOptions.BasePackColour);
        ClothingFitter.Fit(Skeleton, CharacterId, BaseShirtKey(), _baseShirt);
        ClothingFitter.Fit(Skeleton, CharacterId, "body_trousers", _bodyTrousers);
        ClothingFitter.Fit(Skeleton, CharacterId, "body_boots", _bodyBoots);
        EnsureBasePack();
        ApplyBaseAppearance();
        if (!listen) { return; }
        GameOptions.Changed += ApplyBaseAppearance;
        GameEvents.GearChanged += OnGear;
        OnGear(GameEvents.CurrentGear);
    }

    /// <summary>Shows this gear without listening for changes (the clothing fitting scene).</summary>
    public void ShowGear(GearSnapshot gear)
    {
        OnGear(gear);
    }

    /// <summary>The body as drawn now: whole, or without the skin under modelled clothes (null if it has none).</summary>
    public MeshInstance3D? ShownBody
    {
        get
        {
            if (_keptBody != null && IsInstanceValid(_keptBody) && _keptBody.Visible) { return _keptBody; }
            return _body;
        }
    }

    /// <summary>The pieces shown now with their fit keys (ClothingFitter): the base outfit's and each worn item's.</summary>
    public List<KeyValuePair<string, Node3D>> Pieces()
    {
        List<KeyValuePair<string, Node3D>> pieces = new();
        AddPiece(pieces, BaseShirtKey(), _baseShirt);
        AddPiece(pieces, "body_trousers", _bodyTrousers);
        AddPiece(pieces, "body_boots", _bodyBoots);
        AddPiece(pieces, "base_pack", _basePack);
        foreach ((EquipSlot slot, Node3D visual) in _visuals)
        {
            AddPiece(pieces, _definitions[slot].ItemId, visual);
        }
        return pieces;
    }

    private static void AddPiece(List<KeyValuePair<string, Node3D>> pieces, string key, Node3D? node)
    {
        if (node != null && IsInstanceValid(node) && !node.IsQueuedForDeletion() && node.Visible)
        {
            pieces.Add(new KeyValuePair<string, Node3D>(key, node));
        }
    }

    /// <summary>The base shirt's fit key: cut from the body, or the drawn prototype.</summary>
    private string BaseShirtKey()
    {
        if (_baseShirt != null && _baseShirt.Name == "BodyShirt") { return "body_shirt"; }
        return "base_shirt";
    }

    public override void _ExitTree()
    {
        GameEvents.GearChanged -= OnGear;
        GameOptions.Changed -= ApplyBaseAppearance;
        RestoreBody();
        ShowWholeBody();
        foreach (Node3D node in _visuals.Values) { Remove(node); }
        Remove(_baseShirt);
        Remove(_basePack);
        Remove(_bodyTrousers);
        Remove(_bodyBoots);
    }

    /// <summary>
    /// A base character in underwear (Unreal-named rig, no shirt of its own) gets a shirt, trousers and
    /// boots cut from its own body, which fit it exactly; the shirt then takes the base shirt's place.
    /// </summary>
    private void CutBodyGarments()
    {
        if (Skeleton == null || _nativeShirt != null || Skeleton.FindBone("spine_01") < 0) { return; }
        MeshInstance3D? body = BodyGarment.FindBody(Skeleton);
        if (body == null) { return; }
        _baseShirt = BodyGarment.Cut(body, new[] { "spine_01", "spine_02", "spine_03", "clavicle_l", "clavicle_r", "upperarm_l", "upperarm_r", "lowerarm_l", "lowerarm_r" }, BodyShirtInflate, "BodyShirt");
        if (_baseShirt != null) { _baseShirt.MaterialOverride = Material(_base.Colour); }
        _bodyTrousers = BodyGarment.Cut(body, new[] { "pelvis", "thigh_l", "thigh_r", "calf_l", "calf_r" }, BodyTrousersInflate, "BodyTrousers");
        if (_bodyTrousers != null) { _bodyTrousers.MaterialOverride = Material(BodyTrousersColour); }
        _bodyBoots = BodyGarment.Cut(body, new[] { "foot_l", "foot_r", "ball_l", "ball_r" }, BodyBootsInflate, "BodyBoots");
        if (_bodyBoots != null) { _bodyBoots.MaterialOverride = Material(BodyBootsColour); }
    }

    private static void Remove(Node? node)
    {
        if (node != null && IsInstanceValid(node) && !node.IsQueuedForDeletion())
        {
            if (node is Node3D visual) { visual.Visible = false; }
            if (node is BackpackAttachment && node.IsInsideTree()) { node.GetParent()?.RemoveChild(node); }
            node.QueueFree();
        }
    }

    private ShaderMaterial Material(Color colour)
    {
        ShaderMaterial material = new() { Shader = _shader };
        material.SetShaderParameter("cloth_colour", colour);
        return material;
    }

    private void ApplyBaseAppearance()
    {
        if (!IsInsideTree()) { return; }
        if (_baseShirt?.MaterialOverride is ShaderMaterial shirt) { shirt.SetShaderParameter("cloth_colour", GameOptions.BaseShirtColour); }
        if (_nativeShirtMaterial != null) { _nativeShirtMaterial.AlbedoColor = GameOptions.BaseShirtColour; }
        _packMaterial?.SetShaderParameter("cloth_colour", GameOptions.BasePackColour);
    }

    private void OnGear(GearSnapshot gear)
    {
        if (Skeleton == null) { return; }
        WornPiece backPiece = gear.In(EquipSlot.Back);
        ItemDefinition? backDefinition = backPiece.Definition;
        if (backDefinition == null && !backPiece.Empty) { backDefinition = ItemText.Find(backPiece.ItemId); }
        if (backDefinition?.ClothingFor(CharacterId) != null && _basePack != null)
        {
            Remove(_basePack);
            _basePack = null;
        }
        foreach (EquipSlot slot in EquipmentSlots.All)
        {
            WornPiece piece = gear.In(slot);
            ItemDefinition? item = piece.Definition;
            if (item == null && !piece.Empty) { item = ItemText.Find(piece.ItemId); }
            ClothingVisual? look = item?.ClothingFor(CharacterId);
            if (!_definitions.TryGetValue(slot, out ItemDefinition? previous) || previous != item)
            {
                if (_visuals.Remove(slot, out Node3D? old)) { Remove(old); }
                _definitions.Remove(slot);
                _looks.Remove(slot);
                _materials.Remove(slot);
                if (item != null && look != null)
                {
                    _definitions[slot] = item;
                    _looks[slot] = look;
                    Node3D? visual = Build(item, look);
                    if (visual != null)
                    {
                        if (visual is BackpackAttachment pack) { pack.EquippedItemId = item.ItemId; pack.SetMeta("equipped_item", item.ItemId); }
                        _visuals[slot] = visual;
                        _materials[slot] = Dress(visual, look);
                        ClothingFitter.Fit(Skeleton, CharacterId, item.ItemId, visual);
                    }
                }
            }
            if (look != null && _materials.TryGetValue(slot, out List<ShaderMaterial>? cloths))
            {
                Color colour = look.Colour;
                if (look.MaterialColours.Length > 0)
                {
                    colour = look.MaterialColours[Math.Clamp(piece.Appearance.Variant, 0, look.MaterialColours.Length - 1)];
                }
                if (piece.Appearance.Tint.A > 0) { colour = piece.Appearance.Tint; }
                foreach (ShaderMaterial cloth in cloths)
                {
                    cloth.SetShaderParameter("cloth_colour", colour);
                    cloth.SetShaderParameter("wetness", Mathf.Clamp(piece.Appearance.Wetness, 0, 1));
                    cloth.SetShaderParameter("dirt", Mathf.Clamp(piece.Appearance.Dirt, 0, 1));
                    cloth.SetShaderParameter("wear", 1.0f - Mathf.Clamp(piece.Fraction, 0, 1));
                }
            }
        }
        EnsureBasePack();
        // A garment over another hides it (a jerkin over the shirt).
        HashSet<EquipSlot> covered = new();
        foreach ((EquipSlot slot, Node3D _) in _visuals)
        {
            foreach (string name in _looks[slot].HidesSlots)
            {
                if (Enum.TryParse(name, out EquipSlot under) && under != slot) { covered.Add(under); }
            }
        }
        foreach ((EquipSlot slot, Node3D visual) in _visuals)
        {
            visual.Visible = !covered.Contains(slot);
        }
        RestoreBody();
        SortedSet<string> hiddenBones = new(StringComparer.Ordinal);
        foreach ((EquipSlot slot, Node3D visual) in _visuals)
        {
            if (!visual.Visible) { continue; }
            foreach (string bone in _looks[slot].HideBodyBones) { hiddenBones.Add(bone); }
            foreach (string name in _looks[slot].HideBodyMeshes)
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
        if (_bodyTrousers != null) { _bodyTrousers.Visible = !_visuals.ContainsKey(EquipSlot.Legs); }
        if (_bodyBoots != null) { _bodyBoots.Visible = !_visuals.ContainsKey(EquipSlot.Feet); }
        HideCoveredSkin(hiddenBones);
    }

    /// <summary>Draws the body without the skin on these bones (none: the whole body), rebuilt when they change.</summary>
    private void HideCoveredSkin(SortedSet<string> bones)
    {
        string key = string.Join(",", bones);
        if (key == _keptBones) { return; }
        ShowWholeBody();
        _keptBones = key;
        if (bones.Count == 0 || _body == null || !IsInstanceValid(_body)) { return; }
        _keptBody = BodyGarment.Keep(_body, bones, "BodyUnderClothes");
        if (_keptBody != null) { _body.Visible = false; }
    }

    private void ShowWholeBody()
    {
        Remove(_keptBody);
        _keptBody = null;
        _keptBones = "";
        if (_body != null && IsInstanceValid(_body)) { _body.Visible = true; }
    }

    private void EnsureBasePack()
    {
        if (Skeleton == null || _visuals.ContainsKey(EquipSlot.Back) || _basePack != null) { return; }
        _basePack = BackpackAttachment.Create(Skeleton, ClothingPrototypes.Backpack(), BasePackBone,
            new Vector3(0, BasePackHeight, BasePackBackOffset), BasePackRotationDegrees, BasePackScale,
            FitBasePackToBody, BasePackClearance);
        if (_basePack == null) { return; }
        _basePack.Name = "BaseBackpackPrototype";
        foreach (Node node in _basePack.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is MeshInstance3D mesh && mesh.Mesh is BoxMesh box && box.Size.X > 0.2f) { mesh.MaterialOverride = _packMaterial; }
        }
        ClothingFitter.Fit(Skeleton, CharacterId, "base_pack", _basePack);
    }

    private void RestoreBody()
    {
        foreach ((MeshInstance3D mesh, bool visible) in _originalVisibility)
        {
            if (IsInstanceValid(mesh)) { mesh.Visible = visible; }
        }
        _originalVisibility.Clear();
    }

    private Node3D? Build(ItemDefinition item, ClothingVisual visual)
    {
        if (visual.CharacterIds.Length > 0 && Array.IndexOf(visual.CharacterIds, CharacterId) < 0)
        { GD.PushWarning(item.ItemId + ": no fitted variant for " + CharacterId); return null; }
        if (visual.Kind <= ClothingVisual.VisualKind.PrototypeGloves) { return ClothingPrototypes.Garment(Skeleton!, visual); }
        if (visual.Kind == ClothingVisual.VisualKind.PrototypeBackpack)
        { return BackpackAttachment.Create(Skeleton!, ClothingPrototypes.Backpack(), visual.AttachmentBone, visual.Offset, visual.RotationDegrees, visual.Scale, visual.FitBackpackToBody, visual.BackpackClearance); }
        if (visual.Scene == null || !visual.Scene.CanInstantiate()) { GD.PushWarning(item.ItemId + ": missing clothing scene"); return null; }
        Node instance = visual.Scene.Instantiate();
        if (instance is not Node3D source) { instance.Free(); GD.PushWarning(item.ItemId + ": clothing scene must have a 3D root"); return null; }
        if (visual.Kind == ClothingVisual.VisualKind.Rigid)
        {
            if (item.Slot == EquipSlot.Back)
            { return BackpackAttachment.Create(Skeleton!, source, visual.AttachmentBone, visual.Offset, visual.RotationDegrees, visual.Scale, visual.FitBackpackToBody, visual.BackpackClearance); }
            return Attach(source, visual.AttachmentBone, visual.Offset, visual.RotationDegrees, visual.Scale);
        }
        Skeleton3D? sourceRig = source as Skeleton3D;
        foreach (Node node in source.FindChildren("*", "Skeleton3D", true, false)) { sourceRig = node as Skeleton3D; break; }
        Node3D holder = new() { Name = "SkinnedGarment" };
        Skeleton!.AddChild(holder);
        string problem = "No source skeleton or requested meshes";
        foreach (Node node in source.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mesh || Array.IndexOf(visual.MeshNames, mesh.Name.ToString()) < 0) { continue; }
            if (sourceRig == null || mesh.GetParent() != sourceRig
                || !ClothingCompatibility.TryBind(mesh, sourceRig, Skeleton, visual.RestTolerance, out Skin skin, out problem))
            { source.Free(); holder.QueueFree(); GD.PushWarning(item.ItemId + ": " + problem); return null; }
            MeshInstance3D copy = new() { Name = mesh.Name, Mesh = mesh.Mesh, Skin = skin, Transform = mesh.Transform };
            holder.AddChild(copy);
            copy.Skeleton = copy.GetPathTo(Skeleton);
        }
        source.Free();
        if (holder.GetChildCount() == 0) { holder.QueueFree(); GD.PushWarning(item.ItemId + ": " + problem); return null; }
        return holder;
    }

    private Node3D? Attach(Node3D model, string bone, Vector3 offset, Vector3 rotation, Vector3 scale)
    {
        if (!offset.IsFinite() || !rotation.IsFinite() || !scale.IsFinite() || scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0)
        { model.Free(); GD.PushWarning("Clothing attachment has invalid offsets or scale"); return null; }
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

    /// <summary>
    /// The condition materials for a garment: one plain cloth for all of it, or (Textured) one per
    /// surface carrying that surface's own textures, except the surfaces it keeps as modelled.
    /// </summary>
    private List<ShaderMaterial> Dress(Node3D visual, ClothingVisual look)
    {
        List<ShaderMaterial> materials = new();
        if (!look.Textured)
        {
            ShaderMaterial plain = Material(look.Colour);
            ApplyMaterial(visual, plain);
            materials.Add(plain);
            return materials;
        }
        List<MeshInstance3D> meshes = new();
        if (visual is MeshInstance3D own) { meshes.Add(own); }
        foreach (Node node in visual.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is MeshInstance3D mesh) { meshes.Add(mesh); }
        }
        foreach (MeshInstance3D mesh in meshes)
        {
            if (mesh.Mesh == null) { continue; }
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                Material? modelled = mesh.Mesh.SurfaceGetMaterial(surface);
                if (modelled != null && Array.IndexOf(look.KeepMaterials, modelled.ResourceName) >= 0) { continue; }
                ShaderMaterial material = Material(look.Colour);
                if (modelled is BaseMaterial3D standard)
                {
                    if (standard.AlbedoTexture != null)
                    {
                        material.SetShaderParameter("textured", true);
                        material.SetShaderParameter("albedo_texture", standard.AlbedoTexture);
                    }
                    if (standard.NormalEnabled && standard.NormalTexture != null)
                    {
                        material.SetShaderParameter("has_normal", true);
                        material.SetShaderParameter("normal_texture", standard.NormalTexture);
                    }
                }
                mesh.SetSurfaceOverrideMaterial(surface, material);
                materials.Add(material);
            }
        }
        return materials;
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
