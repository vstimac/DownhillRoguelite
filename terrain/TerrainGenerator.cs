using System.Diagnostics;
using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// The endless mountain as pure functions of a seed: height anywhere, the walkable spine, the
/// surface at a point, and whole chunks (height grid, flat-shaded mesh arrays, obstacles).
/// Plain C# with no engine calls, so chunks can be built on a background thread.
///
/// Height = descent gradient along -Z + slow variation + (wild ridges and bumps, blended to a
/// flat band along the spine). The band's cross-section is level and its slope only comes from
/// the gradient, the slow variation and small path bumps, which keeps it under SpineMaxSlope.
/// </summary>
public sealed class TerrainGenerator
{
    // Salts that give each noise layer its own seed.
    private const ulong BaseSalt = 1;
    private const ulong MountainSalt = 2;
    private const ulong BumpSalt = 3;
    private const ulong SpineSalt = 4;
    private const ulong SpineBumpSalt = 5;
    private const ulong SnowSalt = 6;
    private const ulong MudSalt = 7;
    private const ulong ScreeSalt = 8;
    private const ulong ObstacleSalt = 9;
    // 1D noise is read along a line off the lattice (noise is 0 on lattice points).
    private const float LineOffset = 0.5f;
    // Step for slope by finite differences, in m.
    private const float SlopeStep = 0.5f;

    private readonly TerrainSettings _settings;
    private readonly GradientNoise _base;
    private readonly GradientNoise _mountain;
    private readonly GradientNoise _bumps;
    private readonly GradientNoise _spine;
    private readonly GradientNoise _spineBumps;
    private readonly GradientNoise _snow;
    private readonly GradientNoise _mud;
    private readonly GradientNoise _scree;
    private readonly float _gradeTangent;

    public TerrainGenerator(TerrainSettings settings, ulong seed)
    {
        _settings = settings;
        Seed = seed;
        _base = new GradientNoise(SplitMix64.Mix(seed, BaseSalt));
        _mountain = new GradientNoise(SplitMix64.Mix(seed, MountainSalt));
        _bumps = new GradientNoise(SplitMix64.Mix(seed, BumpSalt));
        _spine = new GradientNoise(SplitMix64.Mix(seed, SpineSalt));
        _spineBumps = new GradientNoise(SplitMix64.Mix(seed, SpineBumpSalt));
        _snow = new GradientNoise(SplitMix64.Mix(seed, SnowSalt));
        _mud = new GradientNoise(SplitMix64.Mix(seed, MudSalt));
        _scree = new GradientNoise(SplitMix64.Mix(seed, ScreeSalt));
        _gradeTangent = Mathf.Tan(Mathf.DegToRad(settings.Grade));
    }

    public ulong Seed { get; }
    public TerrainSettings Settings => _settings;

    /// <summary>Centre line of the walkable spine at <paramref name="z"/>.</summary>
    public float SpineX(float z)
    {
        return _settings.SpineMeanderAmplitude * _spine.Sample(z / _settings.SpineMeanderWavelength, LineOffset);
    }

    /// <summary>1 inside the spine band, falling to 0 over SpineBlend beyond it.</summary>
    public float SpineWeight(float x, float z)
    {
        float distance = Mathf.Abs(x - SpineX(z));
        return 1.0f - Mathf.SmoothStep(_settings.SpineHalfWidth, _settings.SpineHalfWidth + _settings.SpineBlend, distance);
    }

    public float Height(float x, float z)
    {
        float descent = z * _gradeTangent
            + _settings.BaseVariationAmplitude * _base.Sample(z / _settings.BaseVariationWavelength, LineOffset);
        float wild = _settings.MountainAmplitude * _mountain.Fractal(x / _settings.MountainWavelength, z / _settings.MountainWavelength, _settings.MountainOctaves)
            + _settings.BumpAmplitude * _bumps.Sample(x / _settings.BumpWavelength, z / _settings.BumpWavelength);
        float path = _settings.SpineBumpAmplitude * _spineBumps.Sample(z / _settings.SpineBumpWavelength, LineOffset);
        return descent + Mathf.Lerp(wild, path, SpineWeight(x, z));
    }

