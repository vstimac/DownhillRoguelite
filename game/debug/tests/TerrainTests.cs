using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for M5: the same seed gives the same mountain, the spine stays walkable and
/// clear of obstacles for 2 km, collision matches the generated heights, terrain reports its
/// surfaces, and a scripted walk follows the spine 300 m down while chunk streaming stays bounded.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/TerrainTests.tscn
/// </summary>
public partial class TerrainTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }

    [ExportGroup("Scenario")]
    [Export] public float SettleTime { get; set; } = 1.0f;
    [Export] public int DeterminismSamples { get; set; } = 200;
    [Export] public float SpineCheckLength { get; set; } = 2000.0f;
    [Export] public float SpineCheckStep { get; set; } = 1.0f;
    [Export] public int CollisionSamples { get; set; } = 60;
    [Export] public float WalkDistance { get; set; } = 300.0f;
    [Export] public float WalkTimeout { get; set; } = 150.0f;
    /// <summary>Steering aims at the spine this far ahead, in m.</summary>
    [Export] public float SteerLookAhead { get; set; } = 6.0f;

    [ExportGroup("Criteria")]
    /// <summary>Allowed difference between collision and generated height at grid points, in m.</summary>
    [Export] public float MaxCollisionError { get; set; } = 0.02f;
    /// <summary>Steepest allowed sideways tilt across the spine's direction of travel, in degrees.</summary>
    [Export] public float MaxSpineCrossSlope { get; set; } = 18.0f;
    [Export] public float MaxWalkAirTime { get; set; } = 0.5f;
    [Export] public float MaxSpineDeviation { get; set; } = 5.0f;
    /// <summary>Most chunks allowed loaded at once during the walk.</summary>
    [Export] public int MaxLoadedChunks { get; set; } = 49;
    /// <summary>Longest allowed main-thread time to add one streamed chunk, in ms.</summary>
    [Export] public double MaxApplyMilliseconds { get; set; } = 16.0;

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        if (PlayerBody == null || Streamer == null || Streamer.Generator == null)
        {
            GD.PushError("TerrainTests: PlayerBody and Streamer must be assigned.");
            GetTree().Quit(1);
            return;
        }

        TestDeterminism(Streamer.Settings);
        TestSpine(Streamer.Generator);
        TestObstacleClearance(Streamer.Generator);

        PlayerBody.Teleport(Streamer.SpawnPoint + Vector3.Up * 0.1f, 0.0f);
        PlayerBody.Health?.Refill();
        await WaitSeconds(SettleTime);
        TestCollision(Streamer);
        TestSurface(PlayerBody, Streamer.Generator);
        await TestWalk(PlayerBody, Streamer);

        if (_failures.Count == 0)
        {
            GD.Print("TERRAIN TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"TERRAIN TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void TestDeterminism(TerrainSettings settings)
    {
        TerrainGenerator a = new(settings, 1234);
        TerrainGenerator b = new(settings, 1234);
        TerrainGenerator other = new(settings, 98765);
        SplitMix64 random = new(42);
        int mismatches = 0;
        float largestDifference = 0.0f;
        for (int i = 0; i < DeterminismSamples; i++)
        {
            float x = random.Range(-500.0f, 500.0f);
            float z = random.Range(-3000.0f, 100.0f);
            if (a.Height(x, z) != b.Height(x, z) || a.SurfaceAt(x, z) != b.SurfaceAt(x, z))
            {
                mismatches++;
            }
            largestDifference = Mathf.Max(largestDifference, Mathf.Abs(a.Height(x, z) - other.Height(x, z)));
        }
        Check("same seed, same mountain", mismatches == 0, $"{mismatches} of {DeterminismSamples} samples differ");
        Check("different seed, different mountain", largestDifference > 1.0f, $"largest height difference {largestDifference:0.0} m");

        TerrainChunkData first = a.BuildChunk(new Vector2I(3, -7), new Color[5], 0);
        TerrainChunkData second = b.BuildChunk(new Vector2I(3, -7), new Color[5], 0);
        bool sameObstacles = first.Obstacles.Count == second.Obstacles.Count;
        for (int i = 0; sameObstacles && i < first.Obstacles.Count; i++)
        {
            sameObstacles = first.Obstacles[i] == second.Obstacles[i];
        }
        Check("same seed, same obstacles", sameObstacles && first.Obstacles.Count > 0, $"{first.Obstacles.Count} obstacles in the sample chunk");
    }

    /// <summary>Walks the spine for SpineCheckLength: its ground slope and its tilt across the path stay walkable.</summary>
    private void TestSpine(TerrainGenerator generator)
    {
        float limit = generator.Settings.SpineMaxSlope;
        float start = generator.Settings.SpawnZ;
        float steepest = 0.0f;
        float steepestAt = 0.0f;
        float worstCross = 0.0f;
        int loose = 0;
        for (float d = 0.0f; d <= SpineCheckLength; d += SpineCheckStep)
        {
            float z = start - d;
            float x = generator.SpineX(z);
            float slope = generator.SlopeDegrees(x, z);
            if (slope > steepest)
            {
                steepest = slope;
                steepestAt = d;
            }

            // Sideways tilt: height change across the direction of travel, at the band's edge.
            Vector2 along = new Vector2(generator.SpineX(z - SpineCheckStep) - x, -SpineCheckStep).Normalized();
            Vector2 across = new Vector2(-along.Y, along.X) * generator.Settings.SpineHalfWidth * 0.5f;
            float rise = generator.Height(x + across.X, z + across.Y) - generator.Height(x - across.X, z - across.Y);
            float cross = Mathf.RadToDeg(Mathf.Atan(Mathf.Abs(rise) / (across.Length() * 2.0f)));
            worstCross = Mathf.Max(worstCross, cross);

            TerrainSurface surface = generator.SurfaceAt(x, z);
            if (surface == TerrainSurface.Scree || surface == TerrainSurface.Mud)
            {
                loose++;
            }
        }
        Check($"spine slope stays under {limit:0}° for {SpineCheckLength:0} m", steepest <= limit,
            $"steepest {steepest:0.0}° at {steepestAt:0} m");
        Check("spine isn't tilted sideways", worstCross <= MaxSpineCrossSlope, $"worst sideways tilt {worstCross:0.0}°");
        Check("spine has no scree or mud", loose == 0, $"{loose} loose samples");
    }

    private void TestObstacleClearance(TerrainGenerator generator)
    {
        TerrainSettings settings = generator.Settings;
        HashSet<Vector2I> chunks = new();
        for (float d = 0.0f; d <= SpineCheckLength; d += settings.ChunkSize * 0.5f)
        {
            float z = settings.SpawnZ - d;
            Vector2I centre = generator.ChunkAt(generator.SpineX(z), z);
            for (int dx = -1; dx <= 1; dx++)
            {
                chunks.Add(new Vector2I(centre.X + dx, centre.Y));
            }
        }

        int blocking = 0;
        int total = 0;
        float closest = float.MaxValue;
        foreach (Vector2I coord in chunks)
        {
            foreach (ObstacleData obstacle in generator.BuildChunk(coord, new Color[5], 0).Obstacles)
            {
                total++;
                float extent = obstacle.Size.Length() * 0.5f;
                if (obstacle.Kind == ObstacleKind.Trunk)
                {
                    extent = obstacle.Size.Y * 0.5f;
                }
                float gap = Mathf.Abs(obstacle.Position.X - generator.SpineX(obstacle.Position.Z)) - extent;
                closest = Mathf.Min(closest, gap);
                if (gap < settings.SpineHalfWidth)
                {
                    blocking++;
                }
            }
        }
        Check("no obstacles on the spine", blocking == 0 && total > 0,
            $"{total} obstacles near the spine, closest edge {closest:0.0} m from its centre");
    }

    /// <summary>The heightmap collider must sit exactly on the generated heights at grid points.</summary>
    private void TestCollision(TerrainStreamer streamer)
    {
        TerrainGenerator generator = streamer.Generator!;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        SplitMix64 random = new(7);
        Vector3 spawn = streamer.SpawnPoint;
        float cell = generator.Settings.CellSize;
        float worst = 0.0f;
        int misses = 0;
        for (int i = 0; i < CollisionSamples; i++)
        {
            float x = Mathf.Round((spawn.X + random.Range(-40.0f, 40.0f)) / cell) * cell;
            float z = Mathf.Round((spawn.Z + random.Range(-60.0f, 20.0f)) / cell) * cell;
            float expected = generator.Height(x, z);
            Godot.Collections.Dictionary hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(
                new Vector3(x, expected + 50.0f, z), new Vector3(x, expected - 50.0f, z), 1));
            if (hit.Count == 0)
            {
                misses++;
                continue;
            }
            // Obstacles can be hit first; only measure where the ray reached the ground.
            if (hit["collider"].AsGodotObject() is TerrainChunk)
            {
                worst = Mathf.Max(worst, Mathf.Abs(hit["position"].AsVector3().Y - expected));
            }
        }
        Check("collision matches generated heights", misses == 0 && worst <= MaxCollisionError,
            $"worst error {worst * 100.0f:0.00} cm, {misses} rays found nothing");
    }

    private void TestSurface(PlayerController player, TerrainGenerator generator)
    {
        Vector3 position = player.GlobalPosition;
        SurfaceType? expected = Streamer!.GetNodeOrNull<TerrainChunk>(ChunkName(generator, position))?.SurfaceAt(position);
        Check("carrier reads the terrain surface underfoot", expected != null && Mathf.IsEqualApprox(player.Grip, expected.Grip),
            $"grip {player.Grip:0.00}, terrain says {expected?.DisplayName ?? "nothing"}");
    }

    private async Task TestWalk(PlayerController player, TerrainStreamer streamer)
    {
        TerrainGenerator generator = streamer.Generator!;
        float startZ = player.GlobalPosition.Z;
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        float air = 0.0f;
        float longestAir = 0.0f;
        float worstDeviation = 0.0f;
        int mostChunks = 0;
        bool downed = false;
        float elapsed = 0.0f;

        while (elapsed < WalkTimeout && startZ - player.GlobalPosition.Z < WalkDistance)
        {
            Vector3 position = player.GlobalPosition;
            float aimZ = position.Z - SteerLookAhead;
            Vector3 target = new(generator.SpineX(aimZ), position.Y, aimZ);
            player.DirectionOverride = (target - position) with { Y = 0.0f };

            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            elapsed += dt;

            if (player.IsOnFloor())
            {
                air = 0.0f;
            }
            else
            {
                air += dt;
                longestAir = Mathf.Max(longestAir, air);
            }
            worstDeviation = Mathf.Max(worstDeviation, Mathf.Abs(player.GlobalPosition.X - generator.SpineX(player.GlobalPosition.Z)));
            mostChunks = Mathf.Max(mostChunks, streamer.LoadedCount);
            downed = downed || GameEvents.IsDowned;
        }
        player.DirectionOverride = null;

        float walked = startZ - player.GlobalPosition.Z;
        Check($"scripted walk follows the spine {WalkDistance:0} m down", walked >= WalkDistance && !downed,
            $"{walked:0} m in {elapsed:0} s, depth {streamer.Depth:0} m, downed {downed}");
        Check("walk stays grounded and on the spine", longestAir <= MaxWalkAirTime && worstDeviation <= MaxSpineDeviation,
            $"longest air time {longestAir:0.00} s, furthest from spine {worstDeviation:0.0} m");
        Check("streaming stays bounded", mostChunks <= MaxLoadedChunks, $"most chunks loaded {mostChunks}, now {streamer.LoadedCount}");
        Check("adding a chunk doesn't hitch", streamer.MaxApplyMilliseconds <= MaxApplyMilliseconds,
            $"slowest chunk add {streamer.MaxApplyMilliseconds:0.0} ms, last build {streamer.LastBuildMilliseconds:0} ms");
        Check("adding a chunk's trees and grass doesn't hitch", TerrainChunk.MaxPlantMilliseconds <= MaxApplyMilliseconds,
            $"slowest plants frame {TerrainChunk.MaxPlantMilliseconds:0.0} ms");

        RunState? run = GetNodeOrNull<RunState>("/root/RunState");
        Check("depth reaches RunState", run != null && run.Depth >= (int)streamer.Depth - 1 && run.Depth > 0,
            $"RunState.Depth {run?.Depth}, streamer {streamer.Depth:0} m");
    }

    private static string ChunkName(TerrainGenerator generator, Vector3 position)
    {
        Vector2I coord = generator.ChunkAt(position.X, position.Z);
        return $"Chunk_{coord.X}_{coord.Y}";
    }

    private async Task WaitSeconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
