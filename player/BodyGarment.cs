using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// Clothes cut from the character's own body: the skin triangles that the given bones drive, pushed
/// out along their normals and skinned to the same skeleton. They fit any body and bend exactly like
/// it, so a modular base character (Quaternius base characters, in underwear) gets a shirt, trousers
/// and boots without a model per outfit. One extra draw call per garment; no new bones.
/// </summary>
public static class BodyGarment
{
    /// <summary>The skinned mesh with the most vertices under <paramref name="skeleton"/>: the body.</summary>
    public static MeshInstance3D? FindBody(Skeleton3D skeleton)
    {
        MeshInstance3D? body = null;
        int most = 0;
        foreach (Node node in skeleton.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mesh || mesh.Mesh is not ArrayMesh array || mesh.Skin == null || array.GetSurfaceCount() == 0)
            {
                continue;
            }
            int count = array.SurfaceGetArrayLen(0);
            if (count > most)
            {
                most = count;
                body = mesh;
            }
        }
        return body;
    }

    /// <summary>
    /// A garment over the body triangles touching the named bones (a triangle with any vertex mostly on
    /// one of them), pushed out by <paramref name="inflate"/> (the body mesh's units). Null if none match.
    /// </summary>
    public static MeshInstance3D? Cut(MeshInstance3D body, string[] bones, float inflate, string name)
    {
        if (body.Mesh is not ArrayMesh source || body.Skin == null || body.GetParent() is not Skeleton3D skeleton)
        {
            return null;
        }
        HashSet<int> wanted = new();
        foreach (string bone in bones)
        {
            int index = skeleton.FindBone(bone);
            if (index >= 0)
            {
                wanted.Add(index);
            }
        }
        // Bind index → skeleton bone, as the vertices' bone indices point into the skin's binds.
        Skin skin = body.Skin;
        int[] bindBone = new int[skin.GetBindCount()];
        for (int i = 0; i < bindBone.Length; i++)
        {
            bindBone[i] = skin.GetBindBone(i);
            StringName bindName = skin.GetBindName(i);
            if (bindName != null && !bindName.IsEmpty)
            {
                bindBone[i] = skeleton.FindBone(bindName);
            }
        }

        List<Vector3> vertices = new();
        List<Vector3> normals = new();
        List<Vector2> uvs = new();
        List<int> vertexBones = new();
        List<float> vertexWeights = new();
        List<int> indices = new();
        int perVertex = 4;
        for (int surface = 0; surface < source.GetSurfaceCount(); surface++)
        {
            Godot.Collections.Array arrays = source.SurfaceGetArrays(surface);
            Vector3[] position = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Vector3[] normal = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            Vector2[] uv = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            int[] boneIndex = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            float[] weight = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            int[] index = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (position.Length == 0 || normal.Length != position.Length || boneIndex.Length == 0 || index.Length == 0)
            {
                continue;
            }
            perVertex = boneIndex.Length / position.Length;
            bool[] covered = new bool[position.Length];
            for (int v = 0; v < position.Length; v++)
            {
                int strongest = 0;
                for (int k = 1; k < perVertex; k++)
                {
                    if (weight[v * perVertex + k] > weight[v * perVertex + strongest])
                    {
                        strongest = k;
                    }
                }
                int bind = boneIndex[v * perVertex + strongest];
                covered[v] = bind >= 0 && bind < bindBone.Length && wanted.Contains(bindBone[bind]);
            }
            Dictionary<int, int> remap = new();
            for (int t = 0; t + 2 < index.Length; t += 3)
            {
                if (!covered[index[t]] && !covered[index[t + 1]] && !covered[index[t + 2]])
                {
                    continue;
                }
                for (int c = 0; c < 3; c++)
                {
                    int v = index[t + c];
                    if (!remap.TryGetValue(v, out int mapped))
                    {
                        mapped = vertices.Count;
                        remap[v] = mapped;
                        vertices.Add(position[v] + normal[v] * inflate);
                        normals.Add(normal[v]);
                        if (uv.Length == position.Length)
                        {
                            uvs.Add(uv[v]);
                        }
                        for (int k = 0; k < perVertex; k++)
                        {
                            vertexBones.Add(boneIndex[v * perVertex + k]);
                            vertexWeights.Add(weight[v * perVertex + k]);
                        }
                    }
                    indices.Add(mapped);
                }
            }
        }
        if (indices.Count == 0)
        {
            return null;
        }
        Godot.Collections.Array cut = new();
        cut.Resize((int)Mesh.ArrayType.Max);
        cut[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        cut[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        if (uvs.Count == vertices.Count)
        {
            cut[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        }
        cut[(int)Mesh.ArrayType.Bones] = vertexBones.ToArray();
        cut[(int)Mesh.ArrayType.Weights] = vertexWeights.ToArray();
        cut[(int)Mesh.ArrayType.Index] = indices.ToArray();
        Mesh.ArrayFormat flags = 0;
        if (perVertex == 8)
        {
            flags = Mesh.ArrayFormat.FlagUse8BoneWeights;
        }
        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, cut, null, null, flags);
        MeshInstance3D garment = new()
        {
            Name = name,
            Mesh = mesh,
            Skin = skin,
            Transform = body.Transform,
            CastShadow = body.CastShadow,
        };
        skeleton.AddChild(garment);
        garment.Skeleton = garment.GetPathTo(skeleton);
        return garment;
    }

    /// <summary>
    /// The body without the skin the clothes cover: every surface (with its material) minus the
    /// triangles that have most of their corners mostly on <paramref name="hiddenBones"/>, so the body
    /// can't poke through a modelled garment. Added beside the body, which the caller hides. Null if the
    /// body isn't a skinned mesh.
    /// </summary>
    public static MeshInstance3D? Keep(MeshInstance3D body, ICollection<string> hiddenBones, string name)
    {
        if (body.Mesh is not ArrayMesh source || body.Skin == null || body.GetParent() is not Skeleton3D skeleton)
        {
            return null;
        }
        HashSet<int> hidden = new();
        foreach (string bone in hiddenBones)
        {
            int index = skeleton.FindBone(bone);
            if (index >= 0)
            {
                hidden.Add(index);
            }
        }
        int[] bindBone = BindBones(body.Skin, skeleton);
        ArrayMesh kept = new();
        for (int surface = 0; surface < source.GetSurfaceCount(); surface++)
        {
            Godot.Collections.Array arrays = source.SurfaceGetArrays(surface);
            Vector3[] position = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            int[] boneIndex = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            float[] weight = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            int[] index = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (position.Length == 0 || boneIndex.Length == 0 || index.Length == 0)
            {
                continue;
            }
            int perVertex = boneIndex.Length / position.Length;
            bool[] covered = new bool[position.Length];
            for (int v = 0; v < position.Length; v++)
            {
                int bind = boneIndex[v * perVertex + Strongest(weight, v, perVertex)];
                covered[v] = bind >= 0 && bind < bindBone.Length && hidden.Contains(bindBone[bind]);
            }
            List<int> indices = new();
            for (int t = 0; t + 2 < index.Length; t += 3)
            {
                int corners = 0;
                for (int c = 0; c < 3; c++)
                {
                    if (covered[index[t + c]])
                    {
                        corners++;
                    }
                }
                if (corners >= 2)
                {
                    continue;
                }
                indices.Add(index[t]);
                indices.Add(index[t + 1]);
                indices.Add(index[t + 2]);
            }
            if (indices.Count == 0)
            {
                continue;
            }
            arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
            Mesh.ArrayFormat flags = 0;
            if (perVertex == 8)
            {
                flags = Mesh.ArrayFormat.FlagUse8BoneWeights;
            }
            kept.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, flags);
            Material? material = body.GetSurfaceOverrideMaterial(surface);
            if (material == null)
            {
                material = source.SurfaceGetMaterial(surface);
            }
            kept.SurfaceSetMaterial(kept.GetSurfaceCount() - 1, material);
        }
        if (kept.GetSurfaceCount() == 0)
        {
            return null;
        }
        MeshInstance3D garment = new()
        {
            Name = name,
            Mesh = kept,
            Skin = body.Skin,
            Transform = body.Transform,
            CastShadow = body.CastShadow,
            MaterialOverride = body.MaterialOverride,
        };
        skeleton.AddChild(garment);
        garment.Skeleton = garment.GetPathTo(skeleton);
        return garment;
    }

    /// <summary>Bind index → skeleton bone (vertices' bone indices point into the skin's binds).</summary>
    private static int[] BindBones(Skin skin, Skeleton3D skeleton)
    {
        int[] bindBone = new int[skin.GetBindCount()];
        for (int i = 0; i < bindBone.Length; i++)
        {
            bindBone[i] = skin.GetBindBone(i);
            StringName bindName = skin.GetBindName(i);
            if (bindName != null && !bindName.IsEmpty)
            {
                bindBone[i] = skeleton.FindBone(bindName);
            }
        }
        return bindBone;
    }

    private static int Strongest(float[] weight, int vertex, int perVertex)
    {
        int strongest = 0;
        for (int k = 1; k < perVertex; k++)
        {
            if (weight[vertex * perVertex + k] > weight[vertex * perVertex + strongest])
            {
                strongest = k;
            }
        }
        return strongest;
    }
}
