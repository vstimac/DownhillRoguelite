using System;
using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// The thick carpet of short grass round the carrier (terrain/grass_carpet.gdshader). One clump
/// mesh and one MultiMesh of identity instances are shared by every chunk; each chunk adds sixteen
/// patches of it with a material holding two small textures, its heights and its carpet mask
/// (TerrainChunkData.CarpetMask). The shader places, grounds, colours and thins every clump, so a
/// chunk costs no transform buffer, no plant plan entries and no CPU work beyond the two uploads.
/// </summary>
public static class GrassCarpet
{
    public const string NodeName = "GrassCarpet";
    /// <summary>Blades in one clump, and the clump's radius in m.</summary>
    public const int BladesPerClump = 18;
    public const float ClumpRadius = 0.27f;
    /// <summary>Half a blade's width at its root, in m; how far its tip hangs outward (a share of its distance from the clump's centre); and where it bends (a share of its height).</summary>
    public const float BladeHalfWidth = 0.011f;
    public const float BladeLean = 0.22f;
    public const float BendAt = 0.55f;
    /// <summary>
    /// A patch of the carpet is this wide, in m (a 64 m chunk has sixteen). Each patch is drawn or
    /// skipped as a whole by its distance, so smaller patches waste fewer blades beyond the draw
    /// distance: at 32 m the carpet cost about three million triangles a frame, at 16 m about half.
    /// </summary>
    public const float QuarterSize = 16.0f;

    /// <summary>
    /// The ground's own soil texture, its tile size in m and how strongly the ground shows it
    /// (TerrainStreamer sets these to what its ground material uses), so the blades take the colour
    /// of the ground they stand on and not only its flat vertex colour.
    /// </summary>
    public static Texture2D? Soil { get; set; }
    public static float SoilMetres { get; set; } = 3.2f;
    public static float SoilStrength { get; set; }

    private static ArrayMesh? _clump;
    private static MultiMesh? _quarter;
    private static float _quarterSpacing;

    /// <summary>
    /// One clump: bent blades round its root (a wide lower half and a narrowing tip, three triangles
    /// each), 1 m tall (the shader scales them), UV.y up the blade. The blades stand nearly upright
    /// and lean only a little outward: splayed wide, a clump reads as a spiked star, not as grass.
    /// </summary>
    public static ArrayMesh Clump()
    {
        if (_clump != null)
        {
            return _clump;
        }
        const int perBlade = 5;
        Vector3[] vertices = new Vector3[BladesPerClump * perBlade];
        Vector3[] normals = new Vector3[BladesPerClump * perBlade];
        Vector2[] uvs = new Vector2[BladesPerClump * perBlade];
        int[] indices = new int[BladesPerClump * 9];
        // A fixed layout (golden-angle spiral), so the clump looks the same on every machine.
        for (int b = 0; b < BladesPerClump; b++)
        {
            float angle = b * 2.39996f;
            float reach = ClumpRadius * Mathf.Sqrt((b + 0.5f) / BladesPerClump);
            Vector3 root = new(Mathf.Cos(angle) * reach, 0.0f, Mathf.Sin(angle) * reach);
            float facing = angle * 1.7f + b;
            Vector3 across = new(Mathf.Cos(facing), 0.0f, Mathf.Sin(facing));
            Vector3 side = across * BladeHalfWidth;
            float height = 0.55f + 0.45f * ((b * 37) % 11) / 10.0f;
            // The tip hangs a little outward and to one side of the blade's own face.
            float sideways = ((b * 53) % 7) / 6.0f - 0.5f;
            Vector3 hang = new Vector3(root.X, 0.0f, root.Z) * BladeLean + across.Cross(Vector3.Up) * (0.07f * sideways);
            Vector3 middle = root + hang * 0.3f + Vector3.Up * (height * BendAt);
            int v = b * perBlade;
            vertices[v] = root - side;
            vertices[v + 1] = root + side;
            vertices[v + 2] = middle - side * 0.75f;
            vertices[v + 3] = middle + side * 0.75f;
            vertices[v + 4] = root + hang + Vector3.Up * height;
            Vector3 normal = side.Cross(Vector3.Up).Normalized();
            for (int k = 0; k < perBlade; k++)
            {
                normals[v + k] = normal;
            }
            uvs[v] = new Vector2(0.0f, 0.0f);
            uvs[v + 1] = new Vector2(1.0f, 0.0f);
            uvs[v + 2] = new Vector2(0.0f, BendAt);
            uvs[v + 3] = new Vector2(1.0f, BendAt);
            uvs[v + 4] = new Vector2(0.5f, 1.0f);
            int at = b * 9;
            indices[at] = v;
            indices[at + 1] = v + 1;
            indices[at + 2] = v + 3;
            indices[at + 3] = v;
            indices[at + 4] = v + 3;
            indices[at + 5] = v + 2;
            indices[at + 6] = v + 2;
            indices[at + 7] = v + 3;
            indices[at + 8] = v + 4;
        }
        using Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
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
        // The clumps fill a patch exactly: the asked spacing, rounded to a whole number of columns.
        float spacing = QuarterSize / Columns(settings.CarpetSpacing);
        ShaderMaterial material = new() { Shader = shader };
        material.SetShaderParameter("heights", heights);
        material.SetShaderParameter("carpet", mask);
        material.SetShaderParameter("grid_points", points);
        material.SetShaderParameter("cell_size", settings.CellSize);
        material.SetShaderParameter("columns", Columns(spacing));
        material.SetShaderParameter("spacing", spacing);
        material.SetShaderParameter("draw_distance", drawDistance);
        material.SetShaderParameter("blade_height", settings.CarpetBladeHeight);
        if (Soil != null && SoilStrength > 0.0f)
        {
            material.SetShaderParameter("soil", Soil);
            material.SetShaderParameter("soil_metres", Mathf.Max(SoilMetres, 0.1f));
            material.SetShaderParameter("soil_strength", SoilStrength);
        }
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
