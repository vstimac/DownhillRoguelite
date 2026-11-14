using System;
using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// The thick carpet of short grass round the carrier (terrain/grass_carpet.gdshader). One clump
/// mesh and one MultiMesh of identity instances are shared by every chunk; each chunk adds four
/// quarters of it with a material holding two small textures, its heights and its carpet mask
/// (TerrainChunkData.CarpetMask). The shader places, grounds, colours and thins every clump, so a
/// chunk costs no transform buffer, no plant plan entries and no CPU work beyond the two uploads.
/// </summary>
public static class GrassCarpet
{
    public const string NodeName = "GrassCarpet";
    /// <summary>Blades in one clump, and the clump's radius in m.</summary>
    public const int BladesPerClump = 14;
    public const float ClumpRadius = 0.28f;
    /// <summary>A quarter is this wide, in m (a 64 m chunk has four).</summary>
    public const float QuarterSize = 32.0f;

    private static ArrayMesh? _clump;
    private static MultiMesh? _quarter;
    private static float _quarterSpacing;

    /// <summary>One clump: thin triangle blades round its root, 1 m tall (the shader scales them), UV.y up the blade.</summary>
    public static ArrayMesh Clump()
    {
        if (_clump != null)
        {
            return _clump;
        }
        Vector3[] vertices = new Vector3[BladesPerClump * 3];
        Vector3[] normals = new Vector3[BladesPerClump * 3];
        Vector2[] uvs = new Vector2[BladesPerClump * 3];
        // A fixed layout (golden-angle spiral), so the clump looks the same on every machine.
        for (int b = 0; b < BladesPerClump; b++)
        {
            float angle = b * 2.39996f;
            float reach = ClumpRadius * Mathf.Sqrt((b + 0.5f) / BladesPerClump);
            Vector3 root = new(Mathf.Cos(angle) * reach, 0.0f, Mathf.Sin(angle) * reach);
            float facing = angle * 1.7f + b;
            Vector3 side = new(Mathf.Cos(facing) * 0.022f, 0.0f, Mathf.Sin(facing) * 0.022f);
            float height = 0.65f + 0.35f * ((b * 37) % 11) / 10.0f;
            Vector3 lean = new Vector3(root.X, 0.0f, root.Z) * 0.6f;
            int v = b * 3;
            vertices[v] = root - side;
            vertices[v + 1] = root + side;
            vertices[v + 2] = root + lean + Vector3.Up * height;
            Vector3 normal = side.Cross(Vector3.Up).Normalized();
            for (int k = 0; k < 3; k++)
            {
                normals[v + k] = normal;
            }
            uvs[v] = new Vector2(0.0f, 0.0f);
            uvs[v + 1] = new Vector2(1.0f, 0.0f);
            uvs[v + 2] = new Vector2(0.5f, 1.0f);
        }
        using Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        _clump = new ArrayMesh();
        _clump.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return _clump;
    }

