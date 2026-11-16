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
public sealed partial class TerrainGenerator
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
    private const ulong DetailSalt = 14;
    // Ground cover (shrubs, loose rocks, debris, things by the path, meadows, fallen logs) has its
    // own streams, so adding it moved nothing that was placed before it.
    private const ulong GroundCoverSalt = 15;
    private const ulong ThicketSalt = 16;
    // The second round's roadside cover and outcrops draw from their own stream as well.
    private const ulong RoadsideSalt = 17;
    // The third round's relief (flanking ridges, crags, the valley opening and closing), bare
    // ground, boulder groups and lone trees: their own streams again.
    private const ulong RidgeSalt = 18;
    private const ulong CragSalt = 19;
    private const ulong ValleySalt = 20;
    private const ulong BareSalt = 21;
    private const ulong ClusterSalt = 22;
    // The fourth round's tree lines on the flanks.
    private const ulong FlankTreeSalt = 23;
    // The fifth round: shrubs in the flanks' gullies, and hedgerows and walls round the fields.
    private const ulong GullySalt = 24;
    private const ulong HedgeSalt = 25;
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
    private readonly GradientNoise _forestPatches;
    private readonly GradientNoise _thickets;
    private readonly GradientNoise _ridges;
    private readonly GradientNoise _crags;
    private readonly GradientNoise _valley;
    private readonly GradientNoise _bare;
    private readonly float _gradeTangent;
    private readonly float _clearingSlopeTangent;
    private readonly float _summitCliffTangent;
    private readonly float _summitCapTangent;
    private readonly float _startHeight;
    // The summit's level pad (centre on the ground); none until worked out at the end of the constructor.
    private Vector3 _summitPad;
    private bool _hasSummitPad;
    private readonly object _stationLock = new();
    private readonly object _indexLock = new();
    private int _indexCells = -1;
    // Triangle topology is identical for every chunk of this grid size. Treat as read-only.
    private int[] _smoothIndices = System.Array.Empty<int>();
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
        _forestPatches = new GradientNoise(SplitMix64.Mix(seed, DetailSalt));
        _thickets = new GradientNoise(SplitMix64.Mix(seed, ThicketSalt));
        _ridges = new GradientNoise(SplitMix64.Mix(seed, RidgeSalt));
        _crags = new GradientNoise(SplitMix64.Mix(seed, CragSalt));
        _valley = new GradientNoise(SplitMix64.Mix(seed, ValleySalt));
        _bare = new GradientNoise(SplitMix64.Mix(seed, BareSalt));
        _fields = new GradientNoise(SplitMix64.Mix(seed, HedgeSalt));
        _gradeTangent = Mathf.Tan(Mathf.DegToRad(settings.Grade));
        _clearingSlopeTangent = Mathf.Tan(Mathf.DegToRad(settings.ClearingSlope));
        _summitCliffTangent = Mathf.Tan(Mathf.DegToRad(settings.SummitCliffSlope));
        _summitCapTangent = Mathf.Tan(Mathf.DegToRad(settings.SummitCapSlope));
        _startHeight = SpineHeight(settings.SpawnZ);
        EnsureStations(settings.PrecomputedStations);
        if (settings.SummitPadRadius > 0.0f)
        {
            float padZ = settings.SpawnZ + settings.SummitPadBehind;
            float padX = SpineX(padZ) + settings.SummitPadSide;
            float padY = Mathf.Min(Height(padX, padZ), SummitHeight);
            _summitPad = new Vector3(padX, padY, padZ);
            _hasSummitPad = true;
        }
    }

    /// <summary>Centre of the summit's level pad, on its ground (where the old car stands).</summary>
    public Vector3 SummitPad => _summitPad;

    /// <summary>True within <paramref name="margin"/> m of the summit pad's level ground (nothing is scattered there).</summary>
    public bool OnSummitPad(float x, float z, float margin)
    {
        if (!_hasSummitPad)
        {
            return false;
        }
        return new Vector2(x - _summitPad.X, z - _summitPad.Z).Length() < _settings.SummitPadRadius + margin;
    }

    /// <summary>Levels the ground on the summit pad, blending it into the slope around.</summary>
    private float ApplySummitPad(float x, float z, float height)
    {
        if (!_hasSummitPad)
        {
            return height;
        }
        float reach = _settings.SummitPadRadius + _settings.SummitPadBlend;
        float distance = new Vector2(x - _summitPad.X, z - _summitPad.Z).Length();
        if (distance >= reach)
        {
            return height;
        }
        float weight = 1.0f - Mathf.SmoothStep(_settings.SummitPadRadius, reach, distance);
        return Mathf.Lerp(height, _summitPad.Y, weight);
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
        // Outer paths: the long way round, wide of everything else, on either side (drawn after the
        // inner ones from the same stream, so the inner paths of a seed stay as they were).
        foreach (float side in new[] { firstSide, -firstSide })
        {
            if (random.NextFloat() < _settings.OuterBranchChance)
            {
                float offset = random.Range(_settings.OuterBranchOffset.X, _settings.OuterBranchOffset.Y);
                result.Add(new RouteBranch(RouteKind.SupplyRich, side, offset, split, join, BaseSpineX(split), BaseSpineX(join)));
            }
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
            // A level terrace (the camp stands flat), with gentle ramps in and out following the descent.
            float along = z - station.Z;
            float beyond = Mathf.Sign(along) * Mathf.Max(Mathf.Abs(along) - _settings.ClearingRadius, 0.0f);
            float terrace = station.Y + beyond * _clearingSlopeTangent;
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
        return Mathf.Min(ApplySummitPad(x, z, ApplyClearings(x, z, RawHeight(x, z))), SummitCap(z));
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
        float nearest = Mathf.Abs(x - SpineX(z));
        foreach (RouteBranch branch in BranchesAt(z))
        {
            if (z <= branch.SplitZ && z >= branch.JoinZ)
            {
                nearest = Mathf.Min(nearest, Mathf.Abs(x - BranchX(branch, z)));
            }
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
        wild += Flank(x, z, nearest);
        return Descent(z) + Mathf.Lerp(wild, path, strongest);
    }

    /// <summary>
    /// The third round's relief: away from every path the ground climbs into flanking ridges, so a
    /// path runs down a valley with crests, gullies and crags on either side instead of over open
    /// lawn. The valley opens and closes along the descent (open stretches give the views), and the
    /// circle's Relief scales it. Nothing changes within FlankStart of a path's centre.
    /// </summary>
    private float Flank(float x, float z, float nearest)
    {
        if (_settings.FlankHeight <= 0.0f || nearest <= _settings.FlankStart)
        {
            return 0.0f;
        }
        float rise = Mathf.SmoothStep(_settings.FlankStart, _settings.FlankStart + Mathf.Max(_settings.FlankRise, 1.0f), nearest);
        float open = Mathf.Clamp(0.62f + 0.9f * _valley.Sample(z / Mathf.Max(_settings.ValleyWavelength, 1.0f), LineOffset), 0.15f, 1.25f);
        float wave = Mathf.Max(_settings.RidgeWavelength, 1.0f);
        float crest = 1.0f - Mathf.Abs(_ridges.Fractal(x / wave, z / wave, 2));
        crest *= crest;
        float cragWave = Mathf.Max(_settings.CragWavelength, 1.0f);
        float crag = 1.0f - Mathf.Abs(_crags.Sample(x / cragWave, z / cragWave));
        float relief = ReliefAt(z);
        return relief * rise * (_settings.FlankHeight * open * (0.35f + 0.85f * crest) + _settings.CragAmplitude * crag * crag);
    }

    /// <summary>Distance from a point to the centre of the nearest path (spine or a branch in its zone), in m.</summary>
    public float NearestPathDistance(float x, float z)
    {
        float nearest = Mathf.Abs(x - SpineX(z));
        foreach (RouteBranch branch in BranchesAt(z))
        {
            if (z <= branch.SplitZ && z >= branch.JoinZ)
            {
                nearest = Mathf.Min(nearest, Mathf.Abs(x - BranchX(branch, z)));
            }
        }
        return nearest;
    }

    /// <summary>How far up the valley's flank a point is (0 by the path, 1 where the flank is full height).</summary>
    public float FlankRiseAt(float x, float z)
    {
        return Mathf.SmoothStep(_settings.FlankStart, _settings.FlankStart + Mathf.Max(_settings.FlankRise, 1.0f), NearestPathDistance(x, z));
    }

    /// <summary>
    /// The snow line off the paths: near the summit the flanks are snowier the higher up them, so the
    /// first circle reads as high mountain (the fourth round). Paths keep the plain threshold.
    /// </summary>
    private float SnowThresholdAt(float x, float z, float pathWeight)
    {
        float threshold = _settings.SnowThreshold;
        if (_settings.SummitSnow <= 0.0f || pathWeight > 0.5f)
        {
            return threshold;
        }
        float depth = Mathf.Max(0.0f, _startHeight - Descent(z));
        float high = 1.0f - Mathf.SmoothStep(_settings.SummitSnowDepth.X, _settings.SummitSnowDepth.Y, depth);
        if (high <= 0.0f)
        {
            return threshold;
        }
        return threshold - _settings.SummitSnow * high * (0.5f + 0.5f * FlankRiseAt(x, z));
    }

    /// <summary>
    /// Tree lines on the valley's flanks (the fourth round): groves of the circle's own trees on the
    /// gentler parts of the slopes and along the crests, with a bush or two at their feet, so a ridge
    /// has trees on its skyline instead of bare ground. Their own stream; the circle's tree density
    /// decides how wooded its flanks are.
    /// </summary>
    private void PlaceFlankTrees(TerrainChunkData data, Vector2I coord, Vector3 corner, float maxTrees)
    {
        if (_settings.FlankTreeDensity <= 0.0f || maxTrees <= 0.0f || _settings.FlankHeight <= 0.0f)
        {
            return;
        }
        SplitMix64 random = new(SplitMix64.Mix(SplitMix64.Mix(Seed, FlankTreeSalt), ChunkSalt(coord)));
        float size = _settings.ChunkSize;
        int tries = Mathf.RoundToInt(_settings.FlankTreeDensity * size * size / 1000.0f);
        float wave = Mathf.Max(_settings.FlankGroveWavelength, 1.0f);
        for (int n = 0; n < tries; n++)
        {
            float lx = random.Range(0.0f, size);
            float lz = random.Range(0.0f, size);
            float keep = random.NextFloat();
            float yaw = random.Range(-Mathf.Pi, Mathf.Pi);
            float pick = random.NextFloat();
            float tall = random.NextFloat();
            Vector2 lean = new(random.Range(-1.0f, 1.0f), random.Range(-1.0f, 1.0f));
            float shrubRoll = random.NextFloat();
            float shrubAngle = random.Range(-Mathf.Pi, Mathf.Pi);
            float shrubPick = random.NextFloat();
            float x = corner.X + lx;
            float z = corner.Z + lz;
            float rise = FlankRiseAt(x, z);
            if (rise < 0.15f)
            {
                continue;
            }
            float grove = Mathf.Clamp(0.45f + _forestPatches.Sample(x / wave + 7.1f, z / wave - 3.3f) * 1.6f, 0.0f, 1.0f);
            float ground = GridHeight(data, lx, lz);
            Biome? biome = BiomeAt(x, z, DepthAt(ground));
            if (biome == null || biome.TreeModels.Length == 0)
            {
                continue;
            }
            float wooded = Mathf.Clamp(biome.TreeDensity / 4.0f, 0.45f, 1.2f);
            float alpine = AlpineAt(z);
            if (keep >= grove * wooded * Mathf.Min(rise * 1.5f, 1.0f) * (1.0f - _settings.AlpineTreeThin * alpine))
            {
                continue;
            }
            float slope = GridSlope(data, lx, lz);
            if (slope > _settings.FlankTreeMaxSlope || NearStation(x, z, _settings.ClearingRadius + _settings.TreeStationGap) || OnSummitPad(x, z, 3.0f))
            {
                continue;
            }
            float surfaceWeight = PathWeight(x, z, out RouteKind kind);
            if (Classify(x, z, slope, surfaceWeight, kind) == TerrainSurface.Snow && biome.TreeModels[0].Contains("broadleaf"))
            {
                continue;
            }
            string model = biome.TreeModels[Mathf.Min((int)(pick * biome.TreeModels.Length), biome.TreeModels.Length - 1)];
            float height = Mathf.Lerp(biome.TreeHeight.X, biome.TreeHeight.Y, tall) * Mathf.Lerp(1.0f, _settings.AlpineTreeHeight, alpine);
            data.Plants.Add(new PlantData(PlantKind.Tree, model, new Vector3(x, ground - height * _settings.TreeSink, z), yaw, height,
                WindLean(lean, biome.TreeLean, alpine), true, Mathf.Clamp(height * biome.TrunkRadiusPerMetre, 0.035f, 0.4f)));
            if (biome.ShrubModels.Length > 0 && shrubRoll < 0.5f)
            {
                float reach = 1.2f + shrubRoll * 3.0f;
                float sx = lx + Mathf.Cos(shrubAngle) * reach;
                float sz = lz + Mathf.Sin(shrubAngle) * reach;
                if (sx >= 0.0f && sz >= 0.0f && sx < size && sz < size)
                {
                    AddCover(data, PlantKind.Shrub, Pick(biome.ShrubModels, shrubPick), corner, sx, sz, shrubAngle,
                        Mathf.Lerp(biome.ShrubHeight.X, biome.ShrubHeight.Y, shrubPick));
                }
            }
        }
    }

    /// <summary>The circles' Relief at a point of the descent, blended between circles.</summary>
    public float ReliefAt(float z)
    {
        if (_settings.Biomes.Length == 0)
        {
            return 1.0f;
        }
        BiomesAt(Mathf.Max(0.0f, _startHeight - Descent(z)), out int from, out int to, out float blend);
        return Mathf.Lerp(_settings.Biomes[from].Relief, _settings.Biomes[to].Relief, blend);
    }

    private float StoninessAt(float depth)
    {
        if (_settings.Biomes.Length == 0)
        {
            return 0.5f;
        }
        BiomesAt(depth, out int from, out int to, out float blend);
        return Mathf.Lerp(_settings.Biomes[from].Stoniness, _settings.Biomes[to].Stoniness, blend);
    }

    /// <summary>
    /// How bare open grass is here (0 green, 1 bare earth or gravel): patches by noise, more of them
    /// on steeper ground and in stony circles, none on a path's band (the path is worn its own way).
    /// </summary>
    public float Bareness(float x, float z, float slope, float pathWeight, float depth)
    {
        if (_settings.BareShare <= 0.0f || pathWeight > 0.9f)
        {
            return 0.0f;
        }
        float wave = Mathf.Max(_settings.BareWavelength, 1.0f);
        float patches = _bare.Fractal(x / wave, z / wave, 3) * 0.5f + 0.5f;
        float stony = StoninessAt(depth);
        float steep = Mathf.SmoothStep(12.0f, 34.0f, slope);
        float level = patches + _settings.BareShare * (0.55f + 0.7f * stony) + steep * 0.45f - 0.95f + _settings.AlpineBare * AlpineAt(z);
        float bare = Mathf.SmoothStep(0.0f, 0.18f, level);
        // Thins out towards the path's edge so the worn band keeps its own clear shape.
        return bare * (1.0f - Mathf.SmoothStep(0.55f, 0.9f, pathWeight));
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
        float snowThreshold = SnowThresholdAt(x, z, pathWeight);
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
            float drop = _settings.AlpineRockDrop * AlpineAt(z);
            if (slope >= _settings.RockSlope - drop)
            {
                return TerrainSurface.Rock;
            }
            if (slope >= _settings.ScreeSlope - drop)
            {
                if (_scree.Sample(x * scale, z * scale) > -0.1f)
                {
                    return TerrainSurface.Scree;
                }
                return TerrainSurface.Rock;
            }
            if (slope <= _settings.MudMaxSlope && _mud.Sample(x * scale, z * scale) > _settings.MudThreshold - _settings.WetHollowDrop * BiomeValueAt(z, BiomeWetness))
            {
                return TerrainSurface.Mud;
            }
            if (InScreeFan(x, z, slope))
            {
                return TerrainSurface.Scree;
            }
        }
        if (_snow.Sample(x * scale, z * scale) > snowThreshold)
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
        Vector3[] smoothNormals = System.Array.Empty<Vector3>();
        Color[] smoothColors = System.Array.Empty<Color>();
        if (_settings.SmoothShading)
        {
            // Adjacent triangles have identical smooth normals/colours at a grid point.
            // Share those vertices instead of allocating three large duplicate attribute arrays.
            data.Vertices = new Vector3[points * points];
            smoothNormals = new Vector3[points * points];
            smoothColors = new Color[points * points];
            Vector2[] wear = new Vector2[points * points];
            data.Wear = wear;
            data.CarpetMask = new byte[points * points * 4];
            for (int j = 0; j < points; j++)
            {
                for (int i = 0; i < points; i++)
                {
                    float x = corner.X + i * cell;
                    float z = corner.Z + j * cell;
                    data.Vertices[i + j * points] = new Vector3(i * cell, data.Heights[i + j * points], j * cell);
                    float left = GridOrWorld(data, i - 1, j, corner);
                    float right = GridOrWorld(data, i + 1, j, corner);
                    float back = GridOrWorld(data, i, j - 1, corner);
                    float front = GridOrWorld(data, i, j + 1, corner);
                    Vector3 normal = new Vector3(left - right, 2 * cell, back - front).Normalized();
                    smoothNormals[i + j * points] = normal;
                    float slope = Mathf.RadToDeg(normal.AngleTo(Vector3.Up));
                    Color ground = GroundColor(new Vector3(x, data.Heights[i + j * points], z), slope, surfaceColors,
                        out TerrainSurface surface, out float pathWeight, out float snowCover, out float bare, out Vector2 worn);
                    wear[i + j * points] = worn;
                    // The ground beneath, with the snow lying on it in alpha (1: none): the ground shader
                    // lays the snow on with a ragged edge finer than this grid.
                    smoothColors[i + j * points] = new Color(ground.R, ground.G, ground.B, 1.0f - snowCover);
                    WriteCarpet(data.CarpetMask, (i + j * points) * 4, x, z, ground, slope, surface, pathWeight, snowCover, bare);
                }
            }
            data.Indices = SmoothIndices(cells);
            data.Normals = smoothNormals;
            data.Colors = smoothColors;
        }
        else
        {
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
                    v = AddTriangle(vertices, normals, colors, v, a, b, d, corner, surfaceColors, jitter,
                        smoothNormals, smoothColors, new Vector3I(i + j * points, i + 1 + j * points, i + 1 + (j + 1) * points));
                    v = AddTriangle(vertices, normals, colors, v, a, d, c, corner, surfaceColors, jitter,
                        smoothNormals, smoothColors, new Vector3I(i + j * points, i + 1 + (j + 1) * points, i + (j + 1) * points));
                }
            }
            data.Vertices = vertices;
            data.Normals = normals;
            data.Colors = colors;
        }

        PlaceObstacles(data, coord, corner);
        PlacePlants(data, coord, corner);
        PlacePebbles(data, coord, corner);
        PlaceGroundCover(data, coord, corner);
        NoteFogCover(data);
        data.BuildMilliseconds = timer.Elapsed.TotalMilliseconds;
        return data;
    }

    private int[] SmoothIndices(int cells)
    {
        lock (_indexLock)
        {
            if (_indexCells == cells) { return _smoothIndices; }
            int points = cells + 1;
            int[] indices = new int[cells * cells * 6];
            int corner = 0;
            for (int j = 0; j < cells; j++)
            {
                for (int i = 0; i < cells; i++)
                {
                    int a = i + j * points;
                    int b = a + 1;
                    int c = a + points;
                    int d = c + 1;
                    // Same diagonal, corner order and winding as the unindexed flat path.
                    indices[corner++] = a; indices[corner++] = b; indices[corner++] = d;
                    indices[corner++] = a; indices[corner++] = d; indices[corner++] = c;
                }
            }
            _smoothIndices = indices;
            _indexCells = cells;
            return indices;
        }
    }

    private int AddTriangle(Vector3[] vertices, Vector3[] normals, Color[] colors, int v,
        Vector3 p0, Vector3 p1, Vector3 p2, Vector3 corner, Color[] surfaceColors, SplitMix64 jitter,
        Vector3[] smoothNormals, Color[] smoothColors, Vector3I gridIndices)
    {
        vertices[v] = p0;
        vertices[v + 1] = p1;
        vertices[v + 2] = p2;
        if (smoothNormals.Length > 0)
        {
            for (int k = 0; k < 3; k++)
            {
                normals[v + k] = smoothNormals[gridIndices[k]];
                colors[v + k] = smoothColors[gridIndices[k]];
            }
        }
        else
        {
            Vector3 normal = (p2 - p0).Cross(p1 - p0).Normalized();
            Vector3 centre = (p0 + p1 + p2) / 3.0f + corner;
            Color color = GroundColor(centre, Mathf.RadToDeg(normal.AngleTo(Vector3.Up)), surfaceColors);
            float brightness = 1.0f + jitter.Range(-_settings.ColorJitter, _settings.ColorJitter);
            color = new Color(color.R * brightness, color.G * brightness, color.B * brightness);
            for (int k = 0; k < 3; k++)
            {
                normals[v + k] = normal;
                colors[v + k] = color;
            }
        }
        return v + 3;
    }

    private float GridOrWorld(TerrainChunkData data, int x, int z, Vector3 corner)
    {
        if (x >= 0 && z >= 0 && x < data.GridPoints && z < data.GridPoints) { return data.Heights[x + z * data.GridPoints]; }
        return Height(corner.X + x * _settings.CellSize, corner.Z + z * _settings.CellSize);
    }

    private Color GroundColor(Vector3 centre, float slope, Color[] surfaceColors)
    {
        return GroundColor(centre, slope, surfaceColors, out _, out _);
    }

    /// <summary>
    /// The colour of the ground under any snow, and how much snow lies there (0 none, 1 deep; 0.5 is
    /// where the surface turns to snow). Snow only ever lies on what would be grass, or on a hazardous
    /// path's scree, so those are the only places with any.
    /// </summary>
    private Color GroundColor(Vector3 centre, float slope, Color[] surfaceColors, out TerrainSurface surface, out float pathWeight, out float snowCover)
    {
        return GroundColor(centre, slope, surfaceColors, out surface, out pathWeight, out snowCover, out _, out _);
    }

    /// <summary>
    /// The ground colour with the third round's wear on it: a safe or supply path's band is trodden
    /// earth with a clear edge, and open grass gives way to patches of bare earth (green circles) or
    /// grey gravel (high, stony ones and steep ground). <paramref name="bare"/> says how bare it is.
    /// </summary>
    private Color GroundColor(Vector3 centre, float slope, Color[] surfaceColors, out TerrainSurface surface, out float pathWeight, out float snowCover, out float bare, out Vector2 worn)
    {
        Color color = BaseGroundColor(centre, slope, surfaceColors, out surface, out pathWeight, out snowCover, out RouteKind kind);
        bare = 0.0f;
        bool hazardPath = pathWeight > 0.5f && kind == RouteKind.Hazardous;
        bool grassy = surface == TerrainSurface.Grass || (surface == TerrainSurface.Snow && !hazardPath);
        worn = Vector2.Zero;
        if (surface == TerrainSurface.Rock || surface == TerrainSurface.Scree || hazardPath)
        {
            worn.X = 1.0f;
        }
        if (!grassy)
        {
            return color;
        }
        float depth = DepthAt(centre.Y);
        Color tint = Colors.White.Lerp(BiomeTint(depth), 0.6f);
        float wear = Mathf.SmoothStep(0.9f, 0.99f, pathWeight) * _settings.PathWear;
        if (wear > 0.0f)
        {
            float scale = 1.0f / Mathf.Max(_settings.BareWavelength * 0.3f, 1.0f);
            float mottle = 0.88f + 0.24f * (_bare.Sample(centre.X * scale + 17.3f, centre.Z * scale) * 0.5f + 0.5f);
            Color trodden = new(_settings.PathColour.R * tint.R * mottle, _settings.PathColour.G * tint.G * mottle, _settings.PathColour.B * tint.B * mottle);
            color = color.Lerp(trodden, wear);
            worn.Y = wear;
            // Gravel shows through the trodden earth in high, stony circles.
            worn.X = wear * 0.55f * StoninessAt(depth);
        }
        bare = Bareness(centre.X, centre.Z, slope, pathWeight, depth);
        if (bare > 0.0f)
        {
            float stony = Mathf.Clamp(StoninessAt(depth) + (slope - 20.0f) / 40.0f, 0.0f, 1.0f);
            Color ground = _settings.BareEarthColour.Lerp(_settings.GravelColour, stony);
            ground = new Color(ground.R * tint.R, ground.G * tint.G, ground.B * tint.B);
            color = color.Lerp(ground, bare * 0.85f);
            worn.X = Mathf.Max(worn.X, bare * stony);
        }
        return color;
    }

    private Color BaseGroundColor(Vector3 centre, float slope, Color[] surfaceColors, out TerrainSurface surface, out float pathWeight, out float snowCover, out RouteKind kind)
    {
        pathWeight = PathWeight(centre.X, centre.Z, out kind);
        surface = Classify(centre.X, centre.Z, slope, pathWeight, kind);
        Color color = SurfaceColor(surface, centre.Y, surfaceColors);
        snowCover = 0.0f;
        bool hazardPath = pathWeight > 0.5f && kind == RouteKind.Hazardous;
        TerrainSurface beneath = TerrainSurface.Grass;
        if (hazardPath)
        {
            beneath = TerrainSurface.Scree;
        }
        if (surface != TerrainSurface.Snow && surface != beneath)
        {
            return color;
        }
        float scale = 1.0f / _settings.SurfaceNoiseWavelength;
        float snow = _snow.Sample(centre.X * scale, centre.Z * scale);
        float band = Mathf.Max(_settings.SnowEdgeBand, 0.001f);
        snowCover = Mathf.Clamp((snow - SnowThresholdAt(centre.X, centre.Z, pathWeight)) / (2.0f * band) + 0.5f, 0.0f, 1.0f);
        if (surface == TerrainSurface.Snow)
        {
            color = SurfaceColor(beneath, centre.Y, surfaceColors);
        }
        return color;
    }

    private Color GroundColor(Vector3 centre, float slope, Color[] surfaceColors, out TerrainSurface surface, out float pathWeight)
    {
        pathWeight = PathWeight(centre.X, centre.Z, out RouteKind kind);
        surface = Classify(centre.X, centre.Z, slope, pathWeight, kind);
        return SurfaceColor(surface, centre.Y, surfaceColors);
    }

    private Color SurfaceColor(TerrainSurface surface, float height, Color[] surfaceColors)
    {
        Color color = surfaceColors[(int)surface];
        // The biome tints the ground: grass and mud fully, snow and rock half as much.
        Color tint = BiomeTint(DepthAt(height));
        if (surface != TerrainSurface.Grass && surface != TerrainSurface.Mud)
        {
            tint = Colors.White.Lerp(tint, 0.5f);
        }
        color = new Color(color.R * tint.R, color.G * tint.G, color.B * tint.B);
        return color;
    }

    /// <summary>
    /// The grass carpet at one grid point (RGBA8): the ground's colour there, and how thick the carpet
    /// grows (alpha). Full on grassy ground, thinning on mud and scree, none on rock, snow, the summit
    /// pad or a station's terrace, and stopping at a path's trodden band with a crisp edge.
    /// </summary>
    private void WriteCarpet(byte[] mask, int at, float x, float z, Color ground, float slope, TerrainSurface surface, float pathWeight, float snowCover, float bare)
    {
        float density = 0.0f;
        if (surface == TerrainSurface.Grass)
        {
            density = 1.0f;
        }
        else if (surface == TerrainSurface.Mud)
        {
            density = 0.3f;
        }
        else if (surface == TerrainSurface.Scree)
        {
            density = 0.15f;
        }
        density *= 1.0f - Mathf.SmoothStep(0.985f, 0.999f, pathWeight);
        density *= 1.0f - Mathf.SmoothStep(34.0f, 42.0f, slope);
        // The grass thins out before a snow patch's ragged edge, so only the odd blade stands in the snow.
        density *= 1.0f - Mathf.SmoothStep(0.22f, 0.46f, snowCover);
        // Bare earth and gravel keep only the odd tuft.
        density *= 1.0f - 0.92f * Mathf.SmoothStep(0.2f, 0.75f, bare);
        if (density > 0.0f && (OnSummitPad(x, z, 0.5f) || NearStation(x, z, _settings.ClearingRadius * 0.85f)))
        {
            density = 0.0f;
        }
        mask[at] = (byte)Mathf.Clamp(Mathf.RoundToInt(ground.R * 255.0f), 0, 255);
        mask[at + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(ground.G * 255.0f), 0, 255);
        mask[at + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(ground.B * 255.0f), 0, 255);
        mask[at + 3] = (byte)Mathf.Clamp(Mathf.RoundToInt(density * 255.0f), 0, 255);
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
        int boulders = Mathf.RoundToInt(_settings.BoulderDensity * area * Mathf.Clamp(_settings.ScatteredBoulderShare, 0.0f, 1.0f));
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

            if (TooCloseToPath(x, z, extent) || OnSummitPad(x, z, extent + 1.0f))
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
        PlaceBoulderGroups(data, coord, corner);
    }

    /// <summary>
    /// The third round's boulder groups: one big "hero" rock with smaller ones gathered round it
    /// (instead of every boulder on its own), more of them in rugged circles, and now and then a lone
    /// dead tree standing on open ground. Kept off paths, stations and the summit pad like the rest.
    /// </summary>
    private void PlaceBoulderGroups(TerrainChunkData data, Vector2I coord, Vector3 corner)
    {
        SplitMix64 random = new(SplitMix64.Mix(SplitMix64.Mix(Seed, ClusterSalt), ChunkSalt(coord)));
        float size = _settings.ChunkSize;
        float area = size * size / 1000.0f;
        float relief = ReliefAt(corner.Z + size * 0.5f);
        float expected = Mathf.Max(_settings.BoulderClusterDensity, 0.0f) * area * relief;
        int groups = (int)expected;
        if (random.NextFloat() < expected - groups)
        {
            groups++;
        }
        float maxTilt = Mathf.DegToRad(_settings.BoulderMaxTilt) * 0.5f;
        for (int g = 0; g < groups; g++)
        {
            float x = corner.X + random.Range(0.0f, size);
            float z = corner.Z + random.Range(0.0f, size);
            float sightRoll = random.NextFloat();
            float sightSide = random.NextFloat() < 0.5f ? -1.0f : 1.0f;
            float sightReach = random.Range(_settings.GroupSightDistance.X, _settings.GroupSightDistance.Y);
            float spireRoll = random.NextFloat();
            Vector3 hero = new(
                random.Range(_settings.HeroBoulderSize.X, _settings.HeroBoulderSize.Y),
                random.Range(_settings.HeroBoulderSize.X, _settings.HeroBoulderSize.Y) * 0.8f,
                random.Range(_settings.HeroBoulderSize.X, _settings.HeroBoulderSize.Y));
            // Most groups stand within sight of the main path (beside it, not on it).
            if (sightRoll < _settings.GroupNearPathShare)
            {
                x = SpineX(z) + sightSide * sightReach;
                if (x < corner.X || x >= corner.X + size)
                {
                    continue;
                }
            }
            // Now and then the hero is a tall standing spire: a tor that stands against the sky.
            if (spireRoll < _settings.SpireShare)
            {
                float girth = Mathf.Min(hero.X, hero.Z) * 0.6f;
                hero = new Vector3(girth, random.Range(_settings.SpireHeight.X, _settings.SpireHeight.Y), girth * 0.85f);
            }
            float yaw = random.Range(-Mathf.Pi, Mathf.Pi);
            float tiltX = random.Range(-maxTilt, maxTilt);
            float tiltZ = random.Range(-maxTilt, maxTilt);
            int low = Mathf.Max(_settings.ClusterBoulders.X, 0);
            int count = low + random.NextInt(Mathf.Max(_settings.ClusterBoulders.Y - low + 1, 1));
            float heroRadius = Mathf.Max(hero.X, hero.Z) * 0.5f;
            bool spire = hero.Y > Mathf.Max(hero.X, hero.Z) * 1.5f;
            if (TooCloseToPath(x, z, heroRadius + 1.5f) || OnSummitPad(x, z, heroRadius + 2.0f) || NearStation(x, z, _settings.ClearingRadius + heroRadius + 6.0f))
            {
                continue;
            }
            float ground = Height(x, z);
            // Sunk deeper than a lone boulder, so a big rock looks bedded into the hillside.
            if (spire)
            {
                // Upright, barely leaning.
                tiltX *= 0.25f;
                tiltZ *= 0.25f;
            }
            data.Obstacles.Add(new ObstacleData(ObstacleKind.Boulder, new Vector3(x, ground + hero.Y * (spire ? 0.38f : 0.2f), z), hero,
                Basis.FromEuler(new Vector3(tiltX, yaw, tiltZ))));
            for (int k = 0; k < count; k++)
            {
                float angle = random.Range(-Mathf.Pi, Mathf.Pi);
                float reach = heroRadius * random.Range(0.8f, 1.6f) + 0.6f;
                float scale = random.Range(0.18f, 0.5f);
                float heroLow = Mathf.Min(hero.Y, Mathf.Max(hero.X, hero.Z) * 1.2f);
                Vector3 shape = new(
                    Mathf.Max(Mathf.Max(hero.X, hero.Z) * scale * random.Range(0.8f, 1.2f), 0.6f),
                    Mathf.Max(heroLow * scale * random.Range(0.7f, 1.1f), 0.5f),
                    Mathf.Max(Mathf.Max(hero.X, hero.Z) * scale * random.Range(0.8f, 1.2f), 0.6f));
                float stoneYaw = random.Range(-Mathf.Pi, Mathf.Pi);
                float sx = x + Mathf.Cos(angle) * reach;
                float sz = z + Mathf.Sin(angle) * reach;
                if (TooCloseToPath(sx, sz, shape.Length() * 0.5f) || OnSummitPad(sx, sz, 2.0f))
                {
                    continue;
                }
                float stoneGround = Height(sx, sz);
                data.Obstacles.Add(new ObstacleData(ObstacleKind.Boulder, new Vector3(sx, stoneGround + shape.Y * (0.5f - _settings.BoulderSink), sz), shape,
                    Basis.FromEuler(new Vector3(random.Range(-maxTilt, maxTilt) * 2.0f, stoneYaw, random.Range(-maxTilt, maxTilt) * 2.0f))));
            }
        }

        // A lone dead tree on open ground: something to steer by, standing against the sky.
        float roll = random.NextFloat();
        float tz = corner.Z + random.Range(0.0f, size);
        float tx = SpineX(tz) + (random.NextFloat() < 0.5f ? -1.0f : 1.0f) * random.Range(_settings.GroupSightDistance.X, _settings.GroupSightDistance.Y);
        float tall = random.NextFloat();
        float treeYaw = random.Range(-Mathf.Pi, Mathf.Pi);
        Vector2 lean = new(random.Range(-1.0f, 1.0f), random.Range(-1.0f, 1.0f));
        string model = "res://art/environment/forest/snag_" + (random.NextFloat() < 0.5f ? "0" : "1") + ".tscn";
        if (roll < _settings.LoneTreeChance && tx >= corner.X && tx < corner.X + size && !TooCloseToPath(tx, tz, 3.0f) && !NearStation(tx, tz, _settings.ClearingRadius + _settings.TreeStationGap)
            && !OnSummitPad(tx, tz, 4.0f) && SlopeDegrees(tx, tz) < 30.0f)
        {
            float height = Mathf.Lerp(_settings.LoneTreeHeight.X, _settings.LoneTreeHeight.Y, tall);
            float treeGround = Height(tx, tz);
            data.Plants.Add(new PlantData(PlantKind.Tree, model, new Vector3(tx, treeGround - height * _settings.TreeSink, tz), treeYaw, height,
                lean * Mathf.DegToRad(6.0f), true, Mathf.Clamp(height * 0.02f, 0.1f, 0.35f)));
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
            if (biome == null || biome.TreeModels.Length == 0)
            {
                continue;
            }
            float densityFactor = 1;
            if (biome.ClusterTrees)
            {
                float wave = Mathf.Max(_settings.ForestClusterWavelength, 1);
                densityFactor = Mathf.Clamp(0.55f + _forestPatches.Sample(x / wave, z / wave) * 1.8f, 0.1f, 1);
            }
            float alpine = AlpineAt(z);
            if (keep * maxTrees >= biome.TreeDensity * densityFactor * (1.0f - _settings.AlpineTreeThin * alpine)) { continue; }
            if (TooCloseToPath(x, z, 2.0f + _settings.AlpinePathGap * alpine) || NearStation(x, z, _settings.ClearingRadius + _settings.TreeStationGap) || OnSummitPad(x, z, 3.0f) || SlopeDegrees(x, z) > _settings.TreeMaxSlope)
            {
                continue;
            }
            string model = biome.TreeModels[Mathf.Min((int)(pick * biome.TreeModels.Length), biome.TreeModels.Length - 1)];
            float height = Mathf.Lerp(biome.TreeHeight.X, biome.TreeHeight.Y, tall);
            if (biome.SaplingShare > 0 && tall < biome.SaplingShare)
            {
                height = Mathf.Lerp(biome.SaplingHeight.X, biome.SaplingHeight.Y, tall / biome.SaplingShare);
            }
            else
            {
                height *= Mathf.Lerp(1.0f, _settings.AlpineTreeHeight, alpine);
            }
            // The trunk sinks a share of its height into the ground, so no root hangs in the air on a slope.
            data.Plants.Add(new PlantData(PlantKind.Tree, model, new Vector3(x, ground - height * _settings.TreeSink, z), yaw, height,
                WindLean(lean, biome.TreeLean, alpine), true, Mathf.Clamp(height * biome.TrunkRadiusPerMetre, 0.035f, 0.4f)));
        }

        PlaceFlankTrees(data, coord, corner, maxTrees);
        PlaceGullyShrubs(data, coord, corner);
        PlaceHedgerows(data, coord, corner);
        PlaceReeds(data, coord, corner);

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
                float tuftSlope = GridSlope(data, lx, lz);
                if (pathWeight > 0.85f || OnSummitPad(x, z, 0.5f) || Classify(x, z, tuftSlope, 0.0f, route) != TerrainSurface.Grass
                    || Bareness(x, z, tuftSlope, pathWeight, DepthAt(ground)) > 0.45f)
                {
                    continue;
                }
                string model = models[Mathf.Min((int)(pick * models.Length), models.Length - 1)];
                float s = Mathf.Lerp(biome.GrassScale.X, biome.GrassScale.Y, scale);
                data.Plants.Add(new PlantData(kind, model, new Vector3(x, ground, z), yaw, s, Vector2.Zero, biome.GroundPlantsInMetres));
            }
        }
    }

    private void PlacePebbles(TerrainChunkData data, Vector2I coord, Vector3 corner)
    {
        if (_settings.PebbleModels.Length == 0 || _settings.PebbleDensity <= 0) { return; }
        SplitMix64 random = new(SplitMix64.Mix(SplitMix64.Mix(Seed, DetailSalt), ChunkSalt(coord)));
        float size = _settings.ChunkSize;
        int count = Mathf.Min(Mathf.RoundToInt(size * size * _settings.PebbleDensity), 4096);
        for (int i = 0; i < count; i++)
        {
            float lx = random.Range(0, size);
            float lz = random.Range(0, size);
            float x = corner.X + lx;
            float z = corner.Z + lz;
            float slope = GridSlope(data, lx, lz);
            if (slope > _settings.DetailMaxSlope) { continue; }
            float height = GridHeight(data, lx, lz);
            TerrainSurface surface = SurfaceAt(x, z);
            if (surface == TerrainSurface.Snow) { continue; }
            // Leave the trodden centre clear, but show stones on shoulders and exposed off-trail ground.
            if (PathWeight(x, z, out _) > 0.9f || OnSummitPad(x, z, 0.5f)) { continue; }
            string model = _settings.PebbleModels[random.NextInt(_settings.PebbleModels.Length)];
            data.Plants.Add(new PlantData(PlantKind.Pebble, model, new Vector3(x, height, z), random.Range(-Mathf.Pi, Mathf.Pi),
                random.Range(_settings.PebbleHeight.X, _settings.PebbleHeight.Y), Vector2.Zero, true));
        }
    }

    /// <summary>
    /// Ground cover, from its own seeded stream: thickets of shrubs, loose rocks (thick on scree and
    /// rocky slopes, broken pieces round each boulder), debris at trees' feet (stumps, mushrooms,
    /// branches), things lying at the paths' edges, meadows of flowers and a few solid fallen logs.
    /// Everything but the logs is only seen. Heights come from the chunk's grid, so it all sits on the
    /// rendered ground; none of it grows on a path's band, a station's terrace or the summit pad.
    /// </summary>
    private void PlaceGroundCover(TerrainChunkData data, Vector2I coord, Vector3 corner)
    {
        if (_settings.Biomes.Length == 0)
        {
            return;
        }
        SplitMix64 random = new(SplitMix64.Mix(SplitMix64.Mix(Seed, GroundCoverSalt), ChunkSalt(coord)));
        float size = _settings.ChunkSize;
        float area = size * size / 1000.0f;
        float maxShrubs = 0.0f;
        float maxDebris = 0.0f;
        float maxRocks = 0.0f;
        float maxMeadow = 0.0f;
        float maxLogs = 0.0f;
        foreach (Biome biome in _settings.Biomes)
        {
            maxShrubs = Mathf.Max(maxShrubs, biome.ShrubDensity);
            maxDebris = Mathf.Max(maxDebris, biome.DebrisDensity);
            maxRocks = Mathf.Max(maxRocks, biome.RockDensity);
            maxMeadow = Mathf.Max(maxMeadow, biome.MeadowFlowerDensity);
            maxLogs = Mathf.Max(maxLogs, biome.FallenLogDensity);
        }
        float wave = Mathf.Max(_settings.ThicketWavelength, 1.0f);
        int treeCount = 0;
        for (int i = 0; i < data.Plants.Count; i++)
        {
            if (data.Plants[i].Kind == PlantKind.Tree)
            {
                treeCount++;
            }
        }

        // Shrubs in thickets: a few bushes or dwarf pines together where the thicket noise is high.
        float shrubScatter = Mathf.Max(_settings.ShrubScatter, 0.0f);
        int shrubTries = Mathf.RoundToInt(maxShrubs * area * shrubScatter);
        for (int n = 0; n < shrubTries; n++)
        {
            float lx = random.Range(0.0f, size);
            float lz = random.Range(0.0f, size);
            float keep = random.NextFloat() * maxShrubs;
            float x = corner.X + lx;
            float z = corner.Z + lz;
            float thicket = Mathf.Clamp(0.3f + _thickets.Sample(x / wave, z / wave) * 1.8f, 0.0f, 1.5f);
            float ground = GridHeight(data, lx, lz);
            Biome? biome = BiomeAt(x, z, DepthAt(ground));
            if (biome == null || biome.ShrubModels.Length == 0 || keep >= biome.ShrubDensity * thicket)
            {
                continue;
            }
            int group = 1 + random.NextInt(3);
            for (int k = 0; k < group; k++)
            {
                float gx = lx;
                float gz = lz;
                if (k > 0)
                {
                    float angle = random.Range(-Mathf.Pi, Mathf.Pi);
                    float reach = random.Range(0.6f, 1.6f);
                    gx += Mathf.Cos(angle) * reach;
                    gz += Mathf.Sin(angle) * reach;
                }
                float tall = random.NextFloat();
                float yaw = random.Range(-Mathf.Pi, Mathf.Pi);
                string model = Pick(biome.ShrubModels, random.NextFloat());
                if (!CoverFits(data, gx, gz, 0.6f, 32.0f, out TerrainSurface surface, out _) || (surface != TerrainSurface.Grass && surface != TerrainSurface.Mud))
                {
                    continue;
                }
                float height = Mathf.Lerp(biome.ShrubHeight.X, biome.ShrubHeight.Y, tall * tall);
                if (k > 0)
                {
                    height *= 0.75f;
                }
                AddCover(data, PlantKind.Shrub, model, corner, gx, gz, yaw, height);
            }
        }

        // Loose rocks: a scatter everywhere, several times thicker on scree and rocky ground, and
        // poking out of the snow here and there.
        float rockyFactor = Mathf.Max(_settings.RockySurfaceRockFactor, 1.0f);
        int rockTries = Mathf.RoundToInt(maxRocks * rockyFactor * area * Mathf.Max(_settings.RockScatter, 0.0f));
        if (_settings.RockModels.Length > 0)
        {
            for (int n = 0; n < rockTries; n++)
            {
                float lx = random.Range(0.0f, size);
                float lz = random.Range(0.0f, size);
                float keep = random.NextFloat() * maxRocks * rockyFactor;
                float big = random.NextFloat();
                float yaw = random.Range(-Mathf.Pi, Mathf.Pi);
                string model = Pick(_settings.RockModels, random.NextFloat());
                float x = corner.X + lx;
                float z = corner.Z + lz;
                Biome? biome = BiomeAt(x, z, DepthAt(GridHeight(data, lx, lz)));
                if (biome == null || keep >= biome.RockDensity * rockyFactor)
                {
                    continue;
                }
                if (!CoverFits(data, lx, lz, 0.4f, 55.0f, out TerrainSurface surface, out float pathWeight) || pathWeight > 0.3f)
                {
                    continue;
                }
                float share = 1.0f / rockyFactor;
                if (surface == TerrainSurface.Rock || surface == TerrainSurface.Scree)
                {
                    share = 1.0f;
                }
                else if (surface == TerrainSurface.Snow)
                {
                    share = 0.5f / rockyFactor;
                }
                if (keep >= biome.RockDensity * rockyFactor * share)
                {
                    continue;
                }
                // Mostly fist-sized stones, a fair few knee-high, the odd one to sit on.
                float height = Mathf.Lerp(_settings.RockHeight.X, _settings.RockHeight.Y, big * big);
                AddCover(data, PlantKind.Rock, model, corner, lx, lz, yaw, height);
            }
            // Broken pieces round each boulder's foot, so boulders sit in the ground instead of on it.
            for (int b = 0; b < data.Obstacles.Count; b++)
            {
                ObstacleData boulder = data.Obstacles[b];
                if (boulder.Kind != ObstacleKind.Boulder)
                {
                    continue;
                }
                int pieces = _settings.BoulderFragments.X + random.NextInt(Mathf.Max(_settings.BoulderFragments.Y - _settings.BoulderFragments.X + 1, 1));
                float radius = Mathf.Max(boulder.Size.X, boulder.Size.Z) * 0.55f;
                for (int k = 0; k < pieces; k++)
                {
                    float angle = random.Range(-Mathf.Pi, Mathf.Pi);
                    float reach = radius + random.Range(0.1f, 0.9f);
                    float lx = boulder.Position.X + Mathf.Cos(angle) * reach - corner.X;
                    float lz = boulder.Position.Z + Mathf.Sin(angle) * reach - corner.Z;
                    float height = random.Range(0.1f, 0.35f) * Mathf.Clamp(boulder.Size.Y, 0.6f, 2.0f);
                    float yaw = random.Range(-Mathf.Pi, Mathf.Pi);
                    string model = Pick(_settings.RockModels, random.NextFloat());
                    if (!CoverFits(data, lx, lz, 0.3f, 55.0f, out _, out _))
                    {
                        continue;
                    }
                    AddCover(data, PlantKind.Rock, model, corner, lx, lz, yaw, height);
                }
            }
        }

        // Debris: most of it at trees' feet (stumps, mushrooms, fallen branches), some in the open.
        int debrisTries = Mathf.RoundToInt(maxDebris * area);
        for (int n = 0; n < debrisTries; n++)
        {
            float lx = random.Range(0.0f, size);
            float lz = random.Range(0.0f, size);
            float keep = random.NextFloat() * maxDebris;
            float underTree = random.NextFloat();
            float pickTree = random.NextFloat();
            float angle = random.Range(-Mathf.Pi, Mathf.Pi);
            float reach = random.Range(0.5f, 2.2f);
            float tall = random.NextFloat();
            float yaw = random.Range(-Mathf.Pi, Mathf.Pi);
            float pick = random.NextFloat();
            if (treeCount > 0 && underTree < 0.65f)
            {
                int target = Mathf.Min((int)(pickTree * treeCount), treeCount - 1);
                for (int i = 0; i < data.Plants.Count; i++)
                {
                    if (data.Plants[i].Kind != PlantKind.Tree)
                    {
                        continue;
                    }
                    if (target == 0)
                    {
                        Vector3 foot = data.Plants[i].Position;
                        lx = foot.X - corner.X + Mathf.Cos(angle) * reach;
                        lz = foot.Z - corner.Z + Mathf.Sin(angle) * reach;
                        break;
                    }
                    target--;
                }
            }
            float x = corner.X + lx;
            float z = corner.Z + lz;
            if (lx < 0.0f || lz < 0.0f || lx >= size || lz >= size)
            {
                continue;
            }
            Biome? biome = BiomeAt(x, z, DepthAt(GridHeight(data, lx, lz)));
            if (biome == null || biome.DebrisModels.Length == 0 || keep >= biome.DebrisDensity)
            {
                continue;
            }
            if (!CoverFits(data, lx, lz, 0.4f, 34.0f, out TerrainSurface surface, out _) || surface == TerrainSurface.Snow)
            {
                continue;
            }
            string model = Pick(biome.DebrisModels, pick);
            AddCover(data, PlantKind.Debris, model, corner, lx, lz, yaw, DetailHeight(model, tall));
        }

        PlaceWayside(data, corner, random);

        // Meadows: flowers gathered in patches on open grass (the thin scatter everywhere stays as it was).
        if (maxMeadow > 0.0f)
        {
            int flowerTries = Mathf.RoundToInt(maxMeadow * size * size * 0.45f);
            for (int n = 0; n < flowerTries; n++)
            {
                float lx = random.Range(0.0f, size);
                float lz = random.Range(0.0f, size);
                float keep = random.NextFloat() * maxMeadow;
                float tall = random.NextFloat();
                float yaw = random.Range(-Mathf.Pi, Mathf.Pi);
                float pick = random.NextFloat();
                float x = corner.X + lx;
                float z = corner.Z + lz;
                float meadow = Mathf.Clamp((_thickets.Sample(x / wave * 1.7f + 31.4f, z / wave * 1.7f - 12.9f) - 0.15f) * 2.2f, 0.0f, 1.0f);
                if (meadow <= 0.0f)
                {
                    continue;
                }
                Biome? biome = BiomeAt(x, z, DepthAt(GridHeight(data, lx, lz)));
                if (biome == null || biome.FlowerModels.Length == 0 || keep >= biome.MeadowFlowerDensity * meadow)
                {
                    continue;
                }
                if (!CoverFits(data, lx, lz, -1.5f, 28.0f, out TerrainSurface surface, out float pathWeight) || surface != TerrainSurface.Grass || pathWeight > 0.85f)
                {
                    continue;
                }
                AddCover(data, PlantKind.Flower, Pick(biome.FlowerModels, pick), corner, lx, lz, yaw, Mathf.Lerp(0.14f, 0.34f, tall));
            }
        }

        // A few solid fallen logs where trees grow, lying across the slope and following the ground.
        int logTries = Mathf.CeilToInt(maxLogs * area);
        for (int n = 0; n < logTries; n++)
        {
            float x = corner.X + random.Range(0.0f, size);
            float z = corner.Z + random.Range(0.0f, size);
            float keep = random.NextFloat() * Mathf.Max(maxLogs, 0.01f);
            float length = random.Range(_settings.FallenLogLength.X, _settings.FallenLogLength.Y);
            float radius = random.Range(0.17f, 0.3f);
            float swing = random.Range(-0.45f, 0.45f);
            float ground = Height(x, z);
            Biome? biome = BiomeAt(x, z, DepthAt(ground));
            if (biome == null || biome.TreeModels.Length == 0 || keep >= biome.FallenLogDensity)
            {
                continue;
            }
            if (TooCloseToPath(x, z, length * 0.5f) || NearStation(x, z, _settings.ClearingRadius + length) || OnSummitPad(x, z, length) || SlopeDegrees(x, z) > 26.0f)
            {
                continue;
            }
            // Across the fall line (the way a trunk comes to rest), give or take a little.
            float dx = Height(x + SlopeStep, z) - Height(x - SlopeStep, z);
            float dz = Height(x, z + SlopeStep) - Height(x, z - SlopeStep);
            float yaw = Mathf.Atan2(dx, dz) + swing;
            Vector3 along = new Basis(Vector3.Up, yaw) * Vector3.Left;
            Vector3 high = new(x + along.X * length * 0.5f, 0.0f, z + along.Z * length * 0.5f);
            Vector3 low = new(x - along.X * length * 0.5f, 0.0f, z - along.Z * length * 0.5f);
            float highY = Height(high.X, high.Z);
            float lowY = Height(low.X, low.Z);
            float pitch = Mathf.Atan2(highY - lowY, length);
            Basis rotation = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Back, Mathf.Pi * 0.5f - pitch);
            Vector3 centre = new(x, (highY + lowY) * 0.5f + radius * 0.6f, z);
            data.Obstacles.Add(new ObstacleData(ObstacleKind.Trunk, centre, new Vector3(radius * 2.0f, length, radius * 2.0f), rotation));
        }

        PlaceRoadside(data, coord, corner);
        PlaceOutcrops(data, coord, corner);
    }

    /// <summary>
    /// Denser cover beside the paths (the second round): bushes and loose stones in a band just off
    /// each path's edge, the band RoadsideReach wide. Same rules as the rest of the ground cover:
    /// nothing on a path's band, a station's terrace or the summit pad, and the biome's density decides.
    /// Each path's band is walked over the chunk's length, both sides.
    /// </summary>
    private void PlaceRoadside(TerrainChunkData data, Vector2I coord, Vector3 corner)
    {
        if (_settings.Biomes.Length == 0)
        {
            return;
        }
        float shrubScatter = Mathf.Max(_settings.RoadsideShrubScatter, 0.0f);
        float rockScatter = Mathf.Max(_settings.RoadsideRockScatter, 0.0f);
        if (shrubScatter <= 0.0f && rockScatter <= 0.0f)
        {
            return;
        }
        SplitMix64 random = new(SplitMix64.Mix(SplitMix64.Mix(Seed, RoadsideSalt), ChunkSalt(coord)));
        float size = _settings.ChunkSize;
        float maxShrubs = 0.0f;
        float maxRocks = 0.0f;
        foreach (Biome biome in _settings.Biomes)
        {
            maxShrubs = Mathf.Max(maxShrubs, biome.ShrubDensity);
            maxRocks = Mathf.Max(maxRocks, biome.RockDensity);
        }
        float reachNear = Mathf.Max(_settings.RoadsideReach.X, 0.0f);
        float reachFar = Mathf.Max(_settings.RoadsideReach.Y, reachNear);
        // Band area per path and chunk, in units of 1000 m² (two sides, the band's width each).
        float bandUnits = size * (reachFar - reachNear) * 2.0f / 1000.0f;
        int shrubTries = Mathf.RoundToInt(maxShrubs * shrubScatter * bandUnits);
        int rockTries = Mathf.RoundToInt(maxRocks * rockScatter * bandUnits);
        if (shrubTries <= 0 && rockTries <= 0)
        {
            return;
        }
        RouteBranch[] branches = BranchesAt(corner.Z + size * 0.5f);
        for (int path = 0; path <= branches.Length; path++)
        {
            for (int n = 0; n < shrubTries + rockTries; n++)
            {
                bool shrub = n < shrubTries;
                float z = corner.Z + random.Range(0.0f, size);
                float keep = random.NextFloat();
                float side = random.NextFloat() < 0.5f ? -1.0f : 1.0f;
                float outward = random.Range(reachNear, reachFar);
                float tall = random.NextFloat();
                float yaw = random.Range(-Mathf.Pi, Mathf.Pi);
                float pick = random.NextFloat();
                float centreX;
                float half;
                if (path == 0)
                {
                    centreX = SpineX(z);
                    half = _settings.SpineHalfWidth;
                }
                else
                {
                    RouteBranch branch = branches[path - 1];
                    if (z > branch.SplitZ || z < branch.JoinZ)
                    {
                        continue;
                    }
                    centreX = BranchX(branch, z);
                    half = HalfWidth(branch.Kind);
                }
                float x = centreX + side * (half + _settings.SpineClearance + outward);
                float lx = x - corner.X;
                float lz = z - corner.Z;
                if (lx < 0.0f || lx >= size)
                {
                    continue;
                }
                Biome? biome = BiomeAt(x, z, DepthAt(GridHeight(data, lx, lz)));
                if (biome == null)
                {
                    continue;
                }
                if (shrub)
                {
                    if (biome.ShrubModels.Length == 0 || keep * maxShrubs >= biome.ShrubDensity * shrubScatter)
                    {
                        continue;
                    }
                    if (TooCloseToPath(x, z, 0.2f) || !CoverFits(data, lx, lz, 0.0f, 32.0f, out TerrainSurface surface, out _)
                        || (surface != TerrainSurface.Grass && surface != TerrainSurface.Mud))
                    {
                        continue;
                    }
                    // A bush, or a pair of them, growing at the path's edge.
                    int group = 1 + (pick < 0.35f ? 1 : 0);
                    for (int k = 0; k < group; k++)
                    {
                        float gx = lx;
                        float gz = lz;
                        if (k > 0)
                        {
                            gx += random.Range(-0.8f, 0.8f);
                            gz += random.Range(-0.8f, 0.8f);
                        }
                        bool groupFits = CoverFits(data, gx, gz, 0.0f, 32.0f, out TerrainSurface groupSurface, out _);
                        if (!groupFits || (groupSurface != TerrainSurface.Grass && groupSurface != TerrainSurface.Mud))
                        {
                            continue;
                        }
                        string model = Pick(biome.ShrubModels, random.NextFloat());
                        float height = Mathf.Lerp(biome.ShrubHeight.X, biome.ShrubHeight.Y, tall * tall) * (k > 0 ? 0.75f : 1.0f);
                        AddCover(data, PlantKind.Shrub, model, corner, gx, gz, yaw, height);
                    }
                }
                else
                {
                    if (_settings.RockModels.Length == 0 || keep * maxRocks >= biome.RockDensity * rockScatter)
                    {
                        continue;
                    }
                    if (!CoverFits(data, lx, lz, 0.4f, 45.0f, out TerrainSurface surface, out float pathWeight) || pathWeight > 0.3f || surface == TerrainSurface.Snow)
                    {
                        continue;
                    }
                    float height = Mathf.Lerp(_settings.RockHeight.X, _settings.RockHeight.Y, tall * tall);
                    AddCover(data, PlantKind.Rock, Pick(_settings.RockModels, pick), corner, lx, lz, yaw, height);
                }
            }
        }
    }

    /// <summary>
    /// Rock outcrops on slopes (the second round): a main rock with a few smaller stones round its
    /// foot, where the ground's slope is inside OutcropSlope and the spot is off the paths.
    /// </summary>
    private void PlaceOutcrops(TerrainChunkData data, Vector2I coord, Vector3 corner)
    {
        if (_settings.RockModels.Length == 0 || _settings.OutcropDensity <= 0.0f || _settings.Biomes.Length == 0)
        {
            return;
        }
        SplitMix64 random = new(SplitMix64.Mix(SplitMix64.Mix(Seed, RoadsideSalt ^ 0x4F55544FUL), ChunkSalt(coord)));
        float size = _settings.ChunkSize;
        float area = size * size / 1000.0f;
        int tries = Mathf.RoundToInt(_settings.OutcropDensity * area);
        float slopeLow = _settings.OutcropSlope.X;
        float slopeHigh = _settings.OutcropSlope.Y;
        int stoneLow = Mathf.Max(_settings.OutcropStones.X, 0);
        int stoneHigh = Mathf.Max(_settings.OutcropStones.Y, stoneLow);
        for (int n = 0; n < tries; n++)
        {
            float lx = random.Range(0.0f, size);
            float lz = random.Range(0.0f, size);
            float big = random.NextFloat();
            float yaw = random.Range(-Mathf.Pi, Mathf.Pi);
            float pick = random.NextFloat();
            int stones = stoneLow + random.NextInt(stoneHigh - stoneLow + 1);
            float slope = GridSlope(data, lx, lz);
            if (slope < slopeLow || slope > slopeHigh)
            {
                continue;
            }
            float x = corner.X + lx;
            float z = corner.Z + lz;
            Biome? biome = BiomeAt(x, z, DepthAt(GridHeight(data, lx, lz)));
            if (biome == null || !CoverFits(data, lx, lz, 4.0f, slopeHigh + 6.0f, out TerrainSurface surface, out float pathWeight)
                || surface == TerrainSurface.Snow || pathWeight > 0.2f)
            {
                continue;
            }
            float mainHeight = Mathf.Lerp(_settings.OutcropHeight.X, _settings.OutcropHeight.Y, big * big);
            AddCover(data, PlantKind.Rock, Pick(_settings.RockModels, pick), corner, lx, lz, yaw, mainHeight);
            for (int k = 0; k < stones; k++)
            {
                float angle = random.Range(-Mathf.Pi, Mathf.Pi);
                float reach = random.Range(0.5f, 1.4f);
                float gx = lx + Mathf.Cos(angle) * reach;
                float gz = lz + Mathf.Sin(angle) * reach;
                float stoneYaw = random.Range(-Mathf.Pi, Mathf.Pi);
                float stoneHeight = mainHeight * random.Range(0.35f, 0.7f);
                string model = Pick(_settings.RockModels, random.NextFloat());
                if (!CoverFits(data, gx, gz, 0.4f, slopeHigh + 10.0f, out _, out _))
                {
                    continue;
                }
                AddCover(data, PlantKind.Rock, model, corner, gx, gz, stoneYaw, stoneHeight);
            }
        }
    }

    /// <summary>
    /// Things lying at the paths' edges, just off the walking band: stumps, cut wood, a lost
    /// carrier's box or bedroll, a wooden cross. A path's own line decides where; nothing lands on it.
    /// </summary>
    private void PlaceWayside(TerrainChunkData data, Vector3 corner, SplitMix64 random)
    {
        float size = _settings.ChunkSize;
        float midZ = corner.Z + size * 0.5f;
        float density = 0.0f;
        foreach (Biome biome in _settings.Biomes)
        {
            density = Mathf.Max(density, biome.WaysideDensity);
        }
        // Tries per path and side over the chunk's length (WaysideDensity is per 100 m).
        int tries = Mathf.CeilToInt(density * size / 100.0f * 2.0f);
        RouteBranch[] branches = BranchesAt(midZ);
        for (int path = 0; path <= branches.Length; path++)
        {
            for (int n = 0; n < tries; n++)
            {
                float z = corner.Z + random.Range(0.0f, size);
                float keep = random.NextFloat() * density * 2.0f;
                float side = 1.0f;
                if (random.NextFloat() < 0.5f)
                {
                    side = -1.0f;
                }
                float outward = random.Range(0.3f, 2.8f);
                float tall = random.NextFloat();
                float yaw = random.Range(-Mathf.Pi, Mathf.Pi);
                float pick = random.NextFloat();
                float centreX;
                float half;
                if (path == 0)
                {
                    centreX = SpineX(z);
                    half = _settings.SpineHalfWidth;
                }
                else
                {
                    RouteBranch branch = branches[path - 1];
                    if (z > branch.SplitZ || z < branch.JoinZ)
                    {
                        continue;
                    }
                    centreX = BranchX(branch, z);
                    half = HalfWidth(branch.Kind);
                }
                float x = centreX + side * (half + _settings.SpineClearance + outward);
                float lx = x - corner.X;
                float lz = z - corner.Z;
                if (lx < 0.0f || lx >= size)
                {
                    continue;
                }
                Biome? biome = BiomeAt(x, z, DepthAt(GridHeight(data, lx, lz)));
                if (biome == null || biome.WaysideModels.Length == 0 || keep >= biome.WaysideDensity)
                {
                    continue;
                }
                if (TooCloseToPath(x, z, 0.2f) || !CoverFits(data, lx, lz, 0.0f, 34.0f, out _, out _))
                {
                    continue;
                }
                string model = Pick(biome.WaysideModels, pick);
                AddCover(data, PlantKind.Debris, model, corner, lx, lz, yaw, DetailHeight(model, tall));
            }
        }
    }

    /// <summary>
    /// True when cover may stand at this spot of the chunk (local m): inside the chunk, on ground no
    /// steeper than <paramref name="maxSlope"/>, <paramref name="pathGap"/> m off every path's band
    /// (negative: it may reach into the band's soft edge), off station terraces and the summit pad.
    /// </summary>
    private bool CoverFits(TerrainChunkData data, float lx, float lz, float pathGap, float maxSlope, out TerrainSurface surface, out float pathWeight)
    {
        surface = TerrainSurface.Grass;
        pathWeight = 0.0f;
        float size = _settings.ChunkSize;
        if (lx < 0.0f || lz < 0.0f || lx >= size || lz >= size)
        {
            return false;
        }
        Vector3 corner = ChunkCorner(data.Coord);
        float x = corner.X + lx;
        float z = corner.Z + lz;
        float slope = GridSlope(data, lx, lz);
        if (slope > maxSlope)
        {
            return false;
        }
        if (pathGap >= 0.0f && TooCloseToPath(x, z, pathGap))
        {
            return false;
        }
        if (OnSummitPad(x, z, 1.0f) || NearStation(x, z, _settings.ClearingRadius + 1.0f))
        {
            return false;
        }
        pathWeight = PathWeight(x, z, out RouteKind kind);
        surface = Classify(x, z, slope, pathWeight, kind);
        return true;
    }

    /// <summary>A rock taller than this stands out of the ground fog instead of holding it, in m.</summary>
    public const float FogLowRock = 1.4f;

    /// <summary>
    /// Notes what holds ground fog in this chunk: every standing tree, and the low rocks (loose
    /// stones weighted by their height, a knee-high one counting 1; low boulders counting 2).
    /// </summary>
    private void NoteFogCover(TerrainChunkData data)
    {
        for (int i = 0; i < data.Plants.Count; i++)
        {
            PlantData plant = data.Plants[i];
            if (plant.Kind == PlantKind.Tree)
            {
                data.FogTrees.Add(new Vector2(plant.Position.X, plant.Position.Z));
            }
            else if (plant.Kind == PlantKind.Rock && plant.Scale <= FogLowRock)
            {
                data.FogRocks.Add(new Vector3(plant.Position.X, plant.Position.Z, Mathf.Clamp(plant.Scale / 0.45f, 0.25f, 1.5f)));
            }
        }
        foreach (ObstacleData obstacle in data.Obstacles)
        {
            if (obstacle.Kind == ObstacleKind.Boulder && obstacle.Size.Y * (1.0f - _settings.BoulderSink) <= FogLowRock)
            {
                data.FogRocks.Add(new Vector3(obstacle.Position.X, obstacle.Position.Z, 2.0f));
            }
        }
    }

    private void AddCover(TerrainChunkData data, PlantKind kind, string model, Vector3 corner, float lx, float lz, float yaw, float height)
    {
        float ground = GridHeight(data, lx, lz);
        data.Plants.Add(new PlantData(kind, model, new Vector3(corner.X + lx, ground, corner.Z + lz), yaw, height, Vector2.Zero, true));
    }

    private static string Pick(string[] models, float roll)
    {
        return models[Mathf.Min((int)(roll * models.Length), models.Length - 1)];
    }

    /// <summary>How tall a piece of debris is, in m, by what it is (a mushroom is small, a stump knee-high).</summary>
    private static float DetailHeight(string model, float roll)
    {
        Vector2 range = new(0.25f, 0.5f);
        if (model.Contains("mushroom"))
        {
            range = new Vector2(0.12f, 0.32f);
        }
        else if (model.Contains("stump"))
        {
            range = new Vector2(0.3f, 0.65f);
        }
        else if (model.Contains("/log"))
        {
            range = new Vector2(0.2f, 0.32f);
        }
        else if (model.Contains("plant_flat"))
        {
            range = new Vector2(0.3f, 0.6f);
        }
        else if (model.Contains("cross"))
        {
            range = new Vector2(0.7f, 1.0f);
        }
        else if (model.Contains("bucket") || model.Contains("bedroll"))
        {
            range = new Vector2(0.25f, 0.35f);
        }
        else if (model.Contains("shovel"))
        {
            range = new Vector2(0.9f, 1.1f);
        }
        return Mathf.Lerp(range.X, range.Y, roll);
    }

    /// <summary>Height on the rendered grid triangles (local x, z in m).</summary>
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
        // Follow the rendered a-b-d / a-d-c diagonal instead of floating on a bilinear patch.
        if (fx >= fz) { return h00 + (h10 - h00) * fx + (h11 - h10) * fz; }
        return h00 + (h11 - h01) * fx + (h01 - h00) * fz;
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
