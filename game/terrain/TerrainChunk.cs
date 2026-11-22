using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// One streamed piece of the mountain: ground mesh, unchanged HeightMapShape3D, physical obstacles,
/// batched trees with separate solid trunks, and spatial batches of grass, flowers and pebbles.
/// Placed at the chunk's corner. Answers surface queries from the generator, so grip
/// and fall damage vary across the chunk.
/// </summary>
public partial class TerrainChunk : StaticBody3D, ISurfaceQuery, IFogCover
{
    public Vector2I Coord { get; private set; }

    /// <summary>Bodies the carrier can vault over (boulders and fallen trunks).</summary>
    public const string VaultableGroup = "vaultable";
    /// <summary>The standing trees' trunk bodies (one per chunk, a cylinder per tree).</summary>
    public const string TreeGroup = "tree_trunks";
    /// <summary>
    /// Ground cover that looks solid and stands taller than a knee (outcrops, walls, big stones,
    /// stumps and the lost carriers' gear) or a big bush: one body per chunk and material, a box per
    /// piece, so the carrier no longer walks through it (environment/WorldStyle's thresholds).
    /// </summary>
    public const string CoverGroup = "solid_cover";

    private TerrainGenerator? _generator;
    private SurfaceType?[] _surfaces = System.Array.Empty<SurfaceType?>();
    private Material? _grassMaterial;
    private Material? _rockMaterial;
    /// <summary>The scattered rocks' material that blends them into the ground (null: they take _rockMaterial).</summary>
    private Material? _rockBlend;
    private float[] _groundHeights = System.Array.Empty<float>();
    private Color[] _groundColours = System.Array.Empty<Color>();
    private int _gridPoints;
    private Material? _woodMaterial;
    private StaticBody3D? _treeBody;
    private StaticBody3D? _rockCover;
    private StaticBody3D? _woodCover;
    private StaticBody3D? _bushCover;
    private SurfaceType? _rockSurface;
    private Vector2[] _fogTrees = System.Array.Empty<Vector2>();
    private Vector3[] _fogRocks = System.Array.Empty<Vector3>();

    /// <summary>The ground fog lies among this chunk's trees and low rocks.</summary>
    public void GatherFogCover(System.Collections.Generic.List<Vector2> trees, System.Collections.Generic.List<Vector3> rocks)
    {
        trees.AddRange(_fogTrees);
        rocks.AddRange(_fogRocks);
    }

    public override void _EnterTree()
    {
        AddToGroup(FogCover.Group);
        FogCover.Changed();
    }

