using System.Collections.Generic;
using DownhillRoguelite.Core;
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
///
/// Routes (M10): the run is cut into stages between stations; each stage has the spine (the safe
/// path) plus one or two seeded branches that leave it below the top station and rejoin it above
/// the bottom one: a hazardous branch (straight, narrow, rough, loose ground) or a supply-rich
/// one (winding, longer). Branches are bands like the spine and blend into the terrain the same way.
///
/// Summit (M12): the run starts on the mountain's highest point. Nothing near the start rises above
/// a plane falling gently from the summit, and behind it a cliff drops into the abyss.
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
    private const ulong RouteSalt = 10;
    private const ulong HazardBumpSalt = 11;
    private const ulong BiomeSalt = 12;
    private const ulong PlantSalt = 13;
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
    private readonly GradientNoise _hazardBumps;
    private readonly GradientNoise _biomeDither;
    private readonly float _gradeTangent;
    private readonly float _clearingSlopeTangent;
    private readonly float _summitCliffTangent;
    private readonly float _summitCapTangent;
    private readonly float _startHeight;
    private readonly object _stationLock = new();
    // Replaced, never modified, so readers on other threads can use it without the lock.
    private volatile Vector3[] _stations = System.Array.Empty<Vector3>();
    // Branches of stage s (ending at station s + 1) at index s; replaced together with _stations.
    private volatile RouteBranch[][] _branches = System.Array.Empty<RouteBranch[]>();
    // Metres between samples when searching down the route for the next station.
    private const float StationSearchStep = 2.0f;
    // Stations computed at a time when the mountain is explored beyond the known ones.
    private const int StationGrowth = 4;

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
        _hazardBumps = new GradientNoise(SplitMix64.Mix(seed, HazardBumpSalt));
        _biomeDither = new GradientNoise(SplitMix64.Mix(seed, BiomeSalt));
        _gradeTangent = Mathf.Tan(Mathf.DegToRad(settings.Grade));
        _clearingSlopeTangent = Mathf.Tan(Mathf.DegToRad(settings.ClearingSlope));
        _summitCliffTangent = Mathf.Tan(Mathf.DegToRad(settings.SummitCliffSlope));
        _summitCapTangent = Mathf.Tan(Mathf.DegToRad(settings.SummitCapSlope));
        _startHeight = SpineHeight(settings.SpawnZ);
        EnsureStations(settings.PrecomputedStations);
    }

    /// <summary>Route height at the start of the run, before any clearing.</summary>
    public float StartHeight => _startHeight;

    /// <summary>
    /// Centre of station <paramref name="index"/> (1 = the first below the start): on the spine where
    /// the route first drops StationDepthInterval × index below the start. Thread-safe.
    /// </summary>
    public Vector3 Station(int index)
    {
        Vector3[] stations = EnsureStations(index);
        return stations[index - 1];
    }

    /// <summary>The first station whose centre is downhill of <paramref name="z"/>.</summary>
    public int NextStationIndex(float z)
    {
        int index = 1;
        while (Station(index).Z >= z)
        {
            index++;
        }
        return index;
    }

    private Vector3[] EnsureStations(int count)
    {
        Vector3[] stations = _stations;
        if (stations.Length >= count)
        {
            return stations;
        }
        lock (_stationLock)
        {
            List<Vector3> list = new(_stations);
            List<RouteBranch[]> branches = new(_branches);
            float z = _settings.SpawnZ;
            if (list.Count > 0)
            {
                z = list[^1].Z;
            }
            while (list.Count < count)
            {
                float target = _startHeight - _settings.StationDepthInterval * (list.Count + 1);
                float top = z;
                do
                {
                    z -= StationSearchStep;
                }
                while (SpineHeight(z) > target);
                list.Add(new Vector3(BaseSpineX(z), SpineHeight(z), z));
                branches.Add(PlanBranches(list.Count - 1, top, z));
            }
            // Branches first: readers find a stage's branches by its station.
            _branches = branches.ToArray();
            _stations = list.ToArray();
            return _stations;
        }
    }

    /// <summary>Height along the spine's centre (no clearings, no branches): what stations are placed by.</summary>
    private float SpineHeight(float z)
    {
        return Descent(z) + SpinePath(z);
    }

    private float Descent(float z)
    {
        return z * _gradeTangent + _settings.BaseVariationAmplitude * _base.Sample(z / _settings.BaseVariationWavelength, LineOffset);
    }

    private float SpinePath(float z)
    {
        return _settings.SpineBumpAmplitude * _spineBumps.Sample(z / _settings.SpineBumpWavelength, LineOffset);
    }

    /// <summary>The seeded branches of stage <paramref name="stage"/>, between z <paramref name="top"/> and <paramref name="bottom"/>.</summary>
    private RouteBranch[] PlanBranches(int stage, float top, float bottom)
    {
        float split = top - _settings.BranchStationGap;
        float join = bottom + _settings.BranchStationGap;
        if (split - join < _settings.MinBranchLength)
        {
            return System.Array.Empty<RouteBranch>();
        }
        SplitMix64 random = new(SplitMix64.Mix(SplitMix64.Mix(Seed, RouteSalt), (ulong)stage + 1ul));
        float firstSide = 1.0f;
        if (random.NextFloat() < 0.5f)
        {
            firstSide = -1.0f;
        }
        bool both = random.NextFloat() < _settings.BothBranchesChance;
        RouteKind first = RouteKind.SupplyRich;
        if (random.NextFloat() < _settings.HazardousChance)
        {
            first = RouteKind.Hazardous;
        }
        List<RouteBranch> result = new() { MakeBranch(first, firstSide, split, join, random) };
        if (both)
        {
            RouteKind second = RouteKind.Hazardous;
            if (first == RouteKind.Hazardous)
            {
                second = RouteKind.SupplyRich;
            }
            result.Add(MakeBranch(second, -firstSide, split, join, random));
        }
        return result.ToArray();
    }

    private RouteBranch MakeBranch(RouteKind kind, float side, float split, float join, SplitMix64 random)
    {
        float offset = random.Range(_settings.BranchOffset.X, _settings.BranchOffset.Y);
        return new RouteBranch(kind, side, offset, split, join, BaseSpineX(split), BaseSpineX(join));
    }

    /// <summary>The branches of a stage (stage s runs from station s, or the start, to station s + 1).</summary>
    public RouteBranch[] Branches(int stage)
    {
        EnsureStations(stage + 1);
        return _branches[stage];
    }

    /// <summary>Branches whose band can reach <paramref name="z"/> (empty for most of the mountain).</summary>
    private RouteBranch[] BranchesAt(float z)
    {
        Vector3[] stations = EnsureStationsBelow(z);
        RouteBranch[][] branches = _branches;
        // Stations are ordered by descending z; the first one below z ends z's stage.
        int low = 0;
        int high = stations.Length;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (stations[mid].Z >= z)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }
        if (low >= branches.Length)
        {
            return System.Array.Empty<RouteBranch>();
        }
        return branches[low];
    }

    /// <summary>Centre line of a branch at <paramref name="z"/>; the spine outside its zone.</summary>
    public float BranchX(RouteBranch branch, float z)
    {
        float span = branch.SplitZ - branch.JoinZ;
        float t = Mathf.Clamp((branch.SplitZ - z) / span, 0.0f, 1.0f);
        float shape = BranchShape(branch, z);
        float x = SpineX(z);
        if (branch.Kind == RouteKind.Hazardous)
        {
            // Straight between the ends, cutting the spine's meanders.
            float chord = Mathf.Lerp(branch.SplitSpineX, branch.JoinSpineX, t);
            x = Mathf.Lerp(x, chord, shape);
        }
        x += branch.Side * branch.Offset * shape;
        if (branch.Kind == RouteKind.SupplyRich)
        {
            x += shape * _settings.SupplyMeanderAmplitude * Mathf.Sin(Mathf.Tau * z / _settings.SupplyMeanderWavelength);
        }
        return x;
    }

    public float HalfWidth(RouteKind kind)
    {
        if (kind == RouteKind.Hazardous)
        {
            return _settings.HazardHalfWidth;
        }
        if (kind == RouteKind.SupplyRich)
        {
            return _settings.SupplyHalfWidth;
        }
        return _settings.SpineHalfWidth;
    }

    private float BranchWeight(RouteBranch branch, float x, float z)
    {
        if (z > branch.SplitZ || z < branch.JoinZ)
        {
            return 0.0f;
        }
        float half = HalfWidth(branch.Kind);
        float distance = Mathf.Abs(x - BranchX(branch, z));
        return 1.0f - Mathf.SmoothStep(half, half + _settings.SpineBlend, distance);
    }

    private float BranchPath(RouteBranch branch, float x, float z)
    {
        if (branch.Kind == RouteKind.Hazardous)
        {
            float scale = 1.0f / _settings.HazardBumpWavelength;
            return _settings.HazardBumpAmplitude * _hazardBumps.Sample(x * scale, z * scale);
        }
        return SpinePath(z);
    }

    /// <summary>How strongly a point lies on any path's band (0 to 1), and the kind of the strongest.</summary>
    public float PathWeight(float x, float z, out RouteKind kind)
    {
        kind = RouteKind.Safe;
        float best = SpineWeight(x, z);
        foreach (RouteBranch branch in BranchesAt(z))
        {
            float weight = BranchWeight(branch, x, z);
            if (weight > best)
            {
                best = weight;
                kind = branch.Kind;
            }
        }
        return best;
    }

    /// <summary>Blends a point toward the terrace of a station within reach, if any.</summary>
    private float ApplyClearings(float x, float z, float height)
    {
        float reach = _settings.ClearingRadius + _settings.ClearingBlend;
        Vector3[] stations = EnsureStationsBelow(z - reach);
        // Stations are ordered by descending z; find the first one at or below z + reach.
        int low = 0;
        int high = stations.Length;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (stations[mid].Z > z + reach)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }
        for (int i = low; i < stations.Length && stations[i].Z >= z - reach; i++)
        {
            Vector3 station = stations[i];
            float distance = new Vector2(x - station.X, z - station.Z).Length();
            if (distance >= reach)
            {
                continue;
            }
            // A gentle terrace following the descent, so the ramps in and out stay walkable.
            float terrace = station.Y + (z - station.Z) * _clearingSlopeTangent;
            float weight = 1.0f - Mathf.SmoothStep(_settings.ClearingRadius, reach, distance);
            height = Mathf.Lerp(height, terrace, weight);
        }
        return height;
    }

    private Vector3[] EnsureStationsBelow(float z)
    {
        Vector3[] stations = _stations;
        while (stations.Length == 0 || stations[^1].Z > z)
        {
            stations = EnsureStations(stations.Length + StationGrowth);
        }
        return stations;
    }

    public ulong Seed { get; }
    public TerrainSettings Settings => _settings;

    /// <summary>
    /// Centre line of the walkable spine (the safe path) at <paramref name="z"/>: the slow meander,
    /// plus a gentle winding inside a stage's branch zone.
    /// </summary>
    public float SpineX(float z)
    {
        float x = BaseSpineX(z);
        RouteBranch[] branches = BranchesAt(z);
        if (branches.Length > 0)
        {
            x += BranchShape(branches[0], z) * _settings.SafeWindingAmplitude * Mathf.Sin(Mathf.Tau * z / _settings.SafeWindingWavelength);
        }
        return x;
    }

    /// <summary>The spine's slow meander alone (no winding), used to place stations and branch ends.</summary>
    private float BaseSpineX(float z)
    {
        return _settings.SpineMeanderAmplitude * _spine.Sample(z / _settings.SpineMeanderWavelength, LineOffset);
    }

    /// <summary>0 outside a branch's zone, rising to 1 over its ramps: how far into its swing it is.</summary>
    private float BranchShape(RouteBranch branch, float z)
    {
        float span = branch.SplitZ - branch.JoinZ;
        float t = Mathf.Clamp((branch.SplitZ - z) / span, 0.0f, 1.0f);
        float ramp = Mathf.Max(_settings.BranchRamp, 0.01f);
        return Mathf.SmoothStep(0.0f, ramp, t) * Mathf.SmoothStep(0.0f, ramp, 1.0f - t);
    }

    /// <summary>1 inside the spine band, falling to 0 over SpineBlend beyond it.</summary>
    public float SpineWeight(float x, float z)
    {
        float distance = Mathf.Abs(x - SpineX(z));
        return 1.0f - Mathf.SmoothStep(_settings.SpineHalfWidth, _settings.SpineHalfWidth + _settings.SpineBlend, distance);
    }

    public float Height(float x, float z)
    {
        return Mathf.Min(ApplyClearings(x, z, RawHeight(x, z)), SummitCap(z));
    }

    /// <summary>Height of the summit plateau: the highest ground of the whole mountain.</summary>
    public float SummitHeight => _startHeight + _settings.SummitRise;

    /// <summary>
    /// The most the ground may rise at <paramref name="z"/>: a plane falling gently ahead of the
    /// summit, flat over the plateau just behind the start, then a cliff into the abyss.
    /// </summary>
    public float SummitCap(float z)
    {
        float behind = z - _settings.SpawnZ;
        if (behind <= 0.0f)
        {
            return SummitHeight + behind * _summitCapTangent;
        }
        if (behind <= _settings.SummitBack)
        {
            return SummitHeight;
        }
        return SummitHeight - (behind - _settings.SummitBack) * _summitCliffTangent;
    }

    /// <summary>Whether a point has fallen off the back of the summit into the abyss.</summary>
    public bool InAbyss(Vector3 position)
    {
        return position.Z > _settings.SpawnZ + _settings.SummitBack && position.Y < SummitHeight - _settings.AbyssDrop;
    }

    /// <summary>Height before station clearings are cut in.</summary>
    private float RawHeight(float x, float z)
    {
        float wild = _settings.MountainAmplitude * _mountain.Fractal(x / _settings.MountainWavelength, z / _settings.MountainWavelength, _settings.MountainOctaves)
            + _settings.BumpAmplitude * _bumps.Sample(x / _settings.BumpWavelength, z / _settings.BumpWavelength);
        // Every band (spine and branches) pulls the ground to its own path height, by weight.
        float spine = SpineWeight(x, z);
        float weightSum = spine;
        float pathSum = spine * SpinePath(z);
        float strongest = spine;
        foreach (RouteBranch branch in BranchesAt(z))
        {
            float weight = BranchWeight(branch, x, z);
            if (weight <= 0.0f)
            {
                continue;
            }
            weightSum += weight;
            pathSum += weight * BranchPath(branch, x, z);
            strongest = Mathf.Max(strongest, weight);
        }
        float path = 0.0f;
        if (weightSum > 0.0f)
        {
            path = pathSum / weightSum;
        }
        return Descent(z) + Mathf.Lerp(wild, path, strongest);
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
        float weight = PathWeight(x, z, out RouteKind kind);
        return Classify(x, z, SlopeDegrees(x, z), weight, kind);
    }

    public TerrainSurface Classify(float x, float z, float slope, float spineWeight)
    {
        return Classify(x, z, slope, spineWeight, RouteKind.Safe);
    }

    /// <summary>
    /// Steep ground is rock, then scree; gentle ground is grass with patches of snow and mud.
    /// The safe path is kept to grass and snow so it is never loose or boggy; a hazardous path is
    /// loose scree with patches of snow; a supply-rich path is grass with snow and boggy mud.
    /// </summary>
    public TerrainSurface Classify(float x, float z, float slope, float pathWeight, RouteKind kind)
    {
        float scale = 1.0f / _settings.SurfaceNoiseWavelength;
        bool onPath = pathWeight > 0.5f;
        if (onPath && kind == RouteKind.Hazardous)
        {
            if (_snow.Sample(x * scale, z * scale) > _settings.SnowThreshold)
            {
                return TerrainSurface.Snow;
            }
            return TerrainSurface.Scree;
        }
        if (onPath && kind == RouteKind.SupplyRich && _mud.Sample(x * scale, z * scale) > _settings.MudThreshold)
        {
            return TerrainSurface.Mud;
        }
        if (!onPath)
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
        PlacePlants(data, coord, corner);
        data.BuildMilliseconds = timer.Elapsed.TotalMilliseconds;
        return data;
    }

    private int AddTriangle(Vector3[] vertices, Vector3[] normals, Color[] colors, int v,
        Vector3 p0, Vector3 p1, Vector3 p2, Vector3 corner, Color[] surfaceColors, SplitMix64 jitter)
    {
        Vector3 normal = (p2 - p0).Cross(p1 - p0).Normalized();
        Vector3 centre = (p0 + p1 + p2) / 3.0f + corner;
        float slope = Mathf.RadToDeg(normal.AngleTo(Vector3.Up));
        float pathWeight = PathWeight(centre.X, centre.Z, out RouteKind kind);
        TerrainSurface surface = Classify(centre.X, centre.Z, slope, pathWeight, kind);
        Color color = surfaceColors[(int)surface];
        // The biome tints the ground: grass and mud fully, snow and rock half as much.
        Color tint = BiomeTint(DepthAt(centre.Y));
        if (surface != TerrainSurface.Grass && surface != TerrainSurface.Mud)
        {
            tint = Colors.White.Lerp(tint, 0.5f);
        }
        color = new Color(color.R * tint.R, color.G * tint.G, color.B * tint.B);
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

    /// <summary>
    /// Seeded boulders and fallen trunks, never closer to a path than its band plus SpineClearance
    /// (HazardClearance for hazardous paths, which they crowd).
    /// </summary>
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

            if (TooCloseToPath(x, z, extent))
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

    /// <summary>Metres below the summit at this height.</summary>
    public float DepthAt(float height)
    {
        return Mathf.Max(0.0f, _startHeight - height);
    }

    /// <summary>
    /// The two biomes around a depth and how far it is from the first to the second (0 to 1); away
    /// from a boundary both are the same biome and the blend is 0.
    /// </summary>
    public void BiomesAt(float depth, out int from, out int to, out float blend)
    {
        Biome[] biomes = _settings.Biomes;
        from = 0;
        to = 0;
        blend = 0.0f;
        if (biomes.Length == 0)
        {
            return;
        }
        float half = _settings.BiomeBlend * 0.5f;
        for (int k = 1; k < biomes.Length; k++)
        {
            float edge = biomes[k].FromDepth;
            if (depth < edge - half)
            {
                from = k - 1;
                to = k - 1;
                return;
            }
            if (depth <= edge + half)
            {
                from = k - 1;
                to = k;
                blend = Mathf.SmoothStep(edge - half, edge + half, depth);
                return;
            }
        }
        from = biomes.Length - 1;
        to = biomes.Length - 1;
    }

    /// <summary>The ground tint at a depth, blended smoothly between biomes.</summary>
    public Color BiomeTint(float depth)
    {
        if (_settings.Biomes.Length == 0)
        {
            return Colors.White;
        }
        BiomesAt(depth, out int from, out int to, out float blend);
        return _settings.Biomes[from].GroundTint.Lerp(_settings.Biomes[to].GroundTint, blend);
    }

    /// <summary>
    /// Which biome a spot belongs to: inside a blend band the choice is dithered by noise, so the two
    /// biomes' plants interleave in patches rather than switching along a line.
    /// </summary>
    public Biome? BiomeAt(float x, float z, float depth)
    {
        if (_settings.Biomes.Length == 0)
        {
            return null;
        }
        BiomesAt(depth, out int from, out int to, out float blend);
        if (from == to)
        {
            return _settings.Biomes[from];
        }
        float dither = _biomeDither.Sample(x / 24.0f, z / 24.0f) * 0.5f + 0.5f;
        if (dither < blend)
        {
            return _settings.Biomes[to];
        }
        return _settings.Biomes[from];
    }

    /// <summary>
    /// Trees, grass and flowers by biome. Trees keep off paths, station clearings and steep ground;
    /// grass and flowers grow on grassy ground off the trodden path. Grass reads heights from the
    /// chunk's grid (cheap); trees from the terrain itself.
    /// </summary>
    private void PlacePlants(TerrainChunkData data, Vector2I coord, Vector3 corner)
    {
        if (_settings.Biomes.Length == 0)
        {
            return;
        }
        SplitMix64 random = new(SplitMix64.Mix(SplitMix64.Mix(Seed, PlantSalt), ChunkSalt(coord)));
        float size = _settings.ChunkSize;
        float maxTrees = 0.0f;
        float maxGrass = 0.0f;
        float maxFlowers = 0.0f;
        foreach (Biome biome in _settings.Biomes)
        {
            maxTrees = Mathf.Max(maxTrees, biome.TreeDensity);
            maxGrass = Mathf.Max(maxGrass, biome.GrassDensity);
            maxFlowers = Mathf.Max(maxFlowers, biome.FlowerDensity);
        }

        // Trees: candidates at the densest biome's rate, kept with each spot's own biome's chance.
        int treeTries = Mathf.RoundToInt(maxTrees * size * size / 1000.0f);
        for (int n = 0; n < treeTries; n++)
        {
            float x = corner.X + random.Range(0.0f, size);
            float z = corner.Z + random.Range(0.0f, size);
            float keep = random.NextFloat();
            float yaw = random.Range(-Mathf.Pi, Mathf.Pi);
            float pick = random.NextFloat();
            float tall = random.NextFloat();
            Vector2 lean = new(random.Range(-1.0f, 1.0f), random.Range(-1.0f, 1.0f));
            float ground = Height(x, z);
            Biome? biome = BiomeAt(x, z, DepthAt(ground));
            if (biome == null || biome.TreeModels.Length == 0 || keep * maxTrees >= biome.TreeDensity)
            {
                continue;
            }
            if (TooCloseToPath(x, z, 2.0f) || NearStation(x, z, _settings.ClearingRadius + _settings.TreeStationGap) || SlopeDegrees(x, z) > _settings.TreeMaxSlope)
            {
                continue;
            }
            string model = biome.TreeModels[Mathf.Min((int)(pick * biome.TreeModels.Length), biome.TreeModels.Length - 1)];
            float height = Mathf.Lerp(biome.TreeHeight.X, biome.TreeHeight.Y, tall);
            data.Plants.Add(new PlantData(PlantKind.Tree, model, new Vector3(x, ground, z), yaw, height, lean * Mathf.DegToRad(biome.TreeLean)));
        }

        // Grass and flowers: a jittered grid at the densest rate, thinned per biome; heights from the grid.
        float density = maxGrass + maxFlowers;
        if (density <= 0.0f)
        {
            return;
        }
        float step = 1.0f / Mathf.Sqrt(density);
        int steps = Mathf.FloorToInt(size / step);
        for (int j = 0; j < steps; j++)
        {
            for (int i = 0; i < steps; i++)
            {
                float lx = (i + random.NextFloat()) * step;
                float lz = (j + random.NextFloat()) * step;
                float keep = random.NextFloat() * density;
                float pick = random.NextFloat();
                float scale = random.NextFloat();
                float yaw = random.Range(-Mathf.Pi, Mathf.Pi);
                float x = corner.X + lx;
                float z = corner.Z + lz;
                float ground = GridHeight(data, lx, lz);
                Biome? biome = BiomeAt(x, z, DepthAt(ground));
                if (biome == null)
                {
                    continue;
                }
                PlantKind kind = PlantKind.Grass;
                string[] models = biome.GrassModels;
                if (keep >= biome.GrassDensity)
                {
                    if (keep - maxGrass < 0.0f || keep - maxGrass >= biome.FlowerDensity)
                    {
                        continue;
                    }
                    kind = PlantKind.Flower;
                    models = biome.FlowerModels;
                }
                if (models.Length == 0)
                {
                    continue;
                }
                float pathWeight = PathWeight(x, z, out RouteKind route);
                // Grass grows up to the path's trodden middle, and only on grassy ground.
                if (pathWeight > 0.85f || Classify(x, z, GridSlope(data, lx, lz), 0.0f, route) != TerrainSurface.Grass)
                {
                    continue;
                }
                string model = models[Mathf.Min((int)(pick * models.Length), models.Length - 1)];
                float s = Mathf.Lerp(biome.GrassScale.X, biome.GrassScale.Y, scale);
                data.Plants.Add(new PlantData(kind, model, new Vector3(x, ground, z), yaw, s, Vector2.Zero));
            }
        }
    }

    /// <summary>Height inside a chunk from its grid (local x, z in m), bilinear.</summary>
    private float GridHeight(TerrainChunkData data, float lx, float lz)
    {
        float cell = _settings.CellSize;
        int points = data.GridPoints;
        float gx = Mathf.Clamp(lx / cell, 0.0f, points - 1.001f);
        float gz = Mathf.Clamp(lz / cell, 0.0f, points - 1.001f);
        int i = (int)gx;
        int j = (int)gz;
        float fx = gx - i;
        float fz = gz - j;
        float h00 = data.Heights[i + j * points];
        float h10 = data.Heights[i + 1 + j * points];
        float h01 = data.Heights[i + (j + 1) * points];
        float h11 = data.Heights[i + 1 + (j + 1) * points];
        return Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fz);
    }

    /// <summary>Slope inside a chunk from its grid, in degrees.</summary>
    private float GridSlope(TerrainChunkData data, float lx, float lz)
    {
        float cell = _settings.CellSize;
        float dx = GridHeight(data, lx + cell, lz) - GridHeight(data, lx - cell, lz);
        float dz = GridHeight(data, lx, lz + cell) - GridHeight(data, lx, lz - cell);
        return Mathf.RadToDeg(Mathf.Atan(new Vector2(dx, dz).Length() / (2.0f * cell)));
    }

    /// <summary>True within a distance of a station's centre.</summary>
    public bool NearStation(float x, float z, float distance)
    {
        Vector3[] stations = EnsureStationsBelow(z - distance);
        foreach (Vector3 station in stations)
        {
            if (Mathf.Abs(station.Z - z) <= distance && new Vector2(x - station.X, z - station.Z).Length() < distance)
            {
                return true;
            }
        }
        return false;
    }

    private bool TooCloseToPath(float x, float z, float extent)
    {
        if (Mathf.Abs(x - SpineX(z)) < _settings.SpineHalfWidth + _settings.SpineClearance + extent)
        {
            return true;
        }
        foreach (RouteBranch branch in BranchesAt(z))
        {
            if (z > branch.SplitZ || z < branch.JoinZ)
            {
                continue;
            }
            float clearance = _settings.SpineClearance;
            if (branch.Kind == RouteKind.Hazardous)
            {
                clearance = _settings.HazardClearance;
            }
            if (Mathf.Abs(x - BranchX(branch, z)) < HalfWidth(branch.Kind) + clearance + extent)
            {
                return true;
            }
        }
        return false;
    }

    private static ulong ChunkSalt(Vector2I coord)
    {
        return ((ulong)(uint)coord.X << 32) | (uint)coord.Y;
    }
}
