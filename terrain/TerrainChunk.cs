using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// One streamed piece of the mountain: ground mesh, unchanged HeightMapShape3D, physical obstacles,
/// batched trees with separate solid trunks, and spatial batches of grass, flowers and pebbles.
/// Placed at the chunk's corner. Answers surface queries from the generator, so grip
/// and fall damage vary across the chunk.
/// </summary>
public partial class TerrainChunk : StaticBody3D, ISurfaceQuery
{
    public Vector2I Coord { get; private set; }

    /// <summary>Bodies the carrier can vault over (boulders and fallen trunks).</summary>
    public const string VaultableGroup = "vaultable";

    private TerrainGenerator? _generator;
    private SurfaceType?[] _surfaces = System.Array.Empty<SurfaceType?>();
    private Material? _grassMaterial;
    private Material? _rockMaterial;
    private StaticBody3D? _treeBody;

    /// <summary>Creates the chunk's nodes from generated data. Main thread only.</summary>
    public void Build(TerrainChunkData data, TerrainGenerator generator, SurfaceType?[] surfaces,
        Material groundMaterial, Material boulderMaterial, Material trunkMaterial,
        SurfaceType? boulderSurface, SurfaceType? trunkSurface, Material? grassMaterial = null)
    {
        Coord = data.Coord;
        _generator = generator;
        _surfaces = surfaces;
        _grassMaterial = grassMaterial;
        _rockMaterial = boulderMaterial;
        Name = $"Chunk_{data.Coord.X}_{data.Coord.Y}";
        Position = generator.ChunkCorner(data.Coord);

        using Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals;
        arrays[(int)Mesh.ArrayType.Color] = data.Colors;
        if (data.Indices.Length > 0) { arrays[(int)Mesh.ArrayType.Index] = data.Indices; }
        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        AddChild(new MeshInstance3D { Name = "Ground", Mesh = mesh, MaterialOverride = groundMaterial });

        // HeightMapShape3D is centred on its node, one unit between points: centre it and scale by cell size.
        float size = generator.Settings.ChunkSize;
        float cell = generator.Settings.CellSize;
        HeightMapShape3D shape = new() { MapWidth = data.GridPoints, MapDepth = data.GridPoints, MapData = data.Heights };
        AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = shape,
            Position = new Vector3(size * 0.5f, 0.0f, size * 0.5f),
            Scale = new Vector3(cell, 1.0f, cell),
        });

        foreach (ObstacleData obstacle in data.Obstacles)
        {
            AddObstacle(obstacle, boulderMaterial, trunkMaterial, boulderSurface, trunkSurface);
        }
        // Plants follow on the next frame, so the ground and the plants never land in the same frame.
        // Upload has copied the render/collision arrays. Foliage only needs the plant plan;
        // do not promote all of the source mesh buffers while incremental work is pending.
        _pendingPlants = data.Plants;
        _pendingTrunkSurface = trunkSurface;
        SetProcess(true);
    }

    private System.Collections.Generic.IReadOnlyList<PlantData>? _pendingPlants;
    private SurfaceType? _pendingTrunkSurface;
    private System.Collections.Generic.IEnumerator<bool>? _plantBuild;
    private static ulong _plantFrame;
    private static double _plantFrameSpent;
    private string _plantPhase = "";
    private static bool? _profilePlants;
    public static double MaxPlantFrameMilliseconds { get; private set; }

    public override void _Ready()
    {
        _profilePlants ??= System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--profile-plants") >= 0;
        if (_pendingPlants == null)
        {
            SetProcess(false);
        }
    }

    public override void _Process(double delta)
    {
        if (_pendingPlants != null)
        {
            ulong frame = Engine.GetProcessFrames();
            if (frame != _plantFrame) { _plantFrame = frame; _plantFrameSpent = 0; }
            double budget = _generator!.Settings.PlantBuildBudgetMilliseconds;
            if (TerrainStreamer.Loading)
            {
                budget = _generator.Settings.LoadingPlantBudgetMilliseconds;
            }
            budget = Mathf.Max(budget, 0.5);
            if (_plantFrameSpent >= budget) { return; }
            ulong start = Time.GetTicksUsec();
            bool profile = _profilePlants == true;
            int collections = 0;
            int middleCollections = 0;
            int minorCollections = 0;
            long allocated = 0;
            double pause = 0;
            double worstSlice = 0;
            string worstPhase = "";
            if (profile)
            {
                collections = System.GC.CollectionCount(2);
                middleCollections = System.GC.CollectionCount(1);
                minorCollections = System.GC.CollectionCount(0);
                allocated = System.GC.GetAllocatedBytesForCurrentThread();
                pause = System.GC.GetTotalPauseDuration().TotalMilliseconds;
            }
            _plantBuild ??= AddPlants(_pendingPlants, _pendingTrunkSurface).GetEnumerator();
            bool more = true;
            while (more)
            {
                ulong sliceStart = 0;
                if (profile) { sliceStart = Time.GetTicksUsec(); }
                more = _plantBuild.MoveNext();
                ulong now = Time.GetTicksUsec();
                if (profile && (now - sliceStart) / 1000.0 > worstSlice)
                { worstSlice = (now - sliceStart) / 1000.0; worstPhase = _plantPhase; }
                if (_plantFrameSpent + (now - start) / 1000.0 >= budget) { break; }
            }
            if (!more)
            {
                _plantBuild.Dispose();
                _plantBuild = null;
                _pendingPlants = null;
            }
            double spent = (Time.GetTicksUsec() - start) / 1000.0;
            if (spent > budget * 2 && profile)
            {
                GD.Print("Plant streaming step " + Coord + " " + _plantPhase + ": " + spent.ToString("0.00")
                    + " ms; largest slice " + worstPhase + " " + worstSlice.ToString("0.00")
                    + " ms; managed allocation " + (System.GC.GetAllocatedBytesForCurrentThread() - allocated)
                    + " B; GC counters 0/1/2 " + (System.GC.CollectionCount(0) - minorCollections) + "/"
                    + (System.GC.CollectionCount(1) - middleCollections) + "/" + (System.GC.CollectionCount(2) - collections)
                    + "; GC pause delta " + (System.GC.GetTotalPauseDuration().TotalMilliseconds - pause).ToString("0.00") + " ms");
            }
            MaxPlantMilliseconds = Mathf.Max(MaxPlantMilliseconds, spent);
            _plantFrameSpent += spent;
            MaxPlantFrameMilliseconds = Mathf.Max(MaxPlantFrameMilliseconds, _plantFrameSpent);
        }
        SetProcess(_pendingPlants != null);
    }

    public override void _ExitTree()
    {
        _plantBuild?.Dispose();
        _plantBuild = null;
        _pendingPlants = null;
        if (_treeBody != null && IsInstanceValid(_treeBody) && _treeBody.GetParent() == null) { _treeBody.Free(); }
    }

    /// <summary>The slowest frame spent adding one chunk's plants, in ms (for the streaming test).</summary>
    public static double MaxPlantMilliseconds { get; set; }

    /// <summary>True until the chunk's plants finish incremental construction after the ground.</summary>
    public bool PlantsPending => _pendingPlants != null;

    /// <summary>Trees as bodies the carrier bumps into; grass and flowers batched per model.</summary>
    private System.Collections.Generic.IEnumerable<bool> AddPlants(System.Collections.Generic.IReadOnlyList<PlantData> source, SurfaceType? trunkSurface)
    {
        if (source.Count == 0)
        {
            yield break;
        }
        // Keep indices instead of copying the sizeable PlantData struct into every growing batch.
        // The source stays alive until this iterator completes, so transforms retain their order.
        System.Collections.Generic.Dictionary<(string, PlantKind, int, int), System.Collections.Generic.List<int>> batches = new();
        Vector3 chunkOrigin = Position;
        int grouped = 0;
        int quantum = Mathf.Max(_generator!.Settings.PlantWorkItemsPerSlice, 1);
        for (int plantIndex = 0; plantIndex < source.Count; plantIndex++)
        {
            PlantData plant = source[plantIndex];
            if (plant.Kind == PlantKind.Tree)
            {
                if (ModelFit.FirstMesh(plant.Model, out _) == null) { continue; }
                _plantPhase = "trunk collision";
                AddTreeCollision(plant, trunkSurface);
                yield return true;
            }
            float batchSize = Mathf.Max(_generator!.Settings.PlantBatchSize, 4);
            if (plant.Kind == PlantKind.Tree) { batchSize = _generator.Settings.ChunkSize; }
            _plantPhase = "grouping detail";
            Vector3 at = plant.Position - chunkOrigin;
            var key = (plant.Model, plant.Kind, Mathf.FloorToInt(at.X / batchSize), Mathf.FloorToInt(at.Z / batchSize));
            if (!batches.TryGetValue(key, out System.Collections.Generic.List<int>? list))
            {
                list = new System.Collections.Generic.List<int>();
                batches[key] = list;
            }
            list.Add(plantIndex);
            grouped++;
            if (grouped % quantum == 0) { yield return true; }
        }
        if (_treeBody != null)
        {
            _plantPhase = "compound trunk upload";
            AddChild(_treeBody, true);
            yield return true;
        }
        _plantPhase = "grass material setup";
        Material? grassMaterial = _grassMaterial;
        if (grassMaterial == null)
        {
            Vector3 middle = chunkOrigin + new Vector3(_generator!.Settings.ChunkSize * 0.5f, 0, _generator.Settings.ChunkSize * 0.5f);
            Color tint = _generator.BiomeTint(_generator.DepthAt(_generator.Height(middle.X, middle.Z)));
            Color blade = _generator.Settings.GrassColor;
            grassMaterial = new StandardMaterial3D
            {
                AlbedoColor = new Color(blade.R * tint.R, blade.G * tint.G, blade.B * tint.B),
                Roughness = 1,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                VertexColorUseAsAlbedo = true,
            };
        }
        yield return true;
        foreach (var entry in batches)
        {
            string model = entry.Key.Item1;
            PlantKind kind = entry.Key.Item2;
            System.Collections.Generic.List<int> plants = entry.Value;
            Mesh? mesh = ModelFit.FirstMesh(model, out Transform3D local);
            if (mesh == null)
            {
                continue;
            }
            _plantPhase = "multimesh allocation";
            MultiMesh batch = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = plants.Count };
            yield return true;
            _plantPhase = "transform buffer";
            Aabb meshBounds = mesh.GetAabb();
            // One buffer for all instances (12 floats each: the basis by rows, then the origin) instead
            // of a call per tuft, which made adding a chunk hitch.
            float[] buffer = new float[plants.Count * 12];
            for (int i = 0; i < plants.Count; i++)
            {
                PlantData plant = source[plants[i]];
                Transform3D t = FitPlant(plant, meshBounds, local, chunkOrigin);
                int o = i * 12;
                buffer[o] = t.Basis.X.X;
                buffer[o + 1] = t.Basis.Y.X;
                buffer[o + 2] = t.Basis.Z.X;
                buffer[o + 3] = t.Origin.X;
                buffer[o + 4] = t.Basis.X.Y;
                buffer[o + 5] = t.Basis.Y.Y;
                buffer[o + 6] = t.Basis.Z.Y;
                buffer[o + 7] = t.Origin.Y;
                buffer[o + 8] = t.Basis.X.Z;
                buffer[o + 9] = t.Basis.Y.Z;
                buffer[o + 10] = t.Basis.Z.Z;
                buffer[o + 11] = t.Origin.Z;
                if ((i + 1) % quantum == 0) { yield return true; }
            }
            yield return true;
            _plantPhase = "multimesh upload";
            batch.Buffer = buffer;
            yield return true;
            _plantPhase = "scene insertion";
            Material? material = null;
            if (kind == PlantKind.Grass)
            {
                material = grassMaterial;
            }
            else if (kind == PlantKind.Pebble) { material = _rockMaterial; }
            float view = _generator!.Settings.GrassViewDistance;
            GeometryInstance3D.ShadowCastingSetting shadows = GeometryInstance3D.ShadowCastingSetting.Off;
            if (kind == PlantKind.Tree) { view = _generator.Settings.TreeViewDistance; shadows = GeometryInstance3D.ShadowCastingSetting.On; }
            else if (kind == PlantKind.Pebble) { view = _generator.Settings.PebbleViewDistance; }
            AddChild(new MultiMeshInstance3D
            {
                Name = kind + "Batch",
                MaterialOverride = material,
                Multimesh = batch,
                CastShadow = shadows,
                VisibilityRangeEnd = view + _generator.Settings.PlantBatchSize,
                VisibilityRangeEndMargin = 2.0f,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled,
            }, true);
            yield return true;
        }
    }

    /// <summary>Fits authored plants in metres; legacy ground plants retain their original scale.</summary>
    public static Transform3D FitPlant(PlantData plant, Aabb meshBounds, Transform3D local, Vector3 chunkOrigin)
    {
        float scale = plant.Scale;
        Vector3 foot = Vector3.Zero;
        if (plant.HeightInMetres || plant.Kind == PlantKind.Tree)
        {
            Aabb bounds = local * meshBounds;
            scale = plant.Scale / Mathf.Max(bounds.Size.Y, 0.01f);
            foot = new Vector3(bounds.GetCenter().X, bounds.Position.Y, bounds.GetCenter().Z);
        }
        Basis lean = Basis.FromEuler(new Vector3(plant.Lean.X, 0.0f, plant.Lean.Y));
        Transform3D placement = new(lean * new Basis(Vector3.Up, plant.Yaw), plant.Position - chunkOrigin);
        Transform3D fit = new(Basis.Identity.Scaled(Vector3.One * scale), -foot * scale);
        return placement * fit * local;
    }

    private void AddTreeCollision(PlantData plant, SurfaceType? trunkSurface)
    {
        Basis lean = Basis.FromEuler(new Vector3(plant.Lean.X, 0, plant.Lean.Y));
        if (_treeBody == null)
        {
            _treeBody = new StaticBody3D { Name = "TreeTrunks" };
            if (trunkSurface != null) { _treeBody.SetMeta(SurfaceType.MetaKey, trunkSurface); }
        }
        float radius = Mathf.Clamp(plant.Scale * 0.035f, 0.12f, 0.4f);
        if (plant.TrunkRadius > 0) { radius = plant.TrunkRadius; }
        float trunk = Mathf.Min(plant.Scale, 3.0f);
        Transform3D placement = new(lean * new Basis(Vector3.Up, plant.Yaw), plant.Position - Position);
        _treeBody.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = radius, Height = trunk },
            Transform = placement * new Transform3D(Basis.Identity, Vector3.Up * trunk * 0.5f) });
    }

    public SurfaceType? SurfaceAt(Vector3 worldPoint)
    {
        if (_generator == null)
        {
            return null;
        }
        int index = (int)_generator.SurfaceAt(worldPoint.X, worldPoint.Z);
        if (index < 0 || index >= _surfaces.Length)
        {
            return null;
        }
        return _surfaces[index];
    }

    private void AddObstacle(ObstacleData obstacle, Material boulderMaterial, Material trunkMaterial,
        SurfaceType? boulderSurface, SurfaceType? trunkSurface)
    {
        StaticBody3D body = new()
        {
            Name = obstacle.Kind.ToString(),
            Transform = new Transform3D(obstacle.Rotation, obstacle.Position - Position),
        };

        // Boulders and fallen trunks can be vaulted over (the Jump Over timing event).
        body.AddToGroup(VaultableGroup);
        if (obstacle.Kind == ObstacleKind.Boulder)
        {
            if (!AddModel(body, _generator!.Settings.BoulderModels, obstacle, obstacle.Size, false, boulderMaterial))
            {
                body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = obstacle.Size }, MaterialOverride = boulderMaterial });
            }
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = obstacle.Size } });
            if (boulderSurface != null)
            {
                body.SetMeta(SurfaceType.MetaKey, boulderSurface);
            }
        }
        else
        {
            float radius = obstacle.Size.X * 0.5f;
            if (!AddModel(body, _generator!.Settings.TrunkModels, obstacle, new Vector3(radius * 2.0f, obstacle.Size.Y, radius * 2.0f), true))
            {
                body.AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = obstacle.Size.Y, RadialSegments = 6, Rings = 1 },
                    MaterialOverride = trunkMaterial,
                });
            }
            body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = radius, Height = obstacle.Size.Y } });
            if (trunkSurface != null)
            {
                body.SetMeta(SurfaceType.MetaKey, trunkSurface);
            }
        }
        AddChild(body);
    }

    /// <summary>
    /// Draws an obstacle with one of the art-pack models (picked by its position, so it never changes),
    /// stretched to its collision shape; a log's long side is turned to the trunk's axis (Y).
    /// Returns false when no model is available.
    /// </summary>
    private static bool AddModel(StaticBody3D body, string[] paths, ObstacleData obstacle, Vector3 size, bool alongY, Material? material = null)
    {
        if (paths.Length == 0)
        {
            return false;
        }
        int pick = Mathf.Abs((int)(obstacle.Position.X * 73.0f) ^ (int)(obstacle.Position.Z * 37.0f)) % paths.Length;
        Mesh? mesh = ModelFit.FirstMesh(paths[pick], out Transform3D local);
        if (mesh == null)
        {
            return false;
        }
        Aabb bounds = local * mesh.GetAabb();
        Transform3D turn = Transform3D.Identity;
        if (alongY)
        {
            // Put the model's longest side along Y, where the trunk's cylinder runs.
            Vector3 extent = bounds.Size;
            if (extent.X >= extent.Y && extent.X >= extent.Z)
            {
                turn = new Transform3D(new Basis(Vector3.Back, Mathf.Pi * 0.5f), Vector3.Zero);
            }
            else if (extent.Z >= extent.Y)
            {
                turn = new Transform3D(new Basis(Vector3.Right, Mathf.Pi * 0.5f), Vector3.Zero);
            }
            bounds = turn * bounds;
        }
        body.AddChild(new MeshInstance3D { Mesh = mesh, Transform = ModelFit.FitCentred(bounds, size) * turn * local, MaterialOverride = material });
        return true;
    }
}
