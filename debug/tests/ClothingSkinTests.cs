using System;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using Godot;
namespace DownhillRoguelite.Debug;

public partial class ClothingSkinTests : Node3D
{
    private int _failures;
    public override void _Ready()
    {
        Node3D character = GD.Load<PackedScene>("res://art/characters/vucic/vucic.glb").Instantiate<Node3D>();
        AddChild(character);
        Skeleton3D? rig = null;
        foreach (Node node in character.FindChildren("*", "Skeleton3D", true, false)) { rig = node as Skeleton3D; break; }
        if (rig == null) { GD.Print("FAIL  no test skeleton"); GetTree().Quit(1); return; }
        MeshInstance3D? body = null;
        foreach (Node node in rig.FindChildren("*", "MeshInstance3D", true, false))
        { if (node is MeshInstance3D mesh && mesh.Skin != null) { body = mesh; break; } }
        Check("same-rig imported skin passes bind/rest validation", body != null && ClothingCompatibility.TryBind(body, rig, rig, out _, out _));
        Skeleton3D different = new(); AddChild(different);
        for (int i = 0; i < rig.GetBoneCount(); i++) { different.AddBone(rig.GetBoneName(i)); }
        for (int i = 0; i < rig.GetBoneCount(); i++) { different.SetBoneParent(i, rig.GetBoneParent(i)); different.SetBoneRest(i, rig.GetBoneRest(i)); }
        Transform3D changed = different.GetBoneRest(0); changed.Origin += new Vector3(0.1f, 0, 0); different.SetBoneRest(0, changed);
        Check("identical bone names with different rests are rejected", body != null && !ClothingCompatibility.TryBind(body, rig, different, out _, out _));
        MeshInstance3D unskinned = new() { Mesh = new BoxMesh() };
        Check("missing skin data is rejected gracefully", !ClothingCompatibility.TryBind(unskinned, rig, rig, out _, out _));
        unskinned.Free();
        foreach (ClothingVisual.VisualKind kind in new[] { ClothingVisual.VisualKind.PrototypeShirt, ClothingVisual.VisualKind.PrototypeJacket,
            ClothingVisual.VisualKind.PrototypeTrousers, ClothingVisual.VisualKind.PrototypeGloves })
        {
            MeshInstance3D? garment = ClothingPrototypes.Garment(rig, new ClothingVisual { Kind = kind });
            bool valid = garment?.Mesh != null && garment.Skin != null;
            if (valid)
            {
                Godot.Collections.Array data = garment!.Mesh!.SurfaceGetArrays(0);
                Vector3[] vertices = data[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                int[] bones = data[(int)Mesh.ArrayType.Bones].AsInt32Array();
                float[] weights = data[(int)Mesh.ArrayType.Weights].AsFloat32Array();
                valid = vertices.Length > 0 && bones.Length == vertices.Length * 4 && weights.Length == bones.Length;
                for (int v = 0; v < vertices.Length && valid; v++)
                {
                    float sum = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int index = v * 4 + k;
                        valid &= float.IsFinite(weights[index]) && weights[index] >= 0 && bones[index] >= 0 && bones[index] < garment.Skin!.GetBindCount();
                        sum += weights[index];
                    }
                    // Uploaded weights are UNORM16; two nonzero weights can lose two quantization units.
                    if (!vertices[v].IsFinite() || Mathf.Abs(sum - 1) > 2.0f / 65535.0f)
                    {
                        GD.Print($"SKIN DETAIL {kind} vertex={v}, sum={sum:R}, bone-count={bones.Length}, weights={weights.Length}, verts={vertices.Length}, finite={vertices[v].IsFinite()}");
                        valid = false;
                    }
                }
            }
            Check(kind + " has finite geometry and normalized valid skin weights", valid);
        }
        GetTree().Quit(Math.Min(_failures, 1));
    }
    private void Check(string name, bool pass)
    {
        if (pass) { GD.Print("PASS  " + name); }
        else { GD.Print("FAIL  " + name); _failures++; }
    }
}
