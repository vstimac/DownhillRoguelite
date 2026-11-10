using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// A skinned garment's hand fit: keeps its original mesh and bakes the corrections (one for the
/// whole garment, or one per side when its left and right halves are apart, like gloves and boots)
/// into the vertices at the rest pose, so the garment still bends with the bones. Kept on the mesh
/// node as metadata, so fitting it again starts from the original.
/// </summary>
public partial class ClothingMeshFit : RefCounted
{
    private const string MetaKey = "clothing_mesh_fit";

    /// <summary>True when no triangle crosses the body's midline: the halves can be fitted apart.</summary>
    public bool Split { get; private set; }

    private MeshInstance3D? _instance;
    private ArrayMesh? _original;
    /// <summary>Mesh space to skeleton space at the rest pose.</summary>
    private Transform3D _toSkeleton = Transform3D.Identity;
    /// <summary>Per surface, per vertex: -1 on the body's left, 1 on its right.</summary>
    private int[][] _sides = System.Array.Empty<int[]>();
    private Transform3D _whole = Transform3D.Identity;
    private Transform3D _left = Transform3D.Identity;
    private Transform3D _right = Transform3D.Identity;

    /// <summary>The fit of <paramref name="mesh"/> (made on first use; null if it isn't a skinned array mesh).</summary>
    public static ClothingMeshFit? For(MeshInstance3D mesh, Skeleton3D skeleton, Basis frame, Vector3 midline)
    {
        if (mesh.HasMeta(MetaKey) && mesh.GetMeta(MetaKey).AsGodotObject() is ClothingMeshFit existing)
        {
            return existing;
        }
        if (mesh.Mesh is not ArrayMesh array || mesh.Skin == null || mesh.Skin.GetBindCount() == 0)
        {
            return null;
        }
        ClothingMeshFit fit = new() { _instance = mesh, _original = array };
        Skin skin = mesh.Skin;
        int bone = skin.GetBindBone(0);
        StringName bindName = skin.GetBindName(0);
        if (bindName != null && !bindName.IsEmpty)
        {
            bone = skeleton.FindBone(bindName);
        }
        if (bone >= 0 && bone < skeleton.GetBoneCount())
        {
            fit._toSkeleton = skeleton.GetBoneGlobalRest(bone) * skin.GetBindPose(0);
        }
        fit.Classify(frame, midline);
        mesh.SetMeta(MetaKey, fit);
        return fit;
    }

    private void Classify(Basis frame, Vector3 midline)
    {
        if (_original == null)
        {
            return;
        }
        int surfaces = _original.GetSurfaceCount();
        _sides = new int[surfaces][];
        bool crossing = false;
        bool anyLeft = false;
        bool anyRight = false;
        for (int s = 0; s < surfaces; s++)
        {
            Godot.Collections.Array arrays = _original.SurfaceGetArrays(s);
            Vector3[] positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            float[] across = new float[positions.Length];
            int[] side = new int[positions.Length];
            for (int v = 0; v < positions.Length; v++)
            {
                across[v] = frame.X.Dot(_toSkeleton * positions[v] - midline);
                side[v] = 1;
                if (across[v] < 0.0f)
                {
                    side[v] = -1;
                    anyLeft = true;
                }
                else
                {
                    anyRight = true;
                }
            }
            _sides[s] = side;
            for (int t = 0; t + 2 < indices.Length && !crossing; t += 3)
            {
                float low = Mathf.Min(across[indices[t]], Mathf.Min(across[indices[t + 1]], across[indices[t + 2]]));
                float high = Mathf.Max(across[indices[t]], Mathf.Max(across[indices[t + 1]], across[indices[t + 2]]));
                crossing = low < 0.0f && high > 0.0f;
            }
            if (indices.Length == 0 && positions.Length > 0)
            {
                crossing = true;
            }
        }
        Split = !crossing && anyLeft && anyRight;
    }

    /// <summary>Skeleton-space bounds at rest of one side (-1 left, 1 right) or of everything (0).</summary>
    public Aabb Bounds(int side)
    {
        Aabb box = new();
        bool any = false;
        if (_original == null)
        {
            return box;
        }
        for (int s = 0; s < _original.GetSurfaceCount(); s++)
        {
            Vector3[] positions = _original.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            for (int v = 0; v < positions.Length; v++)
            {
                if (side != 0 && _sides[s][v] != side)
                {
                    continue;
                }
                Vector3 point = _toSkeleton * positions[v];
                if (!any)
                {
                    box = new Aabb(point, Vector3.Zero);
                    any = true;
                }
                else
                {
                    box = box.Expand(point);
                }
            }
        }
        return box;
    }

    /// <summary>Sets one correction (skeleton space, at rest) and rebuilds the mesh.</summary>
    public void Set(int side, Transform3D correction)
    {
        if (side < 0)
        {
            _left = correction;
        }
        else if (side > 0)
        {
            _right = correction;
        }
        else
        {
            _whole = correction;
        }
        Rebuild();
    }

    private void Rebuild()
    {
        if (_original == null || _instance == null || !IsInstanceValid(_instance))
        {
            return;
        }
        if (_whole.IsEqualApprox(Transform3D.Identity) && _left.IsEqualApprox(Transform3D.Identity) && _right.IsEqualApprox(Transform3D.Identity))
        {
            _instance.Mesh = _original;
            return;
        }
        Transform3D fromSkeleton = _toSkeleton.AffineInverse();
        Transform3D wholeMesh = fromSkeleton * _whole * _toSkeleton;
        Transform3D leftMesh = fromSkeleton * _whole * _left * _toSkeleton;
        Transform3D rightMesh = fromSkeleton * _whole * _right * _toSkeleton;
        ArrayMesh rebuilt = new();
        for (int s = 0; s < _original.GetSurfaceCount(); s++)
        {
            Godot.Collections.Array arrays = _original.SurfaceGetArrays(s);
            Vector3[] positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            Vector3[] normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            float[] tangents = arrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
            for (int v = 0; v < positions.Length; v++)
            {
                Transform3D move = wholeMesh;
                if (Split && _sides[s][v] < 0)
                {
                    move = leftMesh;
                }
                else if (Split)
                {
                    move = rightMesh;
                }
                positions[v] = move * positions[v];
                if (normals.Length == positions.Length)
                {
                    normals[v] = (move.Basis.Inverse().Transposed() * normals[v]).Normalized();
                }
                if (tangents.Length == positions.Length * 4)
                {
                    Vector3 tangent = (move.Basis * new Vector3(tangents[v * 4], tangents[v * 4 + 1], tangents[v * 4 + 2])).Normalized();
                    tangents[v * 4] = tangent.X;
                    tangents[v * 4 + 1] = tangent.Y;
                    tangents[v * 4 + 2] = tangent.Z;
                }
            }
            arrays[(int)Mesh.ArrayType.Vertex] = positions;
            if (normals.Length == positions.Length)
            {
                arrays[(int)Mesh.ArrayType.Normal] = normals;
            }
            if (tangents.Length == positions.Length * 4)
            {
                arrays[(int)Mesh.ArrayType.Tangent] = tangents;
            }
            Mesh.ArrayFormat flags = _original.SurfaceGetFormat(s) & Mesh.ArrayFormat.FlagUse8BoneWeights;
            rebuilt.AddSurfaceFromArrays(_original.SurfaceGetPrimitiveType(s), arrays, null, null, flags);
            rebuilt.SurfaceSetMaterial(s, _original.SurfaceGetMaterial(s));
            rebuilt.SurfaceSetName(s, _original.SurfaceGetName(s));
        }
        _instance.Mesh = rebuilt;
    }
}