    /// <summary>Creates the chunk's nodes from generated data. Main thread only.</summary>
    public void Build(TerrainChunkData data, TerrainGenerator generator, SurfaceType?[] surfaces,
        Material groundMaterial, Material boulderMaterial, Material trunkMaterial,
        SurfaceType? boulderSurface, SurfaceType? trunkSurface, Material? grassMaterial = null, Shader? carpetShader = null)
    {
        Coord = data.Coord;
        _generator = generator;
        _fogTrees = data.FogTrees.ToArray();
        _fogRocks = data.FogRocks.ToArray();
        FogCover.Changed();
        _surfaces = surfaces;
        _grassMaterial = grassMaterial;
        _rockMaterial = boulderMaterial;
        _rockBlend = boulderMaterial is StandardMaterial3D textured ? DownhillRoguelite.Environment.WorldStyle.RockBlend(textured) : null;
        _groundHeights = data.Heights;
        _groundColours = data.Colors;
        _gridPoints = data.GridPoints;
        _woodMaterial = trunkMaterial;
        _rockSurface = boulderSurface;
        Name = $"Chunk_{data.Coord.X}_{data.Coord.Y}";
        Position = generator.ChunkCorner(data.Coord);

        using Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals;
        arrays[(int)Mesh.ArrayType.Color] = data.Colors;
        if (data.Wear.Length == data.Vertices.Length && data.Wear.Length > 0) { arrays[(int)Mesh.ArrayType.TexUV] = data.Wear; }
        if (data.Indices.Length > 0) { arrays[(int)Mesh.ArrayType.Index] = data.Indices; }
        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        AddChild(new MeshInstance3D { Name = "Ground", Mesh = mesh, MaterialOverride = groundMaterial });
        float carpetDistance = generator.Settings.CarpetViewDistance * generator.Settings.QualityViewScale[(int)DownhillRoguelite.Core.GameOptions.Quality];
        GrassCarpet.Add(this, data, carpetShader, generator.Settings, carpetDistance);

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
        FogCover.Changed();
        _plantBuild?.Dispose();
        _plantBuild = null;
        _pendingPlants = null;
        if (_treeBody != null && IsInstanceValid(_treeBody) && _treeBody.GetParent() == null) { _treeBody.Free(); }
        foreach (StaticBody3D? cover in new[] { _rockCover, _woodCover, _bushCover })
        {
            if (cover != null && IsInstanceValid(cover) && cover.GetParent() == null) { cover.Free(); }
        }
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
            else if (IsGroundCover(plant.Kind) && ModelFit.FirstMesh(plant.Model, out Transform3D coverLocal) is Mesh coverMesh)
            {
                _plantPhase = "cover collision";
                if (AddCoverCollision(plant, coverMesh.GetAabb(), coverLocal, chunkOrigin, trunkSurface))
                {
                    yield return true;
                }
            }
            float batchSize = Mathf.Max(_generator!.Settings.PlantBatchSize, 4);
            if (plant.Kind == PlantKind.Tree) { batchSize = _generator.Settings.ChunkSize; }
            else if (IsGroundCover(plant.Kind)) { batchSize = Mathf.Max(_generator.Settings.GroundCoverBatchSize, 4); }
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
        foreach (StaticBody3D? cover in new[] { _rockCover, _woodCover, _bushCover })
        {
            if (cover == null) { continue; }
            _plantPhase = "compound cover upload";
            AddChild(cover, true);
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
            _plantPhase = "batch mesh";
            mesh = DrawnMesh(kind, model, mesh);
            _plantPhase = "multimesh allocation";
            // Rocks carry the ground they stand on (custom data) for the material that blends them into it.
            bool blended = kind == PlantKind.Rock && _rockBlend != null && _groundColours.Length == _gridPoints * _gridPoints && _gridPoints > 1;
            int stride = blended ? 16 : 12;
            MultiMesh batch = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = blended, Mesh = mesh, InstanceCount = plants.Count };
            yield return true;
            _plantPhase = "transform buffer";
            Aabb meshBounds = mesh.GetAabb();
            // One buffer for all instances (12 floats each: the basis by rows, then the origin; 4 more
            // of custom data for blended rocks) instead of a call per tuft, which made adding a chunk hitch.
            float[] buffer = new float[plants.Count * stride];
            for (int i = 0; i < plants.Count; i++)
            {
                PlantData plant = source[plants[i]];
                Transform3D t = FitPlant(plant, meshBounds, local, chunkOrigin);
                int o = i * stride;
                if (blended)
                {
                    Vector3 at = plant.Position - chunkOrigin;
                    (float groundY, Color colour) = GroundAt(at.X, at.Z);
                    // How far the ground lies over the model's origin, along the rock's up.
                    float overOrigin = (groundY - t.Origin.Y) * t.Basis.Y.Normalized().Y;
                    buffer[o + 12] = colour.R;
                    buffer[o + 13] = colour.G;
                    buffer[o + 14] = colour.B;
                    buffer[o + 15] = overOrigin;
                }
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
            else if (kind == PlantKind.Rock && blended) { material = _rockBlend; }
            else if (kind == PlantKind.Pebble || kind == PlantKind.Rock) { material = _rockMaterial; }
            else if (kind == PlantKind.Debris && (model.Contains("/log") || model.Contains("/stump"))) { material = _woodMaterial; }

            float view = _generator!.Settings.GrassViewDistance;
            float cell = _generator.Settings.PlantBatchSize;
            GeometryInstance3D.ShadowCastingSetting shadows = GeometryInstance3D.ShadowCastingSetting.Off;
            // Far trees fade out (into the fog) over a margin; small plants just stop.
            float margin = 2.0f;
            GeometryInstance3D.VisibilityRangeFadeModeEnum fade = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
            if (kind == PlantKind.Tree)
            {
                view = _generator.Settings.TreeViewDistance;
                shadows = GeometryInstance3D.ShadowCastingSetting.On;
                margin = _generator.Settings.TreeFadeMargin;
                fade = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            }
            else if (kind == PlantKind.Pebble) { view = _generator.Settings.PebbleViewDistance; }
            else if (kind == PlantKind.Shrub) { view = _generator.Settings.ShrubViewDistance; }
            else if (kind == PlantKind.Rock) { view = _generator.Settings.RockViewDistance; }
            else if (kind == PlantKind.Debris) { view = _generator.Settings.DebrisViewDistance; }
            if (IsGroundCover(kind))
            {
                // Sparse cover is batched in larger cells: the range is measured to the cell's centre.
                cell = _generator.Settings.GroundCoverBatchSize * 0.71f;
            }
            view *= _generator.Settings.QualityViewScale[(int)DownhillRoguelite.Core.GameOptions.Quality];
            AddChild(new MultiMeshInstance3D
            {
                Name = kind + "Batch",
                MaterialOverride = material,
                Multimesh = batch,
                CastShadow = shadows,
                VisibilityRangeEnd = view + cell,
                VisibilityRangeEndMargin = margin,
                VisibilityRangeFadeMode = fade,
            }, true);
            yield return true;
        }
    }

