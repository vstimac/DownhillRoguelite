using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// Streams the endless mountain around the carrier: keeps chunks loaded in a window that reaches
/// further downhill (-Z) than uphill, builds missing ones on a background thread one at a time
/// (nearest first), frees ones left behind, and publishes the deepest point reached. Follows the
/// carrier through LocomotionSnapshot events. Chunks around the spawn are built synchronously on
/// (re)seed so there is always ground under the carrier.
/// </summary>
public partial class TerrainStreamer : Node3D
{
    [Export] public TerrainSettings Settings { get; set; } = new();
    /// <summary>
    /// A new random mountain at start (the game); off keeps Settings.DefaultSeed (tests). A
    /// `-- --seed=N` command-line argument always wins, to replay a mountain.
    /// </summary>
    [Export] public bool RandomSeedOnStart { get; set; }
    /// <summary>Chunks kept loaded downhill (-Z) of the carrier's chunk.</summary>
    [Export] public int ChunksAhead { get; set; } = 3;
    [Export] public int ChunksBehind { get; set; } = 1;
    /// <summary>Chunks kept loaded on each side of the carrier's chunk.</summary>
    [Export] public int ChunksSide { get; set; } = 2;
    /// <summary>Chunks this far outside the window (in chunks) are freed, so walking along a border doesn't thrash.</summary>
    [Export] public int UnloadMargin { get; set; } = 1;
    /// <summary>Depth is published each time it grows by this much, in m.</summary>
    [Export] public float DepthStep { get; set; } = 1.0f;

    private bool _inAbyss;
    public TerrainGenerator? Generator { get; private set; }
    /// <summary>Start of the run: on the spine at Settings.SpawnZ.</summary>
    public Vector3 SpawnPoint { get; private set; }
    public int LoadedCount => _chunks.Count;
    public double LastBuildMilliseconds { get; private set; }
    public double LastApplyMilliseconds { get; private set; }
    /// <summary>Longest main-thread time spent adding one chunk since the last reseed, in ms.</summary>
    public double MaxApplyMilliseconds { get; private set; }
    public float Depth { get; private set; }
    public IEnumerable<Vector2I> LoadedChunks => _chunks.Keys;

    [ExportGroup("Ground look")]
    /// <summary>The ground shader (patches, grain, bumps, rocky slopes); empty: plain vertex colours.</summary>
    [Export(PropertyHint.File, "*.gdshader")] public string GroundShader { get; set; } = "res://terrain/terrain_ground.gdshader";
    /// <summary>Patches of lighter and darker ground: 1 / their size in m, and how strong.</summary>
    [Export] public float PatchScale { get; set; } = 0.03f;
    [Export] public float PatchStrength { get; set; } = 0.34f;
    /// <summary>Fine grain in the ground colour.</summary>
    [Export] public float GrainScale { get; set; } = 0.22f;
    [Export] public float GrainStrength { get; set; } = 0.16f;
    /// <summary>Small bumps that catch the light.</summary>
    [Export] public float BumpScale { get; set; } = 0.35f;
    [Export] public float BumpStrength { get; set; } = 0.9f;
    /// <summary>Steep ground turns rocky (from slope RockStart to RockEnd, as 1 - normal.y) and darker.</summary>
    [Export] public Color RockColor { get; set; } = new(0.34f, 0.32f, 0.3f);
    [Export] public float RockStart { get; set; } = 0.3f;
    [Export] public float RockEnd { get; set; } = 0.62f;
    [Export] public float RockAmount { get; set; } = 0.65f;
    [Export] public float SlopeDarkening { get; set; } = 0.28f;
    [Export] public int NoiseSize { get; set; } = 256;

    private readonly Dictionary<Vector2I, TerrainChunk> _chunks = new();
    private readonly SurfaceType?[] _surfaces = new SurfaceType?[System.Enum.GetValues<TerrainSurface>().Length];
    private Color[] _surfaceColors = System.Array.Empty<Color>();
    private SurfaceType? _boulderSurface;
    private SurfaceType? _trunkSurface;
    private Material? _groundMaterial;
    private StandardMaterial3D? _boulderMaterial;
    private StandardMaterial3D? _trunkMaterial;
    private Task<TerrainChunkData>? _pending;
    private int _generation;
    private Vector3 _focus;
    private float _publishedDepth;

