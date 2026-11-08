using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Worn clothes on the character, for the items that aren't pieces of his own model (CarrierModel
/// handles the hard hat and hi-vis vest): a wool hat on his head, gloves on both hands, an oilskin
/// coat (his vest's shape in oilskin, grown a little, with sleeves on his arms and a skirt at the
/// hips) and a fur cape on his back. Each piece rides a BoneAttachment3D, placed once against the
/// skeleton's rest pose in the body's own frame (up from hips to head, left from hip to hip), so it
/// sits right whatever the model's import axes and scale.
/// </summary>
public partial class CarrierWornGear : Node
{
    public Skeleton3D? Skeleton { get; set; }
    /// <summary>His vest mesh: the oilskin coat is a grown copy of it.</summary>
    public MeshInstance3D? Vest { get; set; }
    /// <summary>The items shown by CarrierModel from his own pieces, skipped here.</summary>
    public string HelmetItem { get; set; } = "hard_hat";
    public string VestItem { get; set; } = "hi_vis_vest";
    /// <summary>Drawn here as a yellow hard hat and a hi-vis vest when the model has no pieces for them.</summary>
    [Export] public string HardHatId { get; set; } = "hard_hat";
    [Export] public string HiVisId { get; set; } = "hi_vis_vest";
    /// <summary>The body under a drawn coat or vest when the model has no vest mesh to copy (m).</summary>
    [Export] public float TorsoRadius { get; set; } = 0.17f;
    [Export] public float TorsoDepth { get; set; } = 0.72f;
    /// <summary>The drawn torso's half width as a share of the shoulder joints' half spacing.</summary>
    [Export] public float ShoulderShare { get; set; } = 0.95f;
    /// <summary>Slack over the body mesh's half depth, so a drawn vest sits on the clothes (m).</summary>
    [Export] public float TorsoSlack { get; set; } = 0.02f;
    [Export] public float TorsoMaxRadius { get; set; } = 0.3f;

    [Export] public float HatRadius { get; set; } = 0.105f;
    [Export] public float GloveRadius { get; set; } = 0.045f;
    [Export] public float SleeveRadius { get; set; } = 0.07f;
    [Export] public float CoatGrow { get; set; } = 0.012f;
    [Export] public float CapeWidth { get; set; } = 0.55f;
    [Export] public float CapeLength { get; set; } = 0.95f;

    /// <summary>Item id → the nodes showing it (empty when nothing of that item is worn).</summary>
    private readonly Dictionary<EquipSlot, List<Node3D>> _shown = new();
    private readonly Dictionary<EquipSlot, string> _shownItem = new();
    private Basis _frame = Basis.Identity;
    private float _metresPerUnit = 1.0f;
    private float _torsoX;
    private float _torsoZ;

    public override void _Ready()
    {
        if (Skeleton == null)
        {
            return;
        }
        _metresPerUnit = Mathf.Max(Skeleton.GlobalBasis.Scale.X, 0.0001f);
        Vector3 hips = RestOrigin("Hips");
        Vector3 head = RestOrigin("Head");
        Vector3 left = RestOrigin("LeftUpLeg") - RestOrigin("RightUpLeg");
        Vector3 up = (head - hips).Normalized();
        if (left.LengthSquared() < 0.0001f || up.LengthSquared() < 0.0001f)
        {
            GD.PushWarning("CarrierWornGear: the skeleton has no Mixamo hips, head and legs; worn clothes won't show.");
            Skeleton = null;
            return;
        }
        // The body's frame in skeleton space, Godot style: X right, Y up, -Z forward.
        Vector3 right = -left.Normalized();
        Vector3 back = right.Cross(up).Normalized();
        right = up.Cross(back).Normalized();
        _frame = new Basis(right, up, back);
        MeasureTorso();
        GameEvents.GearChanged += OnGear;
        OnGear(GameEvents.CurrentGear);
    }

    public override void _ExitTree()
    {
        GameEvents.GearChanged -= OnGear;
    }

