using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Visual terrain changes must preserve physics, match across chunk boundaries, and stay bounded.</summary>
public partial class ForestTerrainTests : Node3D
{
    private int _failed;
    private readonly Color[] _palette = { new(0.56f, 0.57f, 0.6f), new(0.68f, 0.6f, 0.48f), new(0.42f, 0.62f, 0.3f),
        new(0.9f, 0.95f, 1), new(0.4f, 0.3f, 0.2f) };
    public override async void _Ready()
    {
        try { await Run(); }
        catch (Exception error) { Check("forest tests complete without exception", false, error.ToString()); }
        if (_failed == 0) { GD.Print("FOREST TERRAIN TESTS: PASS"); }
        else { GD.Print("FOREST TERRAIN TESTS: FAIL"); }
        GetTree().Quit(Math.Min(1, _failed));
    }

    private async Task Run()
    {
        TerrainSettings settings = new();
        TerrainGenerator smooth = new(settings, 1);
        TerrainChunkData chunk = smooth.BuildChunk(new Vector2I(0, -3), _palette, 0);
        settings = new TerrainSettings { SmoothShading = false };
        TerrainGenerator flat = new(settings, 1);
        TerrainChunkData legacy = flat.BuildChunk(chunk.Coord, _palette, 0);
        bool heights = Equal(chunk.Heights, legacy.Heights);
        bool vertices = Equal(chunk.Vertices, legacy.Vertices);
        bool obstacles = Equal(chunk.Obstacles, legacy.Obstacles);
        Check("smooth shading preserves every vertex, collision height and physical obstacle", heights && vertices && obstacles);
        CheckSeams(smooth, chunk);
        bool finite = true;
        foreach (Vector3 n in chunk.Normals) { finite &= n.IsFinite() && Mathf.Abs(n.Length() - 1) < 0.001f && n.Y > 0; }
        Check("smooth normals remain finite, unit length and upward-facing", finite);
        TerrainChunkData again = smooth.BuildChunk(chunk.Coord, _palette, 0);
        Check("forest and detail placements are deterministic", Equal(chunk.Plants, again.Plants));
        CheckScaleAndAssets(chunk);
        CheckTextures();
        CheckGrounding(smooth, chunk);
        await CheckRenderingNodes(smooth, chunk);
    }

    private void CheckTextures()
    {
        bool valid = true;
        string folder = "res://art/environment/ground/";
        foreach (string asset in new[] { "forest_ground_04_diff_1k", "forest_ground_04_nor_gl_1k", "rock_ground_diff_1k", "rock_ground_nor_gl_1k" })
        {
            Texture2D? texture = ResourceLoader.Load<Texture2D>(folder + asset + ".jpg");
            using Image? image = texture?.GetImage();
            valid &= texture?.GetWidth() == 1024 && texture.GetHeight() == 1024 && image?.HasMipmaps() == true;
        }
        Check("all four ground maps remain 1K with generated mipmaps", valid);
    }

    private void CheckSeams(TerrainGenerator generator, TerrainChunkData left)
    {
        TerrainChunkData right = generator.BuildChunk(left.Coord + Vector2I.Right, _palette, 0);
        Dictionary<float, (Vector3, Color)> border = new();
        for (int i = 0; i < left.Vertices.Length; i++)
        {
            if (Mathf.Abs(left.Vertices[i].X - generator.Settings.ChunkSize) < 0.001f) { border[left.Vertices[i].Z] = (left.Normals[i], left.Colors[i]); }
        }
        bool seamless = true;
        int count = 0;
        for (int i = 0; i < right.Vertices.Length; i++)
        {
            if (Mathf.Abs(right.Vertices[i].X) > 0.001f) { continue; }
            count++;
            var reference = border[right.Vertices[i].Z];
            seamless &= reference.Item1.DistanceTo(right.Normals[i]) < 0.00001f && reference.Item2.IsEqualApprox(right.Colors[i]);
        }
        Check("adjacent chunks share lighting normals and surface colours at every border vertex", seamless && count > 64);
    }

