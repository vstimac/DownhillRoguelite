using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// The fifth round's landscape character, on top of the third round's relief and the fourth's tree
/// lines: the summit's high ground (sparse, stunted, wind-bent trees, rock bands and gravel, a clear
/// view down the valley), the gentle circles' fields (hedgerows, old walls, boggy hollows with reeds),
/// and the flanks' detail (scree fans at their foot, shrubs in their gullies). Everything is seeded
/// and has its own random stream, so the earlier rounds' placements are unchanged.
/// </summary>
public sealed partial class TerrainGenerator
{
    private const ulong ReedSalt = 26;
    private const string NatureKit = "res://art/props/kenney_nature_kit/GLB format/";
    private const string GraveyardKit = "res://art/props/kenney_graveyard_kit/GLB format/";
    private static readonly string[] WallModels = { GraveyardKit + "stone-wall.glb", GraveyardKit + "stone-wall-damaged.glb" };
    private static readonly string[] ReedModels = { NatureKit + "grass_leafsLarge.glb", NatureKit + "plant_flatTall.glb", NatureKit + "grass_large.glb" };
    // Height of a wall piece, in m; the model is 1 m long by 0.725 m high, so a piece is about 1.24 m long.
    private const float WallHeight = 0.9f;
    private const float WallPieceLength = WallHeight / 0.725f;

    private static readonly System.Func<Biome, float> BiomeWetness = biome => biome.Wetness;
    private static readonly System.Func<Biome, float> BiomeHedgerows = biome => biome.Hedgerows;

    private readonly GradientNoise _fields;

    /// <summary>How alpine the ground is at this point of the descent: 1 near the summit, 0 below AlpineDepth's second value.</summary>
    public float AlpineAt(float z)
    {
        float depth = Mathf.Max(0.0f, _startHeight - Descent(z));
        return 1.0f - Mathf.SmoothStep(_settings.AlpineDepth.X, Mathf.Max(_settings.AlpineDepth.Y, _settings.AlpineDepth.X + 1.0f), depth);
    }

    /// <summary>A circle's own value at this point of the descent, blended between circles.</summary>
    private float BiomeValueAt(float z, System.Func<Biome, float> value)
    {
        if (_settings.Biomes.Length == 0)
        {
            return 0.0f;
        }
        BiomesAt(Mathf.Max(0.0f, _startHeight - Descent(z)), out int from, out int to, out float blend);
        return Mathf.Lerp(value(_settings.Biomes[from]), value(_settings.Biomes[to]), blend);
    }

    /// <summary>
    /// A tree's lean: its own random lean, plus on the high ground a lean away from the wind (Euler
    /// X tips the top towards +z, Euler Z towards -x), so the summit's trees all bend the same way.
    /// </summary>
    private Vector2 WindLean(Vector2 lean, float leanDegrees, float alpine)
    {
        Vector2 own = lean * Mathf.DegToRad(leanDegrees);
        if (alpine <= 0.0f || _settings.AlpineTreeLean <= 0.0f || _settings.WindDirection.LengthSquared() < 0.0001f)
        {
            return own;
        }
        Vector2 wind = _settings.WindDirection.Normalized();
        float bend = Mathf.DegToRad(_settings.AlpineTreeLean) * alpine;
        return own * (1.0f - 0.5f * alpine) + new Vector2(wind.Y * bend, -wind.X * bend);
    }

    /// <summary>
    /// Scree fans at the foot of the flanks: tongues of loose stone running down the slope (across
    /// the valley, so drawn long in x) on ground a little gentler than scree slopes, widest at the foot.
    /// </summary>
    private bool InScreeFan(float x, float z, float slope)
    {
        if (_settings.ScreeFanShare <= 0.0f || _settings.FlankHeight <= 0.0f || slope < 13.0f)
        {
            return false;
        }
        float rise = FlankRiseAt(x, z);
        if (rise < 0.06f || rise > 0.65f)
        {
            return false;
        }
        float length = Mathf.Max(_settings.ScreeFanSize.X, 1.0f);
        float width = Mathf.Max(_settings.ScreeFanSize.Y, 1.0f);
        float fan = _scree.Sample(x / length + 31.7f, z / width - 12.9f);
        return fan > 0.5f - _settings.ScreeFanShare + rise * 0.35f;
    }

