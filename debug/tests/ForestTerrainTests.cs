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
        bool vertices = SameTriangles(chunk, legacy);
        bool obstacles = Equal(chunk.Obstacles, legacy.Obstacles);
        Check("smooth shading preserves triangle corners, winding, collision heights and physical obstacles", heights && vertices && obstacles);
        long sharedBytes = GeometryBytes(chunk);
        long legacyBytes = GeometryBytes(legacy);
        Check("shared smooth geometry preserves triangle count with less than half the vertex-attribute storage",
            chunk.Indices.Length == legacy.Vertices.Length && chunk.Indices.Length % 3 == 0
            && chunk.Vertices.Length == chunk.GridPoints * chunk.GridPoints && sharedBytes < legacyBytes / 2,
            sharedBytes + " indexed bytes / " + legacyBytes + " legacy bytes for " + chunk.Indices.Length / 3 + " triangles");
        CheckSeams(smooth, chunk);
        bool finite = true;
        foreach (Vector3 n in chunk.Normals) { finite &= n.IsFinite() && Mathf.Abs(n.Length() - 1) < 0.001f && n.Y > 0; }
        Check("smooth normals remain finite, unit length and upward-facing", finite);
        TerrainChunkData again = smooth.BuildChunk(chunk.Coord, _palette, 0);
        Check("forest and detail placements are deterministic", Equal(chunk.Plants, again.Plants));
        Check("smooth chunks reuse the same read-only triangle topology",
            ReferenceEquals(chunk.Indices, again.Indices) && ReferenceEquals(chunk.Indices,
                smooth.BuildChunk(chunk.Coord + Vector2I.Right, _palette, 0).Indices));
        CheckTopologyResize();
        CheckScaleAndAssets(chunk);
        CheckTextures();
        CheckGrounding(smooth, chunk);
        await CheckRenderingNodes(smooth, chunk);
        await CheckCancellation(smooth, chunk);
    }

    private void CheckTopologyResize()
    {
        TerrainSettings settings = new() { ChunkCells = 4, Biomes = Array.Empty<Biome>(), PebbleDensity = 0 };
        TerrainGenerator generator = new(settings, 1);
        TerrainChunkData first = generator.BuildChunk(Vector2I.Zero, _palette, 0);
        int[] snapshot = (int[])first.Indices.Clone();
        settings.ChunkCells = 8;
        TerrainChunkData resized = generator.BuildChunk(Vector2I.Zero, _palette, 0);
        Check("resizing a grid builds valid topology without changing earlier chunks",
            first.Indices.Length == 4 * 4 * 6 && resized.Indices.Length == 8 * 8 * 6
            && !ReferenceEquals(first.Indices, resized.Indices) && Equal(first.Indices, snapshot)
            && Array.TrueForAll(resized.Indices, index => index >= 0 && index < resized.Vertices.Length));
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
        int quantum = generator.Settings.PlantWorkItemsPerSlice;
        generator.Settings.PlantWorkItemsPerSlice = 1;
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
                if (batch.Name.ToString().StartsWith("Tree", StringComparison.Ordinal)) { treeBatches++; }
                else { detailBatches++; opaque &= batch.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off; }
                opaque &= batch.VisibilityRangeFadeMode == GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
            }
        }
        Check("batched trees keep individual trunk shapes in one static compound body", !chunk.PlantsPending && trees > treeBatches && trunkBodies == 1 && treeBatches > 0 && smallColliders == 0,
            trees + " trunk shapes / " + trunkBodies + " static body / " + treeBatches + " tree batches");
        Check("small detail uses spatial batches without extra colliders or shadow passes", detailBatches > 0 && opaque);
        CheckInstances(generator, data, chunk);
        Check("incremental foliage construction completes within its streaming budget", TerrainChunk.MaxPlantFrameMilliseconds < 16,
            TerrainChunk.MaxPlantFrameMilliseconds.ToString("0.0") + " ms worst shared plant frame");
        generator.Settings.PlantWorkItemsPerSlice = quantum;
        chunk.QueueFree();
    }

    private void CheckInstances(TerrainGenerator generator, TerrainChunkData data, TerrainChunk chunk)
    {
        // Compare the rendered instances to the generated plan as a multiset; this deliberately
        // does not reconstruct the renderer's spatial grouping or its per-batch index lists.
        Dictionary<(ulong, Vector3I), List<Transform3D>> expected = new();
        Vector3 origin = generator.ChunkCorner(data.Coord);
        int planned = 0;
        foreach (PlantData plant in data.Plants)
        {
            Mesh? mesh = ModelFit.FirstMesh(plant.Model, out Transform3D local);
            if (mesh == null) { continue; }
            Transform3D transform = TerrainChunk.FitPlant(plant, mesh.GetAabb(), local, origin);
            var key = (mesh.GetInstanceId(), PositionKey(transform.Origin));
            if (!expected.TryGetValue(key, out List<Transform3D>? atPosition))
            { atPosition = new List<Transform3D>(); expected.Add(key, atPosition); }
            atPosition.Add(transform);
            planned++;
        }
        int rendered = 0;
        int missingKeys = 0;
        int mismatchedMatrices = 0;
        int invalidBuffers = 0;
        List<string> examples = new();
        bool headless = DisplayServer.GetName() == "headless";
        string readback = "native GetInstanceTransform";
        if (headless) { readback = "headless packed-buffer readback"; }
        bool matching = true;
        foreach (Node child in chunk.GetChildren())
        {
            if (child is not MultiMeshInstance3D visual || visual.Multimesh?.Mesh == null) { continue; }
            MultiMesh batch = visual.Multimesh;
            rendered += batch.InstanceCount;
            float[] stored = Array.Empty<float>();
            if (headless)
            {
                // The dummy renderer returns identity from GetInstanceTransform, but preserves
                // the uploaded packed buffer. Read it once, validating the actual payload size.
                stored = batch.Buffer;
                if (stored.Length != batch.InstanceCount * 12)
                {
                    matching = false;
                    invalidBuffers++;
                    if (examples.Count < 3) { examples.Add(visual.Name + " buffer has " + stored.Length
                        + " floats for " + batch.InstanceCount + " instances"); }
                    continue;
                }
            }
            for (int i = 0; i < batch.InstanceCount; i++)
            {
                Transform3D transform;
                if (headless) { transform = PackedTransform(stored, i); }
                else { transform = batch.GetInstanceTransform(i); }
                var key = (batch.Mesh.GetInstanceId(), PositionKey(transform.Origin));
                if (!expected.TryGetValue(key, out List<Transform3D>? candidates))
                {
                    matching = false;
                    missingKeys++;
                    if (examples.Count < 3)
                    {
                        Transform3D nearest = Transform3D.Identity;
                        float distance = float.PositiveInfinity;
                        foreach (var entry in expected)
                        {
                            if (entry.Key.Item1 != key.Item1) { continue; }
                            foreach (Transform3D candidate in entry.Value)
                            {
                                float gap = transform.Origin.DistanceSquaredTo(candidate.Origin);
                                if (gap < distance) { distance = gap; nearest = candidate; }
                            }
                        }
                        examples.Add(visual.Name + "[" + i + "] missing position key " + key.Item2
                            + "; actual " + transform + "; nearest expected " + nearest
                            + "; origin gap " + Mathf.Sqrt(distance));
                    }
                    continue;
                }
                int found = candidates.FindIndex(candidate => candidate.IsEqualApprox(transform));
                if (found < 0)
                {
                    matching = false;
                    mismatchedMatrices++;
                    if (examples.Count < 3)
                    {
                        string expectedSample = "no unused candidate";
                        if (candidates.Count > 0) { expectedSample = candidates[0].ToString(); }
                        examples.Add(visual.Name + "[" + i + "] actual " + transform + "; expected " + expectedSample);
                    }
                }
                else { candidates.RemoveAt(found); }
            }
        }
        int unmatched = 0;
        foreach (List<Transform3D> remaining in expected.Values) { matching &= remaining.Count == 0; unmatched += remaining.Count; }
        Check("sliced batches render every valid generated plant exactly once", planned > 0 && rendered == planned,
            rendered + " instances / " + planned + " generated plants");
        Check("sliced transform buffers preserve every generated plant's position, rotation and scale", matching,
            "renderer " + DisplayServer.GetName() + ", source " + readback + ", invalid buffers " + invalidBuffers
            + ", missing keys " + missingKeys + ", matrix mismatches " + mismatchedMatrices
            + ", unmatched expected " + unmatched + ". " + string.Join(" | ", examples));
    }

    private async Task CheckCancellation(TerrainGenerator generator, TerrainChunkData data)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        PlantData tree = data.Plants.Find(plant => plant.Kind == PlantKind.Tree);
        TerrainChunkData pending = new(data.Coord, data.Generation, data.GridPoints)
        { Vertices = data.Vertices, Normals = data.Normals, Colors = data.Colors, Indices = data.Indices };
        Array.Copy(data.Heights, pending.Heights, data.Heights.Length);
        for (int i = 0; i < 512; i++) { pending.Plants.Add(tree); }
        double originalBudget = generator.Settings.PlantBuildBudgetMilliseconds;
        generator.Settings.PlantBuildBudgetMilliseconds = 0.5;
        double orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        TerrainChunk cancelled = new();
        cancelled.Build(pending, generator, new SurfaceType?[5], new StandardMaterial3D(), new StandardMaterial3D(), new StandardMaterial3D(), null, null);
        AddChild(cancelled);
        cancelled._Process(0);
        bool interrupted = cancelled.PlantsPending;
        bool detached = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) > orphans;
        cancelled.Free();
        generator.Settings.PlantBuildBudgetMilliseconds = originalBudget;
        Check("unloading an unfinished plant build releases detached trunk nodes", interrupted && detached
            && Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) == orphans,
            "pending " + interrupted + ", detached trunk prepared " + detached + ", orphan delta " + (Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) - orphans));
    }

    private static Vector3I PositionKey(Vector3 position) => new(Mathf.RoundToInt(position.X * 1000),
        Mathf.RoundToInt(position.Y * 1000), Mathf.RoundToInt(position.Z * 1000));

    private static Transform3D PackedTransform(float[] buffer, int index)
    {
        // Godot's 3D instance format is three rows of four floats; Basis takes its columns.
        int offset = index * 12;
        Basis basis = new(new Vector3(buffer[offset], buffer[offset + 4], buffer[offset + 8]),
            new Vector3(buffer[offset + 1], buffer[offset + 5], buffer[offset + 9]),
            new Vector3(buffer[offset + 2], buffer[offset + 6], buffer[offset + 10]));
        return new Transform3D(basis, new Vector3(buffer[offset + 3], buffer[offset + 7], buffer[offset + 11]));
    }

    private static bool SameTriangles(TerrainChunkData shared, TerrainChunkData legacy)
    {
        if (shared.Indices.Length != legacy.Vertices.Length || shared.Indices.Length % 3 != 0) { return false; }
        for (int i = 0; i < shared.Indices.Length; i++)
        {
            int index = shared.Indices[i];
            if (index < 0 || index >= shared.Vertices.Length || shared.Vertices[index] != legacy.Vertices[i]) { return false; }
        }
        return true;
    }

    private static long GeometryBytes(TerrainChunkData data) => (long)data.Vertices.Length * 12
        + (long)data.Normals.Length * 12 + (long)data.Colors.Length * 16 + (long)data.Indices.Length * 4;
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