    /// <summary>The shared quarter: a square of clumps <paramref name="spacing"/> m apart, all at the origin (the shader places them).</summary>
    public static MultiMesh Quarter(float spacing)
    {
        if (_quarter != null && Mathf.IsEqualApprox(_quarterSpacing, spacing))
        {
            return _quarter;
        }
        int columns = Columns(spacing);
        int count = columns * columns;
        float[] buffer = new float[count * 12];
        for (int i = 0; i < count; i++)
        {
            int o = i * 12;
            buffer[o] = 1.0f;
            buffer[o + 5] = 1.0f;
            buffer[o + 10] = 1.0f;
        }
        _quarter = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = Clump(), InstanceCount = count };
        _quarter.Buffer = buffer;
        _quarterSpacing = spacing;
        return _quarter;
    }

    public static int Columns(float spacing)
    {
        return Mathf.Max(Mathf.RoundToInt(QuarterSize / Mathf.Max(spacing, 0.1f)), 1);
    }

    /// <summary>
    /// Adds the carpet's quarters to a chunk (main thread). Nothing is added without a mask, a shader
    /// or a draw distance. Returns the carpet node, or null.
    /// </summary>
    public static Node3D? Add(Node3D chunk, TerrainChunkData data, Shader? shader, TerrainSettings settings, float drawDistance)
    {
        if (shader == null || data.CarpetMask.Length != data.GridPoints * data.GridPoints * 4 || drawDistance <= 0.0f)
        {
            return null;
        }
        if (!HasCarpet(data.CarpetMask))
        {
            return null;
        }
        int points = data.GridPoints;
        byte[] heightBytes = new byte[points * points * sizeof(float)];
        Buffer.BlockCopy(data.Heights, 0, heightBytes, 0, heightBytes.Length);
        ImageTexture heights = ImageTexture.CreateFromImage(Image.CreateFromData(points, points, false, Image.Format.Rf, heightBytes));
        ImageTexture mask = ImageTexture.CreateFromImage(Image.CreateFromData(points, points, false, Image.Format.Rgba8, data.CarpetMask));
        float spacing = settings.CarpetSpacing;
        ShaderMaterial material = new() { Shader = shader };
        material.SetShaderParameter("heights", heights);
        material.SetShaderParameter("carpet", mask);
        material.SetShaderParameter("grid_points", points);
        material.SetShaderParameter("cell_size", settings.CellSize);
        material.SetShaderParameter("columns", Columns(spacing));
        material.SetShaderParameter("spacing", spacing);
        material.SetShaderParameter("draw_distance", drawDistance);
        material.SetShaderParameter("blade_height", settings.CarpetBladeHeight);
        Node3D carpet = new() { Name = NodeName };
        MultiMesh quarter = Quarter(spacing);
        float size = settings.ChunkSize;
        int quarters = Mathf.Max(Mathf.RoundToInt(size / QuarterSize), 1);
        for (int qz = 0; qz < quarters; qz++)
        {
            for (int qx = 0; qx < quarters; qx++)
            {
                Vector2 origin = new(qx * QuarterSize, qz * QuarterSize);
                if (!QuarterBounds(data, settings.CellSize, origin, out float low, out float high))
                {
                    continue;
                }
                MultiMeshInstance3D part = new()
                {
                    Name = "Quarter",
                    Multimesh = quarter,
                    MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    CustomAabb = new Aabb(new Vector3(origin.X - 0.5f, low - 0.5f, origin.Y - 0.5f), new Vector3(QuarterSize + 1.0f, high - low + settings.CarpetBladeHeight * 2.0f + 1.0f, QuarterSize + 1.0f)),
                    VisibilityRangeEnd = drawDistance + QuarterSize * 0.71f,
                };
                part.SetInstanceShaderParameter("quarter_origin", origin);
                carpet.AddChild(part);
            }
        }
        if (carpet.GetChildCount() == 0)
        {
            carpet.Free();
            return null;
        }
        chunk.AddChild(carpet);
        return carpet;
    }

    private static bool HasCarpet(byte[] mask)
    {
        for (int i = 3; i < mask.Length; i += 4)
        {
            if (mask[i] > 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Height range of a quarter, and false when no carpet grows anywhere in it.</summary>
    private static bool QuarterBounds(TerrainChunkData data, float cell, Vector2 origin, out float low, out float high)
    {
        int points = data.GridPoints;
        int from = Mathf.Clamp(Mathf.FloorToInt(origin.X / cell), 0, points - 1);
        int to = Mathf.Clamp(Mathf.CeilToInt((origin.X + QuarterSize) / cell), 0, points - 1);
        int fromZ = Mathf.Clamp(Mathf.FloorToInt(origin.Y / cell), 0, points - 1);
        int toZ = Mathf.Clamp(Mathf.CeilToInt((origin.Y + QuarterSize) / cell), 0, points - 1);
        low = float.MaxValue;
        high = float.MinValue;
        bool any = false;
        for (int j = fromZ; j <= toZ; j++)
        {
            for (int i = from; i <= to; i++)
            {
                int at = i + j * points;
                low = Mathf.Min(low, data.Heights[at]);
                high = Mathf.Max(high, data.Heights[at]);
                if (data.CarpetMask[at * 4 + 3] > 0)
                {
                    any = true;
                }
            }
        }
        return any;
    }
}