    /// <summary>The ground's material: the ground shader with a seamless height noise, or plain vertex colours.</summary>
    private Material MakeGroundMaterial()
    {
        if (GroundShader.Length == 0 || ResourceLoader.Load(GroundShader) is not Shader shader)
        {
            return new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1.0f };
        }
        NoiseTexture2D noise = new()
        {
            Width = NoiseSize,
            Height = NoiseSize,
            Seamless = true,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.012f, FractalOctaves = 4 },
        };
        ShaderMaterial material = new() { Shader = shader };
        material.SetShaderParameter("height_noise", noise);
        material.SetShaderParameter("macro_scale", PatchScale);
        material.SetShaderParameter("macro_strength", PatchStrength);
        material.SetShaderParameter("micro_scale", GrainScale);
        material.SetShaderParameter("micro_strength", GrainStrength);
        material.SetShaderParameter("bump_scale", BumpScale);
        material.SetShaderParameter("bump_strength", BumpStrength);
        material.SetShaderParameter("rock_color", RockColor);
        material.SetShaderParameter("rock_start", RockStart);
        material.SetShaderParameter("rock_end", RockEnd);
        material.SetShaderParameter("rock_amount", RockAmount);
        material.SetShaderParameter("slope_darkening", SlopeDarkening);
        return material;
    }

    /// <summary>
    /// Loads every biome's tree, grass and flower model once at start, so a chunk that first uses one
    /// doesn't stall the game while the file loads.
    /// </summary>
    private void WarmPlantModels()
    {
        foreach (Biome biome in Settings.Biomes)
        {
            foreach (string[] models in new[] { biome.TreeModels, biome.GrassModels, biome.FlowerModels })
            {
                foreach (string model in models)
                {
                    ModelFit.FirstMesh(model, out _);
                }
            }
        }
    }

    public override void _Ready()
    {
        LoadSurfaces();
        _groundMaterial = MakeGroundMaterial();
        _boulderMaterial = new StandardMaterial3D { AlbedoColor = ColorOf(_boulderSurface, new Color(0.5f, 0.5f, 0.52f)), Roughness = 1.0f };
        _trunkMaterial = new StandardMaterial3D { AlbedoColor = ColorOf(_trunkSurface, new Color(0.42f, 0.3f, 0.2f)), Roughness = 1.0f };
        WarmPlantModels();

        GameEvents.LocomotionUpdated += OnLocomotionUpdated;
        GameEvents.NewMountain += Reseed;

        ulong seed = Settings.DefaultSeed;
        RunState? run = GetNodeOrNull<RunState>("/root/RunState");
        if (run != null && run.Seed != 0)
        {
            seed = run.Seed;
        }
        else if (RandomSeedOnStart)
        {
            seed = RunState.RandomSeed();
        }
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--seed=") && ulong.TryParse(arg.Substring("--seed=".Length), out ulong chosen))
            {
                seed = chosen;
            }
        }
        Reseed(seed);
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotionUpdated;
        GameEvents.NewMountain -= Reseed;
        RunState? run = GetNodeOrNull<RunState>("/root/RunState");
        if (run != null)
        {
            run.Route = null;
        }
        _generation++;
        _pending = null;
        _chunks.Clear();
        System.Array.Clear(_surfaces);
        _boulderSurface = null;
        _trunkSurface = null;
        _groundMaterial?.Dispose();
        _boulderMaterial?.Dispose();
        _trunkMaterial?.Dispose();
        _groundMaterial = null;
        _boulderMaterial = null;
        _trunkMaterial = null;
    }

    /// <summary>Throws away the mountain and builds a new one from <paramref name="seed"/> around a new spawn point.</summary>
    public void Reseed(ulong seed)
    {
        _generation++;
        _pending = null;
        foreach (TerrainChunk chunk in _chunks.Values)
        {
            chunk.QueueFree();
        }
        _chunks.Clear();

        Generator = new TerrainGenerator(Settings, seed);
        float z = Settings.SpawnZ;
        float x = Generator.SpineX(z);
        SpawnPoint = new Vector3(x, Generator.Height(x, z), z);
        _focus = SpawnPoint;
        Depth = 0.0f;
        _publishedDepth = 0.0f;
        _inAbyss = false;
        MaxApplyMilliseconds = 0.0;

        RunState? run = GetNodeOrNull<RunState>("/root/RunState");
        if (run != null)
        {
            run.Seed = seed;
            run.Route = new TerrainRoute(Generator);
        }

        foreach (Vector2I coord in Window(Generator.ChunkAt(_focus.X, _focus.Z)))
        {
            Apply(Generator.BuildChunk(coord, _surfaceColors, _generation));
        }
        // The synchronous start-up builds shouldn't count as streaming hitches.
        MaxApplyMilliseconds = 0.0;
        GameEvents.PublishDepth(0.0f);
    }

    public override void _Process(double delta)
    {
        if (Generator == null)
        {
            return;
        }

        Vector2I centre = Generator.ChunkAt(_focus.X, _focus.Z);
        UnloadFar(centre);

        if (_pending != null && _pending.IsCompleted)
        {
            Task<TerrainChunkData> done = _pending;
            _pending = null;
            if (done.IsFaulted)
            {
                GD.PushError($"TerrainStreamer: chunk build failed: {done.Exception?.GetBaseException().Message}");
            }
            else if (done.Result.Generation == _generation && InWindow(done.Result.Coord, centre, 0) && !_chunks.ContainsKey(done.Result.Coord))
            {
                Apply(done.Result);
            }
        }

        if (_pending == null && NearestMissing(centre, out Vector2I next))
        {
            TerrainGenerator generator = Generator;
            Color[] colors = _surfaceColors;
            int generation = _generation;
            _pending = Task.Run(() => generator.BuildChunk(next, colors, generation));
        }
    }

    private void OnLocomotionUpdated(LocomotionSnapshot snapshot)
    {
        _focus = snapshot.Position;
        // Behind the summit is the abyss: a fall there ends the run and doesn't count as descent.
        if (Generator != null && Generator.InAbyss(snapshot.Position))
        {
            if (!_inAbyss)
            {
                _inAbyss = true;
                GameEvents.PublishKill("the abyss");
            }
            return;
        }
        float depth = SpawnPoint.Y - snapshot.Position.Y;
        if (snapshot.Position.Z > SpawnPoint.Z)
        {
            depth = 0.0f;
        }
        if (depth > Depth)
        {
            Depth = depth;
        }
        if (Depth >= _publishedDepth + DepthStep)
        {
            _publishedDepth = Depth;
            GameEvents.PublishDepth(Depth);
        }
    }

    private void Apply(TerrainChunkData data)
    {
        if (Generator == null || _groundMaterial == null || _boulderMaterial == null || _trunkMaterial == null)
        {
            return;
        }

        Stopwatch timer = Stopwatch.StartNew();
        TerrainChunk chunk = new();
        chunk.Build(data, Generator, _surfaces, _groundMaterial, _boulderMaterial, _trunkMaterial, _boulderSurface, _trunkSurface);
        AddChild(chunk);
        _chunks[data.Coord] = chunk;

        LastBuildMilliseconds = data.BuildMilliseconds;
        LastApplyMilliseconds = timer.Elapsed.TotalMilliseconds;
        if (LastApplyMilliseconds > MaxApplyMilliseconds)
        {
            MaxApplyMilliseconds = LastApplyMilliseconds;
        }
    }

    private void UnloadFar(Vector2I centre)
    {
        List<Vector2I> far = new();
        foreach (Vector2I coord in _chunks.Keys)
        {
            if (!InWindow(coord, centre, UnloadMargin))
            {
                far.Add(coord);
            }
        }
        foreach (Vector2I coord in far)
        {
            _chunks[coord].QueueFree();
            _chunks.Remove(coord);
        }
    }

    private bool NearestMissing(Vector2I centre, out Vector2I nearest)
    {
        nearest = centre;
        int best = int.MaxValue;
        foreach (Vector2I coord in Window(centre))
        {
            if (_chunks.ContainsKey(coord))
            {
                continue;
            }
            int distance = (coord - centre).LengthSquared();
            if (distance < best)
            {
                best = distance;
                nearest = coord;
            }
        }
        return best < int.MaxValue;
    }

    /// <summary>Chunks to keep loaded around <paramref name="centre"/>; downhill is -Z.</summary>
    private IEnumerable<Vector2I> Window(Vector2I centre)
    {
        for (int dz = -ChunksAhead; dz <= ChunksBehind; dz++)
        {
            for (int dx = -ChunksSide; dx <= ChunksSide; dx++)
            {
                yield return new Vector2I(centre.X + dx, centre.Y + dz);
            }
        }
    }

    private bool InWindow(Vector2I coord, Vector2I centre, int margin)
    {
        Vector2I offset = coord - centre;
        return offset.X >= -ChunksSide - margin && offset.X <= ChunksSide + margin
            && offset.Y >= -ChunksAhead - margin && offset.Y <= ChunksBehind + margin;
    }

    private void LoadSurfaces()
    {
        _surfaces[(int)TerrainSurface.Rock] = LoadSurface(Settings.RockPath);
        _surfaces[(int)TerrainSurface.Scree] = LoadSurface(Settings.ScreePath);
        _surfaces[(int)TerrainSurface.Grass] = LoadSurface(Settings.GrassPath);
        _surfaces[(int)TerrainSurface.Snow] = LoadSurface(Settings.SnowPath);
        _surfaces[(int)TerrainSurface.Mud] = LoadSurface(Settings.MudPath);
        _boulderSurface = LoadSurface(Settings.BoulderSurfacePath);
        _trunkSurface = LoadSurface(Settings.TrunkSurfacePath);

        _surfaceColors = new Color[_surfaces.Length];
        for (int i = 0; i < _surfaces.Length; i++)
        {
            _surfaceColors[i] = ColorOf(_surfaces[i], Colors.Gray);
        }
    }

    private static SurfaceType? LoadSurface(string path)
    {
        if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path))
        {
            GD.PushWarning($"TerrainStreamer: surface {path} not found.");
            return null;
        }
        // Soft cast: during editor import the C# script may not be attached yet.
        return ResourceLoader.Load(path) as SurfaceType;
    }

    private static Color ColorOf(SurfaceType? surface, Color fallback)
    {
        if (surface == null)
        {
            return fallback;
        }
        return surface.DebugColor;
    }
}
