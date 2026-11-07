using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// One streamed piece of the mountain: a flat-shaded mesh, a HeightMapShape3D collider, its
/// obstacles and its plants (trees with solid trunks; grass and flowers drawn in batches, one
/// MultiMesh per model, only near the camera). Placed at the chunk's corner. Answers surface queries from the generator, so grip
/// and fall damage vary across the chunk.
/// </summary>
public partial class TerrainChunk : StaticBody3D, ISurfaceQuery
{
    public Vector2I Coord { get; private set; }

    /// <summary>Bodies the carrier can vault over (boulders and fallen trunks).</summary>
    public const string VaultableGroup = "vaultable";

    private TerrainGenerator? _generator;
    private SurfaceType?[] _surfaces = System.Array.Empty<SurfaceType?>();

    /// <summary>Creates the chunk's nodes from generated data. Main thread only.</summary>
    public void Build(TerrainChunkData data, TerrainGenerator generator, SurfaceType?[] surfaces,
        Material groundMaterial, Material boulderMaterial, Material trunkMaterial,
        SurfaceType? boulderSurface, SurfaceType? trunkSurface)
    {
        Coord = data.Coord;
        _generator = generator;
        _surfaces = surfaces;
        Name = $"Chunk_{data.Coord.X}_{data.Coord.Y}";
        Position = generator.ChunkCorner(data.Coord);

        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals;
        arrays[(int)Mesh.ArrayType.Color] = data.Colors;
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
        _pendingPlants = data;
        _pendingTrunkSurface = trunkSurface;
        SetProcess(true);
    }

    private TerrainChunkData? _pendingPlants;
    private SurfaceType? _pendingTrunkSurface;

    public override void _Ready()
    {
        if (_pendingPlants == null)
        {
            SetProcess(false);
        }
    }

    public override void _Process(double delta)
    {
        if (_pendingPlants != null)
        {
            TerrainChunkData data = _pendingPlants;
            _pendingPlants = null;
            ulong start = Time.GetTicksUsec();
            AddPlants(data, _pendingTrunkSurface);
            MaxPlantMilliseconds = Mathf.Max(MaxPlantMilliseconds, (Time.GetTicksUsec() - start) / 1000.0);
        }
        SetProcess(false);
    }

    /// <summary>The slowest frame spent adding one chunk's plants, in ms (for the streaming test).</summary>
    public static double MaxPlantMilliseconds { get; set; }

    /// <summary>True until the chunk's plants have been added (the frame after its ground).</summary>
    public bool PlantsPending => _pendingPlants != null;

    /// <summary>Trees as bodies the carrier bumps into; grass and flowers batched per model.</summary>
    private void AddPlants(TerrainChunkData data, SurfaceType? trunkSurface)
    {
        if (data.Plants.Count == 0)
        {
            return;
        }
        System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<PlantData>> small = new();
        foreach (PlantData plant in data.Plants)
        {
            if (plant.Kind == PlantKind.Tree)
            {
                AddTree(plant, trunkSurface);
                continue;
            }
            if (!small.TryGetValue(plant.Model, out System.Collections.Generic.List<PlantData>? list))
            {
                list = new System.Collections.Generic.List<PlantData>();
                small[plant.Model] = list;
            }
            list.Add(plant);
        }
        float view = _generator!.Settings.GrassViewDistance;
        // Grass takes the biome's tint so it sits in the ground's colours (flowers keep their own).
        Vector3 middle = Position + new Vector3(_generator.Settings.ChunkSize * 0.5f, 0.0f, _generator.Settings.ChunkSize * 0.5f);
        Color tint = _generator.BiomeTint(_generator.DepthAt(_generator.Height(middle.X, middle.Z)));
        Color blade = _generator.Settings.GrassColor;
        StandardMaterial3D grassMaterial = new()
        {
            AlbedoColor = new Color(blade.R * tint.R, blade.G * tint.G, blade.B * tint.B),
            Roughness = 1.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        foreach ((string model, System.Collections.Generic.List<PlantData> plants) in small)
        {
            Mesh? mesh = ModelFit.FirstMesh(model, out Transform3D local);
            if (mesh == null)
            {
                continue;
            }
            MultiMesh batch = new() { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = plants.Count };
            // One buffer for all instances (12 floats each: the basis by rows, then the origin) instead
            // of a call per tuft, which made adding a chunk hitch.
            float[] buffer = new float[plants.Count * 12];
            for (int i = 0; i < plants.Count; i++)
            {
                PlantData plant = plants[i];
                Basis basis = new Basis(Vector3.Up, plant.Yaw).Scaled(Vector3.One * plant.Scale);
                Transform3D t = new Transform3D(basis, plant.Position - Position) * local;
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
            }
            batch.Buffer = buffer;
            Material? material = null;
            if (plants[0].Kind == PlantKind.Grass)
            {
                material = grassMaterial;
            }
            AddChild(new MultiMeshInstance3D
            {
                Name = "Plants",
                MaterialOverride = material,
                Multimesh = batch,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeEnd = view,
                VisibilityRangeEndMargin = 12.0f,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
            });
        }
    }

    /// <summary>A tree: its model scaled to its height, standing on the ground (leaning a little), with a solid trunk.</summary>
    private void AddTree(PlantData plant, SurfaceType? trunkSurface)
    {
        Mesh? mesh = ModelFit.FirstMesh(plant.Model, out Transform3D local);
        if (mesh == null)
        {
            return;
        }
        Aabb bounds = local * mesh.GetAabb();
        float scale = plant.Scale / Mathf.Max(bounds.Size.Y, 0.01f);
        Basis lean = Basis.FromEuler(new Vector3(plant.Lean.X, 0.0f, plant.Lean.Y));
        StaticBody3D body = new()
        {
            Name = "Tree",
            Transform = new Transform3D(lean * new Basis(Vector3.Up, plant.Yaw), plant.Position - Position),
        };
        // The model's foot on the ground: its lowest point at y = 0, centred over the trunk.
        Vector3 foot = new(bounds.GetCenter().X, bounds.Position.Y, bounds.GetCenter().Z);
        Transform3D fit = new(Basis.Identity.Scaled(Vector3.One * scale), -foot * scale);
        body.AddChild(new MeshInstance3D { Mesh = mesh, Transform = fit * local });
        float radius = Mathf.Clamp(plant.Scale * 0.035f, 0.12f, 0.4f);
        float trunk = Mathf.Min(plant.Scale, 3.0f);
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = radius, Height = trunk }, Position = new Vector3(0.0f, trunk * 0.5f, 0.0f) });
        if (trunkSurface != null)
        {
            body.SetMeta(SurfaceType.MetaKey, trunkSurface);
        }
        AddChild(body);
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
            if (!AddModel(body, _generator!.Settings.BoulderModels, obstacle, obstacle.Size, false))
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
    private static bool AddModel(StaticBody3D body, string[] paths, ObstacleData obstacle, Vector3 size, bool alongY)
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
        body.AddChild(new MeshInstance3D { Mesh = mesh, Transform = ModelFit.FitCentred(bounds, size) * turn * local });
        return true;
    }
}