    private void OnGear(GearSnapshot gear)
    {
        foreach (EquipSlot slot in new[] { EquipSlot.Head, EquipSlot.Torso, EquipSlot.Hands, EquipSlot.Cape })
        {
            string id = gear.In(slot).ItemId ?? "";
            if (gear.In(slot).Definition?.Clothing != null) { id = ""; }
            if (id == HelmetItem || id == VestItem)
            {
                id = "";
            }
            if (_shownItem.GetValueOrDefault(slot, "") == id)
            {
                continue;
            }
            Clear(slot);
            _shownItem[slot] = id;
            if (id.Length > 0)
            {
                Show(slot, id);
            }
        }
    }

    private void Clear(EquipSlot slot)
    {
        if (_shown.TryGetValue(slot, out List<Node3D>? nodes))
        {
            foreach (Node3D node in nodes)
            {
                node.QueueFree();
            }
            nodes.Clear();
        }
    }

    private void Show(EquipSlot slot, string id)
    {
        List<Node3D> nodes = new();
        _shown[slot] = nodes;
        switch (slot)
        {
            case EquipSlot.Head:
                ShowHat(nodes, id == HardHatId);
                break;
            case EquipSlot.Hands:
                ShowGlove(nodes, "Left", 1.0f);
                ShowGlove(nodes, "Right", -1.0f);
                break;
            case EquipSlot.Torso:
                if (id == HiVisId)
                {
                    ShowVest(nodes);
                }
                else
                {
                    ShowCoat(nodes);
                }
                break;
            case EquipSlot.Cape:
                ShowCape(nodes);
                break;
        }
    }

    private void ShowHat(List<Node3D> nodes, bool hardHat)
    {
        Node3D? piece = Attach("Head", nodes);
        if (piece == null)
        {
            return;
        }
        Vector3 top = Local("Head", RestOrigin("HeadTop_End"));
        if (top.LengthSquared() < 0.0001f)
        {
            top = Vector3.Up * 0.18f;
        }
        if (hardHat)
        {
            ItemModels.AddHardHat(piece, top * 0.55f, HatRadius);
        }
        else
        {
            ItemModels.AddHat(piece, top * 0.52f, HatRadius);
        }
    }

    private void ShowGlove(List<Node3D> nodes, string side, float sign)
    {
        Node3D? piece = Attach(side + "Hand", nodes);
        if (piece == null)
        {
            return;
        }
        Vector3 fingers = Local(side + "Hand", RestOrigin(side + "HandMiddle1"));
        if (fingers.LengthSquared() < 0.0001f)
        {
            fingers = Vector3.Down * 0.09f;
        }
        MeshInstance3D glove = Limb(piece, Vector3.Zero, fingers * 1.6f, GloveRadius, ItemModels.Leather);
        glove.Scale = new Vector3(1.0f, 1.0f, 0.7f);
        ItemModels.Part(piece, new CylinderMesh { TopRadius = GloveRadius * 1.15f, BottomRadius = GloveRadius * 1.15f, Height = 0.05f, RadialSegments = 12, CapTop = false, CapBottom = false },
            ItemModels.Leather, -fingers.Normalized() * 0.02f).Basis = Along(fingers);
    }

