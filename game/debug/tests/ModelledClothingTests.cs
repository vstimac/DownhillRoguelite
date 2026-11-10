using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Modelled clothes (the Quaternius outfits) on the modular body: an item's fitted variant is worn
/// by the character it lists, the general look by the others; the garment binds to the body's rig,
/// keeps its textures (the skin of its hands as modelled), and the body's skin under it isn't drawn;
/// a jerkin hides the shirt under it; taking everything off brings the whole body back.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/ModelledClothingTests.tscn
/// </summary>
public partial class ModelledClothingTests : Node3D
{
    [Export] public string BodyPath { get; set; } = "res://art/characters/quaternius_ubc/Superhero_Male_FullBody.gltf";

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        ClothingFit.Use(new ClothingFit());
        ItemDefinition shirt = Item("trail_shirt");
        ItemDefinition hood = Item("ranger_hood");
        Check("an item's fitted variant goes to the character it lists, the general look to the rest",
            shirt.ClothingFor("modular") != shirt.Clothing && shirt.ClothingFor("modular")!.Textured && shirt.ClothingFor("carrier") == shirt.Clothing
            && hood.ClothingFor("modular") != null && hood.ClothingFor("carrier") == null, "");

        Node body = GD.Load<PackedScene>(BodyPath).Instantiate();
        AddChild(body);
        Skeleton3D skeleton = body.FindChildren("*", "Skeleton3D", true, false)[0] as Skeleton3D ?? throw new System.InvalidOperationException("no skeleton");
        ClothingVisualController clothing = new() { Name = "ClothingVisuals", Skeleton = skeleton, CharacterId = "modular" };
        AddChild(clothing);
        clothing.Setup(false);
        MeshInstance3D? whole = clothing.ShownBody;
        int wholeTriangles = Triangles(whole);

        clothing.ShowGear(Wearing("trail_shirt", "wool_trousers", "hobnail_boots"));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        bool built = clothing.Visuals.TryGetValue(EquipSlot.InnerTorso, out Node3D? shirtVisual) && clothing.Visuals.ContainsKey(EquipSlot.Legs)
            && clothing.Visuals.ContainsKey(EquipSlot.Feet);
        MeshInstance3D? sleeves = Mesh(shirtVisual, "Arms");
        bool textured = sleeves != null && sleeves.GetSurfaceOverrideMaterial(0) is ShaderMaterial cloth && (bool)cloth.GetShaderParameter("textured")
            && sleeves.GetSurfaceOverrideMaterial(1) == null;
        Check("the shirt, trousers and boots are modelled, bound to the body's rig, with their own textures (hands as modelled)", built && textured,
            $"built {built}, textured {textured}");
        MeshInstance3D? under = clothing.ShownBody;
        int kept = Triangles(under);
        bool chestGone = !Draws(under, skeleton, "spine_02") && !Draws(under, skeleton, "thigh_l") && !Draws(under, skeleton, "foot_r");
        Check("the skin under them isn't drawn; the head still is", under != whole && whole != null && !whole.Visible && kept > 0 && kept < wholeTriangles
            && chestGone && Draws(under, skeleton, "Head"), $"{wholeTriangles} → {kept} triangles, chest/thigh/foot gone {chestGone}");

        clothing.ShowGear(Wearing("trail_shirt", "ranger_jerkin", "ranger_breeches", "hobnail_boots", "ranger_hood"));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        bool shirtHidden = clothing.Visuals.TryGetValue(EquipSlot.InnerTorso, out Node3D? shirtNow) && !shirtNow.Visible
            && clothing.Visuals.TryGetValue(EquipSlot.OuterTorso, out Node3D? jerkin) && jerkin.Visible
            && clothing.Visuals.ContainsKey(EquipSlot.Head);
        Check("the ranger's jerkin covers the shirt and the hood goes on", shirtHidden, "");

        clothing.ShowGear(Wearing());
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check("with everything off the whole body is back", clothing.ShownBody == whole && whole != null && whole.Visible && clothing.Visuals.Count == 0, "");

        if (_failures.Count == 0)
        {
            GD.Print("MODELLED CLOTHING TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("MODELLED CLOTHING TESTS: FAIL (" + string.Join(", ", _failures) + ")");
            GetTree().Quit(1);
        }
    }

    private static ItemDefinition Item(string id) => GD.Load<ItemDefinition>($"res://inventory/items/{id}.tres");

    private static GearSnapshot Wearing(params string[] ids)
    {
        WornPiece[] slots = new WornPiece[EquipmentSlots.Count];
        foreach (string id in ids)
        {
            ItemDefinition item = Item(id);
            slots[(int)item.Slot] = new WornPiece(item.ItemId, item.DisplayName, item.MaxDurability, item.MaxDurability, item);
        }
        return GearSnapshot.None with { Slots = slots };
    }

    private static MeshInstance3D? Mesh(Node? root, string nameEnd)
    {
        if (root == null)
        {
            return null;
        }
        foreach (Node node in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is MeshInstance3D mesh && mesh.Mesh != null && mesh.Mesh.GetSurfaceCount() == 2)
            {
                return mesh;
            }
        }
        return null;
    }

    private static int Triangles(MeshInstance3D? mesh)
    {
        if (mesh?.Mesh is not ArrayMesh array)
        {
            return 0;
        }
        int count = 0;
        for (int surface = 0; surface < array.GetSurfaceCount(); surface++)
        {
            count += array.SurfaceGetArrayIndexLen(surface) / 3;
        }
        return count;
    }

    /// <summary>Whether any drawn triangle has all its corners mostly on this bone.</summary>
    private static bool Draws(MeshInstance3D? mesh, Skeleton3D skeleton, string bone)
    {
        if (mesh?.Mesh is not ArrayMesh array || mesh.Skin == null)
        {
            return false;
        }
        int target = skeleton.FindBone(bone);
        for (int surface = 0; surface < array.GetSurfaceCount(); surface++)
        {
            Godot.Collections.Array arrays = array.SurfaceGetArrays(surface);
            int vertices = arrays[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array().Length;
            int[] bones = arrays[(int)Godot.Mesh.ArrayType.Bones].AsInt32Array();
            float[] weights = arrays[(int)Godot.Mesh.ArrayType.Weights].AsFloat32Array();
            int[] index = arrays[(int)Godot.Mesh.ArrayType.Index].AsInt32Array();
            int per = bones.Length / vertices;
            for (int t = 0; t + 2 < index.Length; t += 3)
            {
                bool all = true;
                for (int c = 0; c < 3 && all; c++)
                {
                    int v = index[t + c];
                    int best = 0;
                    for (int k = 1; k < per; k++)
                    {
                        if (weights[v * per + k] > weights[v * per + best])
                        {
                            best = k;
                        }
                    }
                    int bind = bones[v * per + best];
                    StringName name = mesh.Skin.GetBindName(bind);
                    int on = mesh.Skin.GetBindBone(bind);
                    if (name != null && !name.IsEmpty)
                    {
                        on = skeleton.FindBone(name);
                    }
                    all = on == target;
                }
                if (all)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