    /// <summary>Shrubs, loose rocks and debris: sparse ground cover, batched in larger cells and drawn further.</summary>
    /// <summary>
    /// The mesh a batch of this kind and model draws (cached, shared by every chunk): trees get the wind
    /// data their branches flutter by (the grass has its own shared material); an art pack's flowers,
    /// mushrooms and lost gear are put in the mountain's palette, while the generated bushes and dwarf
    /// pines keep their own swaying material. Logs and stumps take the trunk material.
    /// </summary>
    public static Mesh DrawnMesh(PlantKind kind, string model, Mesh mesh)
    {
        if (kind == PlantKind.Tree)
        {
            return FoliageMaterials.WindReady(mesh);
        }
        if (kind == PlantKind.Shrub || kind == PlantKind.Flower
            || (kind == PlantKind.Debris && !model.Contains("/log") && !model.Contains("/stump")))
        {
            return DownhillRoguelite.Environment.WorldStyle.Styled(mesh, model);
        }
        return mesh;
    }

    /// <summary>A rock at least this wide (m) is a slab: solid from SlabHeight (he steps up onto it) instead of KneeHeight.</summary>
    public const float WideRock = 1.0f;
    /// <summary>
    /// A slab this proud of the ground (m) is solid: he steps up onto it. Lower ones he steps over with
    /// his feet a little in them, as through a pebble.
    /// </summary>
    public const float SlabHeight = DownhillRoguelite.Environment.WorldStyle.KneeHeight * 0.4f;

    /// <summary>
    /// The chunk's ground at a point (chunk-local x and z): its height and colour, with lying snow
    /// (the colour's alpha says how little snow covers it) turned to snow white as the ground shader does.
    /// </summary>
    private (float Height, Color Colour) GroundAt(float lx, float lz)
    {
        float cell = _generator!.Settings.CellSize;
        float gx = Mathf.Clamp(lx / cell, 0.0f, _gridPoints - 1.001f);
        float gz = Mathf.Clamp(lz / cell, 0.0f, _gridPoints - 1.001f);
        int i = (int)gx;
        int j = (int)gz;
        float fx = gx - i;
        float fz = gz - j;
        int a = i + j * _gridPoints;
        int b = a + _gridPoints;
        float height = Mathf.Lerp(Mathf.Lerp(_groundHeights[a], _groundHeights[a + 1], fx), Mathf.Lerp(_groundHeights[b], _groundHeights[b + 1], fx), fz);
        Color colour = _groundColours[a].Lerp(_groundColours[a + 1], fx).Lerp(_groundColours[b].Lerp(_groundColours[b + 1], fx), fz);
        float lying = Mathf.SmoothStep(0.46f, 0.54f, 1.0f - colour.A);
        colour = colour.Lerp(new Color(0.88f, 0.91f, 0.95f), lying);
        return (height, colour);
    }

    public static bool IsGroundCover(PlantKind kind)
    {
        return kind == PlantKind.Shrub || kind == PlantKind.Rock || kind == PlantKind.Debris;
    }

    /// <summary>
    /// How far a plant's foot is pushed into the ground, in m: rocks sit a fifth of their height
    /// deep and shrubs and debris a little, so on a slope their downhill side doesn't hang in the air.
    /// </summary>
    public static float GroundSink(PlantData plant)
    {
        if (plant.Kind == PlantKind.Rock)
        {
            // On a slope (read back from its lean, TerrainGenerator.AddCover) a rock sinks deeper, so
            // what's left of the slope under it doesn't lift its downhill edge clear of the ground.
            float sink = plant.Scale * 0.2f;
            if (plant.Lean != Vector2.Zero)
            {
                float tilt = (Basis.FromEuler(new Vector3(plant.Lean.X, 0.0f, plant.Lean.Y)) * Vector3.Up).AngleTo(Vector3.Up);
                float gradient = Mathf.Tan(Mathf.Min(tilt / TerrainGenerator.RockSlopeFollow, 1.4f));
                sink += Mathf.Min(plant.Scale * TerrainGenerator.RockSlopeSink * gradient, plant.Scale * TerrainGenerator.RockSlopeSinkMost);
            }
            return sink;
        }
        if (plant.Kind == PlantKind.Shrub || plant.Kind == PlantKind.Debris)
        {
            return plant.Scale * 0.08f;
        }
        return 0.0f;
    }