    /// <summary>
    /// Shrubs in the flanks' gullies (between the ridges, where water and soil gather), so a flank
    /// is not one bare grey-brown slope. The circle's own shrubs; on the high ground its dwarf pines.
    /// </summary>
    private void PlaceGullyShrubs(TerrainChunkData data, Vector2I coord, Vector3 corner)
    {
        if (_settings.GullyShrubDensity <= 0.0f || _settings.FlankHeight <= 0.0f)
        {
            return;
        }
        SplitMix64 random = new(SplitMix64.Mix(SplitMix64.Mix(Seed, GullySalt), ChunkSalt(coord)));
        float size = _settings.ChunkSize;
        int tries = Mathf.RoundToInt(_settings.GullyShrubDensity * size * size / 1000.0f);
        float wave = Mathf.Max(_settings.RidgeWavelength, 1.0f);
        for (int n = 0; n < tries; n++)
        {
            float lx = random.Range(0.0f, size);
            float lz = random.Range(0.0f, size);
            float keep = random.NextFloat();
            float pick = random.NextFloat();
            float tall = random.NextFloat();
            float yaw = random.Range(-Mathf.Pi, Mathf.Pi);
            float x = corner.X + lx;
            float z = corner.Z + lz;
            float rise = FlankRiseAt(x, z);
            if (rise < 0.2f)
            {
                continue;
            }
            float crest = 1.0f - Mathf.Abs(_ridges.Fractal(x / wave, z / wave, 2));
            crest *= crest;
            // Deepest in the gullies (crest near 0), none on the crests.
            if (keep >= 1.0f - Mathf.SmoothStep(0.3f, 0.6f, crest))
            {
                continue;
            }
            if (!CoverFits(data, lx, lz, 3.0f, 38.0f, out TerrainSurface surface, out _)
                || surface == TerrainSurface.Snow || surface == TerrainSurface.Rock)
            {
                continue;
            }
            Biome? biome = BiomeAt(x, z, DepthAt(GridHeight(data, lx, lz)));
            if (biome == null || biome.ShrubModels.Length == 0)
            {
                continue;
            }
            AddCover(data, PlantKind.Shrub, Pick(biome.ShrubModels, pick), corner, lx, lz, yaw,
                Mathf.Lerp(biome.ShrubHeight.X, biome.ShrubHeight.Y, tall) * 1.15f);
        }
    }

    /// <summary>
    /// The fields of the gentle circles: their edges are straight lines between seeded field centres
    /// (a Voronoi pattern FieldWavelength across). Each edge is wholly a hedgerow (shrubs with now and
    /// then a tree), an old stone wall, or nothing, with gaps along it. Off the paths and gentle ground only.
    /// </summary>
    private void PlaceHedgerows(TerrainChunkData data, Vector2I coord, Vector3 corner)
    {
        float size = _settings.ChunkSize;
        if (_settings.FieldWavelength <= 1.0f || _settings.HedgeSpacing < 0.5f)
        {
            return;
        }
        float share = Mathf.Max(BiomeValueAt(corner.Z, BiomeHedgerows), BiomeValueAt(corner.Z + size, BiomeHedgerows));
        if (share <= 0.0f)
        {
            return;
        }
        SplitMix64 random = new(SplitMix64.Mix(SplitMix64.Mix(Seed, HedgeSalt), ChunkSalt(coord)));
        float step = _settings.HedgeSpacing;
        int steps = Mathf.FloorToInt(size / step);
        float gapWave = Mathf.Max(_settings.FieldWavelength * 0.3f, 1.0f);
        for (int j = 0; j < steps; j++)
        {
            for (int i = 0; i < steps; i++)
            {
                float px = corner.X + (i + random.NextFloat()) * step;
                float pz = corner.Z + (j + random.NextFloat()) * step;
                float pick = random.NextFloat();
                float tall = random.NextFloat();
                float twist = random.Range(-0.12f, 0.12f);
                NearestFields(px, pz, out Vector2 a, out Vector2 b, out ulong edge);
                Vector2 across = b - a;
                if (across.LengthSquared() < 1.0f)
                {
                    continue;
                }
                across = across.Normalized();
                float off = (new Vector2(px, pz) - (a + b) * 0.5f).Dot(across);
                if (Mathf.Abs(off) > step * 0.5f)
                {
                    continue;
                }
                // Onto the edge itself, so a hedge or wall runs straight.
                float x = px - across.X * off;
                float z = pz - across.Y * off;
                float lx = x - corner.X;
                float lz = z - corner.Z;
                float exists = Hash01(SplitMix64.Mix(edge, 1));
                float wallRoll = Hash01(SplitMix64.Mix(edge, 2));
                if (exists >= BiomeValueAt(z, BiomeHedgerows) || _fields.Sample(x / gapWave + 5.3f, z / gapWave - 8.1f) < -0.3f)
                {
                    continue;
                }
                bool wall = wallRoll < _settings.WallShare;
                if (!CoverFits(data, lx, lz, 3.5f, wall ? 16.0f : 24.0f, out TerrainSurface surface, out _)
                    || (surface != TerrainSurface.Grass && surface != TerrainSurface.Mud))
                {
                    continue;
                }
                Biome? biome = BiomeAt(x, z, DepthAt(GridHeight(data, lx, lz)));
                if (biome == null)
                {
                    continue;
                }
                Vector2 along = new(-across.Y, across.X);
                // Basis(Up, yaw) turns the model's x axis to (cos yaw, 0, -sin yaw): lay it along the edge.
                float yaw = Mathf.Atan2(-along.Y, along.X);
                if (wall)
                {
                    for (int k = -1; k <= 1; k += 2)
                    {
                        float wx = lx + along.X * WallPieceLength * 0.5f * k;
                        float wz = lz + along.Y * WallPieceLength * 0.5f * k;
                        if (wx < 0.0f || wz < 0.0f || wx >= size || wz >= size)
                        {
                            continue;
                        }
                        string model = WallModels[pick < 0.55f ? 1 : 0];
                        data.Plants.Add(new PlantData(PlantKind.Rock, model, new Vector3(corner.X + wx, GridHeight(data, wx, wz), corner.Z + wz),
                            yaw + twist, WallHeight * (0.85f + 0.3f * tall), Vector2.Zero, true));
                    }
                }
                else if (tall < 0.06f && biome.TreeModels.Length > 0)
                {
                    // A hedgerow tree.
                    string model = biome.TreeModels[Mathf.Min((int)(pick * biome.TreeModels.Length), biome.TreeModels.Length - 1)];
                    float height = Mathf.Lerp(biome.TreeHeight.X, biome.TreeHeight.Y, pick) * 0.85f;
                    float ground = GridHeight(data, lx, lz);
                    data.Plants.Add(new PlantData(PlantKind.Tree, model, new Vector3(x, ground - height * _settings.TreeSink, z), yaw, height,
                        Vector2.Zero, true, Mathf.Clamp(height * biome.TrunkRadiusPerMetre, 0.035f, 0.4f)));
                }
                else if (biome.ShrubModels.Length > 0)
                {
                    AddCover(data, PlantKind.Shrub, Pick(biome.ShrubModels, pick), corner, lx, lz, yaw + pick * Mathf.Tau, Mathf.Lerp(1.2f, 2.1f, tall));
                }
            }
        }
    }