    private void CheckScaleAndAssets(TerrainChunkData data)
    {
        int trees = 0;
        int saplings = 0;
        int grass = 0;
        int stones = 0;
        bool fits = true;
        bool assets = true;
        Dictionary<string, Mesh> loaded = new();
        foreach (PlantData plant in data.Plants)
        {
            Mesh? mesh = ModelFit.FirstMesh(plant.Model, out Transform3D local);
            if (mesh == null) { assets = false; continue; }
            loaded[plant.Model] = mesh;
            PlantData upright = plant with { Lean = Vector2.Zero, Yaw = 0 };
            Transform3D fitted = TerrainChunk.FitPlant(upright, mesh.GetAabb(), local, Vector3.Zero);
            Aabb bounds = fitted * mesh.GetAabb();
            fits &= Mathf.Abs(bounds.Position.Y - plant.Position.Y) < 0.001f;
            if (plant.HeightInMetres) { fits &= Mathf.Abs(bounds.Size.Y - plant.Scale) < 0.001f; }
            if (plant.Kind == PlantKind.Tree)
            {
                trees++;
                bool young = plant.Scale >= 2.5f && plant.Scale <= 5;
                if (young) { saplings++; }
                fits &= (young || (plant.Scale >= 10 && plant.Scale <= 18)) && plant.TrunkRadius >= 0.035f && plant.TrunkRadius < 0.3f;
                fits &= Mathf.Abs(bounds.GetCenter().X - plant.Position.X) < 0.001f;
            }
            else if (plant.Kind == PlantKind.Grass) { grass++; fits &= plant.Scale >= 0.12f && plant.Scale <= 0.4f; }
            else if (plant.Kind == PlantKind.Pebble) { stones++; fits &= plant.Scale >= 0.025f && plant.Scale <= 0.09f; }
        }
        Check("every selected plant and detail model loads", assets && loaded.Count >= 6, loaded.Count + " shared meshes");
        Check("trees, grass and pebbles fit explicit metre ranges and roots stay on the ground", fits && trees > saplings && saplings > 0 && grass > 0 && stones > 0,
            trees + " trees, " + grass + " grass tufts, " + stones + " stones");
        Check("ground detail count is bounded independently of world length", stones <= 4096 && data.Plants.Count < 5000);
    }

    private void CheckGrounding(TerrainGenerator generator, TerrainChunkData data)
    {
        bool grounded = true;
        Vector3 corner = generator.ChunkCorner(data.Coord);
        int points = data.GridPoints;
        float cell = generator.Settings.CellSize;
        foreach (PlantData plant in data.Plants)
        {
            if (plant.Kind == PlantKind.Tree) { continue; }
            Vector3 at = plant.Position - corner;
            int x = Mathf.Clamp((int)(at.X / cell), 0, points - 2);
            int z = Mathf.Clamp((int)(at.Z / cell), 0, points - 2);
            float u = at.X / cell - x;
            float v = at.Z / cell - z;
            float a = data.Heights[x + z * points];
            float b = data.Heights[x + 1 + z * points];
            float c = data.Heights[x + (z + 1) * points];
            float d = data.Heights[x + 1 + (z + 1) * points];
            float y = a + (d - c) * u + (c - a) * v;
            if (u >= v) { y = a + (b - a) * u + (d - b) * v; }
            grounded &= Mathf.Abs(y - plant.Position.Y) < 0.001f;
        }
        Check("ground plants sit on the actual rendered triangles, including diagonal cells", grounded);
    }

    private async Task CheckRenderingNodes(TerrainGenerator generator, TerrainChunkData data)
    {
        TerrainChunk chunk = new();
        chunk.Build(data, generator, new SurfaceType?[5], new StandardMaterial3D(), new StandardMaterial3D(), new StandardMaterial3D(), null, null);
        AddChild(chunk);
        for (int i = 0; i < 180 && chunk.PlantsPending; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        int trees = 0;
        int trunkBodies = 0;
        int smallColliders = 0;
        int treeBatches = 0;
        int detailBatches = 0;
        bool opaque = true;
        foreach (Node child in chunk.GetChildren())
        {
            if (child is StaticBody3D body && body.Name.ToString().StartsWith("Tree", StringComparison.Ordinal))
            {
                trunkBodies++;
                foreach (Node part in body.GetChildren())
                {
                    if (part is CollisionShape3D shape && shape.Shape is CylinderShape3D) { trees++; }
                    else { smallColliders++; }
                }
            }
            if (child is MultiMeshInstance3D batch)
            {
                // Trees fade out at the edge of their range (so they don't pop out of the fog); small detail doesn't.
                if (batch.Name.ToString().StartsWith("Tree", StringComparison.Ordinal)) { treeBatches++; }
                else
                {
                    detailBatches++;
                    opaque &= batch.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off;
                    opaque &= batch.VisibilityRangeFadeMode == GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
                }
            }
        }
        Check("batched trees keep individual trunk shapes in one static compound body", !chunk.PlantsPending && trees > treeBatches && trunkBodies == 1 && treeBatches > 0 && smallColliders == 0,
            trees + " trunk shapes / " + trunkBodies + " static body / " + treeBatches + " tree batches");
        Check("small detail uses spatial batches without extra colliders or shadow passes", detailBatches > 0 && opaque);
        Check("incremental foliage construction completes within its streaming budget", TerrainChunk.MaxPlantFrameMilliseconds < 16,
            TerrainChunk.MaxPlantFrameMilliseconds.ToString("0.0") + " ms worst shared plant frame");
        chunk.QueueFree();
    }
    private static bool Equal<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
    {
        if (a.Count != b.Count) { return false; }
        for (int i = 0; i < a.Count; i++) { if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) { return false; } }
        return true;
    }
    private void Check(string title, bool result, string detail = "")
    {
        if (result) { GD.Print("PASS  " + title + ": " + detail); }
        else { _failed++; GD.Print("FAIL  " + title + ": " + detail); }
    }
}