    private void ShowCoat(List<Node3D> nodes)
    {
        if (Vest != null && Vest.GetParent() is Node3D parent)
        {
            MeshInstance3D body = new()
            {
                Name = "OilskinCoat",
                Mesh = Vest.Mesh,
                Skin = Vest.Skin,
                Skeleton = Vest.Skeleton,
                Transform = Vest.Transform,
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = ItemModels.Oilskin,
                    Roughness = 0.45f,
                    Grow = true,
                    GrowAmount = CoatGrow / _metresPerUnit,
                },
            };
            parent.AddChild(body);
            nodes.Add(body);
        }
        else
        {
            Torso(nodes, ItemModels.Oilskin, 1.0f);
        }
        foreach (string side in new[] { "Left", "Right" })
        {
            Sleeve(nodes, side + "Arm", side + "ForeArm", SleeveRadius);
            Sleeve(nodes, side + "ForeArm", side + "Hand", SleeveRadius * 0.85f);
        }
        Node3D? hips = Attach("Hips", nodes);
        if (hips != null)
        {
            MeshInstance3D skirt = ItemModels.Part(hips, new CylinderMesh { TopRadius = 0.19f, BottomRadius = 0.23f, Height = 0.26f, RadialSegments = 16, CapTop = false, CapBottom = false },
                ItemModels.Oilskin, Vector3.Down * 0.1f, default, 0.45f);
            skirt.Scale = new Vector3(1.0f, 1.0f, 0.72f);
        }
    }

    /// <summary>
    /// The drawn torso's half width and half depth (m), from this model: width from the shoulder
    /// joints, depth from the body meshes' rest bounds, so a broad character's vest isn't swallowed.
    /// </summary>
    private void MeasureTorso()
    {
        _torsoX = TorsoRadius;
        _torsoZ = TorsoRadius * TorsoDepth;
        if (Skeleton == null)
        {
            return;
        }
        float shoulders = (RestOrigin("LeftArm") - RestOrigin("RightArm")).Length() * 0.5f * _metresPerUnit;
        _torsoX = Mathf.Clamp(Mathf.Max(TorsoRadius, shoulders * ShoulderShare), TorsoRadius, TorsoMaxRadius);
        Vector3 back = _frame.Z.Abs();
        foreach (Node node in Skeleton.GetChildren())
        {
            if (node is MeshInstance3D mesh && mesh.Mesh != null)
            {
                Vector3 size = mesh.GetAabb().Size * mesh.Scale;
                float depth = (size.X * back.X + size.Y * back.Y + size.Z * back.Z) * 0.5f * _metresPerUnit;
                _torsoZ = Mathf.Max(_torsoZ, depth + TorsoSlack);
            }
        }
        _torsoZ = Mathf.Min(_torsoZ, TorsoMaxRadius);
    }

    /// <summary>A hi-vis vest: an open torso shell with two reflective bands.</summary>
    /// <summary>The drawn torso's half width and half depth (m), for tests.</summary>
    public Vector2 TorsoSize => new(_torsoX, _torsoZ);

    private void ShowVest(List<Node3D> nodes)
    {
        Node3D? piece = Torso(nodes, ItemModels.HiVis, 0.85f);
        if (piece == null)
        {
            return;
        }
        Vector3 chest = Local("Spine1", RestOrigin("Neck")) * 0.55f;
        foreach (float y in new[] { 0.0f, -0.14f })
        {
            MeshInstance3D band = ItemModels.Part(piece, new CylinderMesh { TopRadius = _torsoX * 1.04f, BottomRadius = _torsoX * 1.04f, Height = 0.035f, RadialSegments = 20, CapTop = false, CapBottom = false },
                ItemModels.Reflective, chest + Vector3.Up * y, default, 0.2f);
            band.Scale = new Vector3(1.0f, 1.0f, _torsoZ / _torsoX);
        }
    }

    /// <summary>
    /// A rigid shell round the torso (hips to the base of the neck), for models without a vest mesh to
    /// grow; <paramref name="length"/> is the share of that height it covers, from the top.
    /// </summary>
    private Node3D? Torso(List<Node3D> nodes, Color colour, float length)
    {
        Node3D? piece = Attach("Spine1", nodes);
        if (piece == null)
        {
            return null;
        }
        Vector3 hips = Local("Spine1", RestOrigin("Hips"));
        Vector3 neck = Local("Spine1", RestOrigin("Neck"));
        if (neck.LengthSquared() < 0.0001f)
        {
            return piece;
        }
        float height = (neck.Y - hips.Y) * length;
        Vector3 centre = new(0.0f, neck.Y - height * 0.5f - 0.03f, 0.0f);
        MeshInstance3D shell = ItemModels.Part(piece, new CylinderMesh { TopRadius = _torsoX * 0.97f, BottomRadius = _torsoX, Height = height, RadialSegments = 20, CapTop = false, CapBottom = false },
            colour, centre, default, 0.5f);
        shell.Scale = new Vector3(1.0f, 1.0f, _torsoZ / _torsoX);
        return piece;
    }

    private void Sleeve(List<Node3D> nodes, string bone, string next, float radius)
    {
        Node3D? piece = Attach(bone, nodes);
        if (piece == null)
        {
            return;
        }
        Vector3 to = Local(bone, RestOrigin(next));
        if (to.LengthSquared() < 0.0001f)
        {
            return;
        }
        Limb(piece, Vector3.Zero, to, radius, ItemModels.Oilskin);
    }

    private void ShowCape(List<Node3D> nodes)
    {
        Node3D? piece = Attach("Spine2", nodes);
        if (piece == null)
        {
            return;
        }
        Vector3 neck = Local("Spine2", RestOrigin("Neck"));
        // The model's cape hangs behind -Z; on him "behind" is +Z, so turn it round.
        Node3D holder = new() { Rotation = new Vector3(0.0f, Mathf.Pi, 0.0f), Position = neck + new Vector3(0.0f, 0.02f, 0.03f) };
        piece.AddChild(holder);
        ItemModels.AddCape(holder, Vector3.Zero, CapeWidth, CapeLength);
    }

    /// <summary>A capsule from <paramref name="from"/> to <paramref name="to"/> (piece space, metres).</summary>
    private static MeshInstance3D Limb(Node3D piece, Vector3 from, Vector3 to, float radius, Color colour)
    {
        float length = from.DistanceTo(to) + radius;
        MeshInstance3D limb = ItemModels.Part(piece, new CapsuleMesh { Radius = radius, Height = Mathf.Max(length, radius * 2.0f), RadialSegments = 12, Rings = 4 },
            colour, (from + to) * 0.5f, default, 0.5f);
        limb.Basis = Along(to - from);
        return limb;
    }

    /// <summary>A rotation taking +Y onto <paramref name="direction"/>.</summary>
    private static Basis Along(Vector3 direction)
    {
        Vector3 d = direction.Normalized();
        if (d.Dot(Vector3.Up) < -0.999f)
        {
            return new Basis(Vector3.Right, Mathf.Pi);
        }
        if (d.Dot(Vector3.Up) > 0.999f)
        {
            return Basis.Identity;
        }
        return new Basis(new Quaternion(Vector3.Up, d));
    }

    /// <summary>
    /// A node riding <paramref name="bone"/>, whose local space is the body's frame in metres with its
    /// origin at the bone (at rest), so pieces are placed the same way on any import.
    /// </summary>
    private Node3D? Attach(string bone, List<Node3D> nodes)
    {
        int index = Bone(bone);
        if (Skeleton == null || index < 0)
        {
            return null;
        }
        BoneAttachment3D attachment = new() { Name = "Worn" + bone, BoneName = Skeleton.GetBoneName(index) };
        Skeleton.AddChild(attachment);
        nodes.Add(attachment);
        Transform3D rest = Skeleton.GetBoneGlobalRest(index);
        Node3D piece = new() { Name = "Piece" };
        attachment.AddChild(piece);
        piece.Transform = rest.AffineInverse() * new Transform3D(_frame.Scaled(Vector3.One / _metresPerUnit), rest.Origin);
        return piece;
    }

    /// <summary>A skeleton-space point in the piece space of <paramref name="bone"/> (body frame, metres).</summary>
    private Vector3 Local(string bone, Vector3 point)
    {
        if (Bone(bone) < 0 || point == Vector3.Zero)
        {
            return Vector3.Zero;
        }
        return _frame.Transposed() * (point - RestOrigin(bone)) * _metresPerUnit;
    }

    private Vector3 RestOrigin(string bone)
    {
        int index = Bone(bone);
        if (Skeleton == null || index < 0)
        {
            return Vector3.Zero;
        }
        return Skeleton.GetBoneGlobalRest(index).Origin;
    }

    /// <summary>The bone named <paramref name="name"/>, with or without a "mixamorig:"/"mixamorig_" prefix.</summary>
    private int Bone(string name)
    {
        if (Skeleton == null)
        {
            return -1;
        }
        for (int i = 0; i < Skeleton.GetBoneCount(); i++)
        {
            string bone = Skeleton.GetBoneName(i);
            if (bone == name || bone.EndsWith(":" + name) || bone.EndsWith("_" + name))
            {
                return i;
            }
        }
        return -1;
    }
}