    /// <summary>Steepness of the ground at a point, in degrees.</summary>
    public float SlopeDegrees(float x, float z)
    {
        float dx = (Height(x + SlopeStep, z) - Height(x - SlopeStep, z)) / (2.0f * SlopeStep);
        float dz = (Height(x, z + SlopeStep) - Height(x, z - SlopeStep)) / (2.0f * SlopeStep);
        return Mathf.RadToDeg(Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)));
    }

    public TerrainSurface SurfaceAt(float x, float z)
    {
        return Classify(x, z, SlopeDegrees(x, z), SpineWeight(x, z));
    }

    /// <summary>
    /// Steep ground is rock, then scree; gentle ground is grass with patches of snow and mud.
    /// The spine is kept to grass and snow so the guaranteed path is never loose or boggy.
    /// </summary>
    public TerrainSurface Classify(float x, float z, float slope, float spineWeight)
    {
        float scale = 1.0f / _settings.SurfaceNoiseWavelength;
        bool onSpine = spineWeight > 0.5f;
        if (!onSpine)
        {
            if (slope >= _settings.RockSlope)
            {
                return TerrainSurface.Rock;
            }
            if (slope >= _settings.ScreeSlope)
            {
                if (_scree.Sample(x * scale, z * scale) > -0.1f)
                {
                    return TerrainSurface.Scree;
                }
                return TerrainSurface.Rock;
            }
            if (slope <= _settings.MudMaxSlope && _mud.Sample(x * scale, z * scale) > _settings.MudThreshold)
            {
                return TerrainSurface.Mud;
            }
        }
        if (_snow.Sample(x * scale, z * scale) > _settings.SnowThreshold)
        {
            return TerrainSurface.Snow;
        }
        return TerrainSurface.Grass;
    }

    public Vector2I ChunkAt(float x, float z)
    {
        float size = _settings.ChunkSize;
        return new Vector2I(Mathf.FloorToInt(x / size), Mathf.FloorToInt(z / size));
    }

    /// <summary>World position of a chunk's lowest-x, lowest-z corner (y = 0).</summary>
    public Vector3 ChunkCorner(Vector2I coord)
    {
        float size = _settings.ChunkSize;
        return new Vector3(coord.X * size, 0.0f, coord.Y * size);
    }

    /// <summary>Builds a chunk. <paramref name="surfaceColors"/> is indexed by TerrainSurface.</summary>
    public TerrainChunkData BuildChunk(Vector2I coord, Color[] surfaceColors, int generation)
    {
        Stopwatch timer = Stopwatch.StartNew();
        int cells = _settings.ChunkCells;
        int points = cells + 1;
        float cell = _settings.CellSize;
        Vector3 corner = ChunkCorner(coord);
        TerrainChunkData data = new(coord, generation, points);

        for (int j = 0; j < points; j++)
        {
            for (int i = 0; i < points; i++)
            {
                data.Heights[i + j * points] = Height(corner.X + i * cell, corner.Z + j * cell);
            }
        }

        int triangles = cells * cells * 2;
        Vector3[] vertices = new Vector3[triangles * 3];
        Vector3[] normals = new Vector3[triangles * 3];
        Color[] colors = new Color[triangles * 3];
        SplitMix64 jitter = new(SplitMix64.Mix(Seed, ChunkSalt(coord)));
        int v = 0;
        for (int j = 0; j < cells; j++)
        {
            for (int i = 0; i < cells; i++)
            {
                Vector3 a = new(i * cell, data.Heights[i + j * points], j * cell);
                Vector3 b = new((i + 1) * cell, data.Heights[i + 1 + j * points], j * cell);
                Vector3 c = new(i * cell, data.Heights[i + (j + 1) * points], (j + 1) * cell);
                Vector3 d = new((i + 1) * cell, data.Heights[i + 1 + (j + 1) * points], (j + 1) * cell);
                // Clockwise seen from above, which Godot treats as the front face.
                v = AddTriangle(vertices, normals, colors, v, a, b, d, corner, surfaceColors, jitter);
                v = AddTriangle(vertices, normals, colors, v, a, d, c, corner, surfaceColors, jitter);
            }
        }
        data.Vertices = vertices;
        data.Normals = normals;
        data.Colors = colors;

        PlaceObstacles(data, coord, corner);
        data.BuildMilliseconds = timer.Elapsed.TotalMilliseconds;
        return data;
    }

    private int AddTriangle(Vector3[] vertices, Vector3[] normals, Color[] colors, int v,
        Vector3 p0, Vector3 p1, Vector3 p2, Vector3 corner, Color[] surfaceColors, SplitMix64 jitter)
    {
        Vector3 normal = (p2 - p0).Cross(p1 - p0).Normalized();
        Vector3 centre = (p0 + p1 + p2) / 3.0f + corner;
        float slope = Mathf.RadToDeg(normal.AngleTo(Vector3.Up));
        TerrainSurface surface = Classify(centre.X, centre.Z, slope, SpineWeight(centre.X, centre.Z));
        Color color = surfaceColors[(int)surface];
        float brightness = 1.0f + jitter.Range(-_settings.ColorJitter, _settings.ColorJitter);
        color = new Color(color.R * brightness, color.G * brightness, color.B * brightness);

        vertices[v] = p0;
        vertices[v + 1] = p1;
        vertices[v + 2] = p2;
        for (int k = 0; k < 3; k++)
        {
            normals[v + k] = normal;
            colors[v + k] = color;
        }
        return v + 3;
    }

    /// <summary>Seeded boulders and fallen trunks, never closer to the spine than its band plus SpineClearance.</summary>
    private void PlaceObstacles(TerrainChunkData data, Vector2I coord, Vector3 corner)
    {
        SplitMix64 random = new(SplitMix64.Mix(SplitMix64.Mix(Seed, ObstacleSalt), ChunkSalt(coord)));
        float size = _settings.ChunkSize;
        float area = size * size / 1000.0f;
        int boulders = Mathf.RoundToInt(_settings.BoulderDensity * area);
        int trunks = Mathf.RoundToInt(_settings.TrunkDensity * area);
        float maxTilt = Mathf.DegToRad(_settings.BoulderMaxTilt);

        for (int n = 0; n < boulders + trunks; n++)
        {
            float x = corner.X + random.Range(0.0f, size);
            float z = corner.Z + random.Range(0.0f, size);
            bool boulder = n < boulders;
            float extent;
            Vector3 shape;
            if (boulder)
            {
                shape = new Vector3(
                    random.Range(_settings.BoulderSize.X, _settings.BoulderSize.Y),
                    random.Range(_settings.BoulderSize.X, _settings.BoulderSize.Y),
                    random.Range(_settings.BoulderSize.X, _settings.BoulderSize.Y));
                extent = shape.Length() * 0.5f;
            }
            else
            {
                float length = random.Range(_settings.TrunkLength.X, _settings.TrunkLength.Y);
                shape = new Vector3(_settings.TrunkRadius * 2.0f, length, _settings.TrunkRadius * 2.0f);
                extent = length * 0.5f;
            }
            float yaw = random.Range(-Mathf.Pi, Mathf.Pi);
            float tiltX = random.Range(-maxTilt, maxTilt);
            float tiltZ = random.Range(-maxTilt, maxTilt);

            if (Mathf.Abs(x - SpineX(z)) < _settings.SpineHalfWidth + _settings.SpineClearance + extent)
            {
                continue;
            }

            float ground = Height(x, z);
            if (boulder)
            {
                Basis rotation = Basis.FromEuler(new Vector3(tiltX, yaw, tiltZ));
                Vector3 centre = new(x, ground + shape.Y * (0.5f - _settings.BoulderSink), z);
                data.Obstacles.Add(new ObstacleData(ObstacleKind.Boulder, centre, shape, rotation));
            }
            else
            {
                // Lying down: the cylinder's Y axis turned to horizontal, then yawed.
                Basis rotation = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Back, Mathf.Pi * 0.5f);
                Vector3 centre = new(x, ground + _settings.TrunkRadius * 0.7f, z);
                data.Obstacles.Add(new ObstacleData(ObstacleKind.Trunk, centre, shape, rotation));
            }
        }
    }

    private static ulong ChunkSalt(Vector2I coord)
    {
        return ((ulong)(uint)coord.X << 32) | (uint)coord.Y;
    }
}
