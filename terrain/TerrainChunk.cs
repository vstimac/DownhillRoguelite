using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// One streamed piece of the mountain: a flat-shaded mesh, a HeightMapShape3D collider and its
/// obstacles. Placed at the chunk's corner. Answers surface queries from the generator, so grip
/// and fall damage vary across the chunk.
/// </summary>
public partial class TerrainChunk : StaticBody3D, ISurfaceQuery
{
    public Vector2I Coord { get; private set; }

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

        if (obstacle.Kind == ObstacleKind.Boulder)
        {
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = obstacle.Size }, MaterialOverride = boulderMaterial });
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = obstacle.Size } });
            if (boulderSurface != null)
            {
                body.SetMeta(SurfaceType.MetaKey, boulderSurface);
            }
        }
        else
        {
            float radius = obstacle.Size.X * 0.5f;
            body.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = obstacle.Size.Y, RadialSegments = 6, Rings = 1 },
                MaterialOverride = trunkMaterial,
            });
            body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = radius, Height = obstacle.Size.Y } });
            if (trunkSurface != null)
            {
                body.SetMeta(SurfaceType.MetaKey, trunkSurface);
            }
        }
        AddChild(body);
    }
}