    /// <summary>The two field centres nearest a point, and a seeded id for the edge between them.</summary>
    private void NearestFields(float x, float z, out Vector2 a, out Vector2 b, out ulong edge)
    {
        float wave = _settings.FieldWavelength;
        int ci = Mathf.FloorToInt(x / wave);
        int cj = Mathf.FloorToInt(z / wave);
        Vector2 at = new(x, z);
        float first = float.MaxValue;
        float second = float.MaxValue;
        a = Vector2.Zero;
        b = Vector2.Zero;
        ulong idA = 0;
        ulong idB = 0;
        ulong salt = SplitMix64.Mix(Seed, HedgeSalt + 100);
        for (int dj = -1; dj <= 1; dj++)
        {
            for (int di = -1; di <= 1; di++)
            {
                ulong id = SplitMix64.Mix(salt, ChunkSalt(new Vector2I(ci + di, cj + dj)));
                Vector2 site = new((ci + di + 0.15f + 0.7f * Hash01(SplitMix64.Mix(id, 3))) * wave,
                    (cj + dj + 0.15f + 0.7f * Hash01(SplitMix64.Mix(id, 4))) * wave);
                float d = site.DistanceSquaredTo(at);
                if (d < first)
                {
                    second = first;
                    b = a;
                    idB = idA;
                    first = d;
                    a = site;
                    idA = id;
                }
                else if (d < second)
                {
                    second = d;
                    b = site;
                    idB = id;
                }
            }
        }
        edge = SplitMix64.Mix(System.Math.Min(idA, idB), System.Math.Max(idA, idB));
    }

    private static float Hash01(ulong value)
    {
        return (value >> 40) / 16777216.0f;
    }

    /// <summary>Reeds round the boggy hollows of the wet circles.</summary>
    private void PlaceReeds(TerrainChunkData data, Vector2I coord, Vector3 corner)
    {
        float size = _settings.ChunkSize;
        float wet = Mathf.Max(BiomeValueAt(corner.Z, BiomeWetness), BiomeValueAt(corner.Z + size, BiomeWetness));
        if (wet <= 0.0f)
        {
            return;
        }
        SplitMix64 random = new(SplitMix64.Mix(SplitMix64.Mix(Seed, ReedSalt), ChunkSalt(coord)));
        int tries = Mathf.RoundToInt(70.0f * wet);
        for (int n = 0; n < tries; n++)
        {
            float lx = random.Range(0.0f, size);
            float lz = random.Range(0.0f, size);
            float pick = random.NextFloat();
            float tall = random.NextFloat();
            float yaw = random.Range(-Mathf.Pi, Mathf.Pi);
            if (!CoverFits(data, lx, lz, 1.5f, 14.0f, out TerrainSurface surface, out _) || surface != TerrainSurface.Mud)
            {
                continue;
            }
            AddCover(data, PlantKind.Shrub, Pick(ReedModels, pick), corner, lx, lz, yaw, Mathf.Lerp(0.7f, 1.4f, tall));
        }
    }
}