    /// <summary>
    /// How far a piece of cover <paramref name="height"/> tall stands proud of the ground, straight up
    /// (as his legs meet it): a rock leaning with a steep slope stands taller than it is thick.
    /// </summary>
    public static float ProudHeight(PlantData plant, float height)
    {
        float upright = 1.0f;
        if (plant.Lean != Vector2.Zero)
        {
            upright = Mathf.Max((Basis.FromEuler(new Vector3(plant.Lean.X, 0.0f, plant.Lean.Y)) * Vector3.Up).Y, 0.3f);
        }
        return height / upright - GroundSink(plant);
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
        Transform3D placement = new(lean * new Basis(Vector3.Up, plant.Yaw), plant.Position - chunkOrigin + Vector3.Down * GroundSink(plant));
        Transform3D fit = new(Basis.Identity.Scaled(Vector3.One * scale), -foot * scale);
        return placement * fit * local;
    }

    /// <summary>
    /// Whether a piece of ground cover blocks the carrier: rocks, walls, stumps and gear standing
    /// taller than a knee, and bushes as tall as a big bush. Grass, flowers, pebbles and small
    /// shrubs never do.
    /// </summary>
    public static bool BlocksCarrier(PlantKind kind, float visibleHeight, float width = 0.0f)
    {
        return kind switch
        {
            // A wide slab is solid from a little lower: he steps up onto it or is stopped by it, where
            // before a 6 m slab under knee height let him wade through it to the waist.
            PlantKind.Rock or PlantKind.Debris => visibleHeight >= DownhillRoguelite.Environment.WorldStyle.KneeHeight
                || (width >= WideRock && visibleHeight >= SlabHeight),
            PlantKind.Shrub => visibleHeight >= DownhillRoguelite.Environment.WorldStyle.BigBushHeight,
            _ => false,
        };
    }

    /// <summary>A box round a solid piece of cover (in the chunk's body for its material); false when it doesn't block.</summary>
    private bool AddCoverCollision(PlantData plant, Aabb meshBounds, Transform3D local, Vector3 chunkOrigin, SurfaceType? trunkSurface)
    {
        Aabb bounds = local * meshBounds;
        float scale = plant.Scale;
        Vector3 foot = Vector3.Zero;
        if (plant.HeightInMetres)
        {
            scale = plant.Scale / Mathf.Max(bounds.Size.Y, 0.01f);
            foot = new Vector3(bounds.GetCenter().X, bounds.Position.Y, bounds.GetCenter().Z);
        }
        Vector3 size = bounds.Size * scale;
        float visible = ProudHeight(plant, size.Y);
        if (!BlocksCarrier(plant.Kind, visible, Mathf.Max(size.X, size.Z)))
        {
            return false;
        }
        Basis turn = Basis.FromEuler(new Vector3(plant.Lean.X, 0.0f, plant.Lean.Y)) * new Basis(Vector3.Up, plant.Yaw);
        Vector3 origin = plant.Position - chunkOrigin + Vector3.Down * GroundSink(plant);
        Vector3 centre = origin + turn * ((bounds.GetCenter() - foot) * scale);
        // A bush stops the carrier at its dense middle; rocks and wood at most of their outline.
        float footprint = 0.85f;
        if (plant.Kind != PlantKind.Shrub)
        {
            // A wide slab's outline is left at most 0.1 m outside the box (leaning with a slope, a wider
            // margin along its foot let him walk a metre into it, crossing the sliver at a slant).
            footprint = Mathf.Max(footprint, 1.0f - 0.2f / Mathf.Max(Mathf.Min(size.X, size.Z), 0.2f));
        }
        StaticBody3D body;
        if (plant.Kind == PlantKind.Shrub)
        {
            footprint = 0.55f;
            body = _bushCover ??= NewCover("CoverBushes", false, null);
        }
        else if (plant.Kind == PlantKind.Rock)
        {
            body = _rockCover ??= NewCover("CoverRocks", true, _rockSurface);
        }
        else
        {
            body = _woodCover ??= NewCover("CoverWood", true, trunkSurface);
        }
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(Mathf.Max(size.X * footprint, 0.1f), size.Y, Mathf.Max(size.Z * footprint, 0.1f)) },
            Transform = new Transform3D(turn, centre),
        });
        return true;
    }

    private static StaticBody3D NewCover(string name, bool vaultable, SurfaceType? surface)
    {
        StaticBody3D body = new() { Name = name };
        body.AddToGroup(CoverGroup);
        if (vaultable) { body.AddToGroup(VaultableGroup); }
        if (surface != null) { body.SetMeta(SurfaceType.MetaKey, surface); }
        return body;
    }

    private void AddTreeCollision(PlantData plant, SurfaceType? trunkSurface)
    {
        Basis lean = Basis.FromEuler(new Vector3(plant.Lean.X, 0, plant.Lean.Y));
        if (_treeBody == null)
        {
            _treeBody = new StaticBody3D { Name = "TreeTrunks" };
            _treeBody.AddToGroup(TreeGroup);
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
            if (!AddModel(body, _generator!.Settings.TrunkModels, obstacle, new Vector3(radius * 2.0f, obstacle.Size.Y, radius * 2.0f), true, trunkMaterial))
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
