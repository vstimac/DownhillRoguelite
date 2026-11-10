using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// Gives a tree model the wind (terrain/foliage_tree.gdshader): a copy of its mesh whose surfaces
/// keep their colour, texture and vertex colours but sway and flutter. Generated pines already carry
/// the wind material and are used as they are. One copy per model, made once.
/// </summary>
public static class FoliageMaterials
{
    public const string TreeShaderPath = "res://terrain/foliage_tree.gdshader";

    private static readonly Dictionary<ulong, Mesh> WindMeshes = new();
    private static Shader? _treeShader;

    public static Shader? TreeShader()
    {
        if (_treeShader == null && ResourceLoader.Exists(TreeShaderPath))
        {
            _treeShader = ResourceLoader.Load<Shader>(TreeShaderPath);
        }
        return _treeShader;
    }

    /// <summary>The model in the wind (its own mesh when it already sways, or when it can't be copied).</summary>
    public static Mesh WindReady(Mesh mesh)
    {
        ulong id = mesh.GetInstanceId();
        if (WindMeshes.TryGetValue(id, out Mesh? ready))
        {
            return ready;
        }
        ready = Build(mesh);
        WindMeshes[id] = ready;
        return ready;
    }

    private static Mesh Build(Mesh mesh)
    {
        Shader? shader = TreeShader();
        if (shader == null || mesh is not ArrayMesh source)
        {
            return mesh;
        }
        bool sways = true;
        for (int i = 0; i < source.GetSurfaceCount(); i++)
        {
            if (source.SurfaceGetMaterial(i) is not ShaderMaterial shaderMaterial || shaderMaterial.Shader != shader)
            {
                sways = false;
            }
        }
        if (sways)
        {
            return mesh;
        }
        Aabb bounds = source.GetAabb();
        ArrayMesh copy = (ArrayMesh)source.Duplicate();
        for (int i = 0; i < copy.GetSurfaceCount(); i++)
        {
            copy.SurfaceSetMaterial(i, WindMaterial(source.SurfaceGetMaterial(i), shader, bounds));
        }
        return copy;
    }

    private static ShaderMaterial WindMaterial(Material? original, Shader shader, Aabb bounds)
    {
        ShaderMaterial material = new() { Shader = shader };
        material.SetShaderParameter("model_base", bounds.Position.Y);
        material.SetShaderParameter("model_height", Mathf.Max(bounds.Size.Y, 0.01f));
        material.SetShaderParameter("flex_in_uv", false);
        if (original is BaseMaterial3D standard)
        {
            material.SetShaderParameter("albedo", standard.AlbedoColor);
            material.SetShaderParameter("use_vertex_colour", standard.VertexColorUseAsAlbedo);
            material.SetShaderParameter("roughness", standard.Roughness);
            if (standard.AlbedoTexture != null)
            {
                material.SetShaderParameter("albedo_texture", standard.AlbedoTexture);
                material.SetShaderParameter("use_texture", true);
            }
        }
        else
        {
            material.SetShaderParameter("use_vertex_colour", true);
        }
        return material;
    }
}
