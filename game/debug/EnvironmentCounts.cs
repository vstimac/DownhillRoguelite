using System.Globalization;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug-only, headless: counts what the terrain generator puts round the environment comparison
/// spots (the same depths as EnvironmentShots, seed 1), so a change to the land can be judged by
/// numbers when no window may be opened. For each spot it builds the 5×5 chunks round the path and
/// reports trees (and how many stand up on the valley's flanks), shrubs, rocks, boulders, the share
/// of the high flanks under snow and of open ground that is bare. Each spot is counted twice: with
/// the fifth round's landscape character, and with it off (as the fourth round had it).
/// `godot --headless --path . res://debug/EnvironmentCounts.tscn -- --seed=1`
/// </summary>
public partial class EnvironmentCounts : Node
{
    [Export] public float[] Depths { get; set; } = { 25.0f, 120.0f, 260.0f, 1250.0f, 2350.0f, 3400.0f, 4450.0f, 7600.0f };
    [Export] public int ChunkRadius { get; set; } = 2;

    public override void _Ready()
    {
        ulong seed = 1;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--seed=", System.StringComparison.Ordinal)) { seed = ulong.Parse(arg["--seed=".Length..], CultureInfo.InvariantCulture); }
        }
        TerrainSettings now = new();
        // The fifth round's features off, and its nearer rare things back as the fourth round had them.
        TerrainSettings before = new()
        {
            AlpineTreeThin = 0.0f, AlpineTreeHeight = 1.0f, AlpineTreeLean = 0.0f, AlpinePathGap = 0.0f, AlpineRockDrop = 0.0f, AlpineBare = 0.0f, AlpineOutcrops = 0.0f,
            ScreeFanShare = 0.0f, GullyShrubDensity = 0.0f, FieldWavelength = 0.0f, WetHollowDrop = 0.0f,
            GroupNearPathShare = 0.6f, GroupSightDistance = new Vector2(11.0f, 42.0f), LoneTreeChance = 0.6f,
        };
        TerrainGenerator withRound = new(now, seed);
        TerrainGenerator withoutRound = new(before, seed);
        Color[] colours = new Color[5];
        for (int i = 0; i < colours.Length; i++) { colours[i] = new Color(0.5f, 0.55f, 0.45f); }
        foreach (float depth in Depths)
        {
            float z = now.SpawnZ - 5.0f;
            while (withRound.DepthAt(withRound.Height(withRound.SpineX(z), z)) < depth && z > -60000.0f) { z -= 10.0f; }
            Vector3 at = new(withRound.SpineX(z), withRound.Height(withRound.SpineX(z), z), z);
            string a = Count(withoutRound, at, colours);
            string b = Count(withRound, at, colours);
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"COUNT depth {depth:0} round 4: {a}"));
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"COUNT depth {depth:0} round 5: {b}"));
        }
        GD.Print("PASS  environment counts");
        GetTree().Quit(0);
    }

    private string Count(TerrainGenerator generator, Vector3 at, Color[] colours)
    {
        Vector2I centre = generator.ChunkAt(at.X, at.Z);
        int trees = 0, flankTrees = 0, shrubs = 0, rocks = 0, boulders = 0, trunks = 0, walls = 0, heroesNear = 0;
        float treeHeight = 0.0f;
        double worst = 0.0;
        for (int dz = -ChunkRadius; dz <= ChunkRadius; dz++)
        {
            for (int dx = -ChunkRadius; dx <= ChunkRadius; dx++)
            {
                TerrainChunkData data = generator.BuildChunk(centre + new Vector2I(dx, dz), colours, 0);
                worst = System.Math.Max(worst, data.BuildMilliseconds);
                for (int i = 0; i < data.Plants.Count; i++)
                {
                    PlantData plant = data.Plants[i];
                    if (plant.Kind == PlantKind.Tree)
                    {
                        trees++;
                        treeHeight += plant.Scale;
                        if (generator.FlankRiseAt(plant.Position.X, plant.Position.Z) >= 0.15f) { flankTrees++; }
                    }
                    else if (plant.Kind == PlantKind.Shrub) { shrubs++; }
                    else if (plant.Kind == PlantKind.Rock && plant.Model.Contains("stone-wall")) { walls++; }
                    else if (plant.Kind == PlantKind.Rock) { rocks++; }
                }
                foreach (ObstacleData obstacle in data.Obstacles)
                {
                    if (obstacle.Kind == ObstacleKind.Boulder) { boulders++; } else { trunks++; }
                    if (obstacle.Kind == ObstacleKind.Boulder && Mathf.Max(obstacle.Size.X, obstacle.Size.Y) >= 3.5f
                        && generator.NearestPathDistance(obstacle.Position.X, obstacle.Position.Z) < 40.0f) { heroesNear++; }
                }
            }
        }
        // The high flanks (more than half way up) within 100 m of the spot, sampled every 2 m.
        int flank = 0, snowy = 0, rocky = 0, foot = 0, footScree = 0, open = 0, boggy = 0;
        for (float x = at.X - 100.0f; x <= at.X + 100.0f; x += 2.0f)
        {
            for (float z = at.Z - 100.0f; z <= at.Z + 100.0f; z += 2.0f)
            {
                float rise = generator.FlankRiseAt(x, z);
                TerrainSurface surface = generator.SurfaceAt(x, z);
                if (rise < 0.06f) { open++; if (surface == TerrainSurface.Mud) { boggy++; } }
                else if (rise <= 0.65f) { foot++; if (surface == TerrainSurface.Scree) { footScree++; } }
                if (rise < 0.5f) { continue; }
                flank++;
                if (surface == TerrainSurface.Snow) { snowy++; }
                else if (surface == TerrainSurface.Rock || surface == TerrainSurface.Scree) { rocky++; }
            }
        }
        float share = flank > 0 ? 100.0f / flank : 0.0f;
        return string.Create(CultureInfo.InvariantCulture,
            $"trees {trees} ({flankTrees} on the flanks, mean {(trees > 0 ? treeHeight / trees : 0):0.0} m), shrubs {shrubs}, rocks {rocks}, walls {walls}, boulders {boulders} ({heroesNear} big within 40 m of a path), logs {trunks}, high flanks {snowy * share:0}% snow {rocky * share:0}% rock/scree, flank foot {(foot > 0 ? 100.0f * footScree / foot : 0):0}% scree, valley floor {(open > 0 ? 100.0f * boggy / open : 0):0}% bog, slowest chunk {worst:0.0} ms");
    }
}
