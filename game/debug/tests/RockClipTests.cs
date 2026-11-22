using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The director walked into a wide rock slab to the waist: every rock drawn in the chunks (scattered rocks, outcrops, walls, boulders) gets
/// an exact copy of its mesh as a collider on a layer of its own; rays at knee to chest height walk
/// in from 8 sides and compare where the drawn rock starts with where the game's collision does.
/// Rock drawn more than a carrier's radius before the collision is a place he walks into the rock.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/RockClipTests.tscn
/// </summary>
public partial class RockClipTests : Node3D
{
    private int _told;
    private TerrainGenerator? _generator;
    private int _leaning;
    private int _rockBatches;
    private int _blendedBatches;
    private int _toldBatch;
    private int _leanTowards;
    private float _leanError;
    private readonly Color[] _palette = { new(0.56f, 0.57f, 0.6f), new(0.68f, 0.6f, 0.48f), new(0.42f, 0.62f, 0.3f),
        new(0.9f, 0.95f, 1), new(0.4f, 0.3f, 0.2f), new(0.45f, 0.36f, 0.26f) };

    private readonly record struct Rock(string Model, string Kind, Vector3 Centre, float Ground, float Top, float Reach, float Scale, bool Metres, TriangleMesh Shape, Transform3D At);

    public override async void _Ready()
    {
        TerrainSettings settings = new();
        TerrainGenerator generator = new(settings, 1);
        _generator = generator;
        bool loading = TerrainStreamer.Loading;
        TerrainStreamer.Loading = true;
        ShaderMaterial ground = new() { Shader = ResourceLoader.Load<Shader>("res://terrain/terrain_ground.gdshader") };
        ShaderMaterial grass = new() { Shader = ResourceLoader.Load<Shader>("res://terrain/forest_grass.gdshader") };
        Shader carpet = ResourceLoader.Load<Shader>("res://terrain/grass_carpet.gdshader");
        SurfaceType?[] surfaces = new SurfaceType?[Enum.GetValues<TerrainSurface>().Length];
        List<Rock> rocks = new();
        float circle = 1050.0f;
        for (int c = 0; c < 10; c++)
        {
            float depth = c == 0 ? 60.0f : c * circle + circle * 0.5f;
            float z = 0.0f;
            while (generator.DepthAt(generator.Height(generator.SpineX(z), z)) < depth && z > -80000.0f) { z -= 20.0f; }
            float x = generator.SpineX(z);
            for (int i = -1; i <= 1; i++)
            {
                Vector2I coord = generator.ChunkAt(x, z) + new Vector2I(i, 0);
                TerrainChunkData data = generator.BuildChunk(coord, _palette, 0);
                TerrainChunk chunk = new();
                chunk.Build(data, generator, surfaces, ground, WorldStyle.Rock, WorldStyle.For(Look.Bark), null, null, grass, carpet);
                AddChild(chunk);
                for (int f = 0; f < 600 && chunk.PlantsPending; f++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
                Collect(chunk, data, generator, rocks);
            }
        }
        TerrainStreamer.Loading = loading;
        for (int i = 0; i < 3; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
        int clipping = Probe(rocks);
        float meanError = _leaning > 0 ? _leanError / _leaning : 0.0f;
        // (A rock or so at a chunk's edge, where the chunk's own grid gives a slightly other slope, may be off.)
        bool leans = _leaning > 50 && meanError < 3.0f && _leanTowards >= _leaning * 0.999f;
        GD.Print($"{(leans ? "PASS" : "FAIL")}  rocks on slopes lean with the ground: {_leaning} rocks, {_leanTowards} turned toward its normal, mean {meanError:0.0}° off the intended lean");
        bool blendAll = _rockBatches > 0 && _blendedBatches == _rockBatches;
        GD.Print($"{(blendAll ? "PASS" : "FAIL")}  scattered rocks take the ground they stand on (rock_blend, the ground's colour and height per rock): {_blendedBatches}/{_rockBatches} batches");
        // A thin slab on ground that curves away under it may lift an edge a little past the rule (one in
        // a few thousand): that much is allowed, more means the rule or the boxes went wrong.
        int allowed = rocks.Count / 1000;
        bool pass = clipping <= allowed && leans && blendAll;
        GD.Print($"{(clipping <= allowed ? "PASS" : "FAIL")}  rocks keep him out where they stand proud of the ground: {clipping} of {rocks.Count} let him in ({allowed} allowed)");
        GD.Print(pass ? "ROCK CLIP TESTS: PASS" : $"ROCK CLIP TESTS: FAIL ({clipping} rocks he walks into, {allowed} allowed, leaning {leans})");
        GetTree().Quit(pass ? 0 : 1);
    }

    private void Collect(TerrainChunk chunk, TerrainChunkData data, TerrainGenerator generator, List<Rock> rocks)
    {
        foreach (PlantData plant in data.Plants)
        {
            if (plant.Kind != PlantKind.Rock && plant.Kind != PlantKind.Debris) { continue; }
            if (plant.Kind == PlantKind.Rock && !plant.Model.Contains("wall"))
            {
                // Leans with the slope: its up is RockSlopeFollow of the way from straight up to the ground's normal.
                float span = Mathf.Clamp(plant.Scale, 1.0f, 4.0f);
                Vector3 p = plant.Position;
                float gx = (generator.Height(p.X + span, p.Z) - generator.Height(p.X - span, p.Z)) / (2.0f * span);
                float gz = (generator.Height(p.X, p.Z + span) - generator.Height(p.X, p.Z - span)) / (2.0f * span);
                Vector3 normal = new Vector3(-gx, 1.0f, -gz).Normalized();
                Vector3 up = Basis.FromEuler(new Vector3(plant.Lean.X, 0.0f, plant.Lean.Y)) * Vector3.Up;
                float slope = Mathf.RadToDeg(normal.AngleTo(Vector3.Up));
                if (slope > 8.0f)
                {
                    _leaning++;
                    float wanted = slope * (1.0f - TerrainGenerator.RockSlopeFollow);
                    _leanError += Mathf.Abs(Mathf.RadToDeg(up.AngleTo(normal)) - wanted);
                    _leanTowards += up.AngleTo(normal) <= Vector3.Up.AngleTo(normal) + Mathf.DegToRad(0.5f) ? 1 : 0;
                }
            }
            if (ModelFit.FirstMesh(plant.Model, out Transform3D local) is not Mesh mesh) { continue; }
            Transform3D at = chunk.GlobalTransform * TerrainChunk.FitPlant(plant, mesh.GetAabb(), local, chunk.Position);
            AddShown(at, mesh, plant.Model.GetFile() + (plant.HeightInMetres ? "" : " (scaled)"), plant.Kind.ToString(), generator, rocks, plant.Scale, plant.HeightInMetres);
        }
        foreach (Node node in chunk.GetChildren())
        {
            if (node is MultiMeshInstance3D batch && node.Name.ToString().StartsWith("RockBatch", StringComparison.Ordinal))
            {
                _rockBatches++;
                bool shaded = batch.MaterialOverride is ShaderMaterial blend && blend.Shader.ResourcePath.EndsWith("rock_blend.gdshader", StringComparison.Ordinal);
                // (A headless run keeps no instance buffers to read back, so the data itself isn't checked here.)
                if (shaded && batch.Multimesh.UseCustomData && batch.Multimesh.InstanceCount > 0) { _blendedBatches++; }
                else if (_toldBatch++ < 2) { GD.Print($"NOTE {node.Name}: material {batch.MaterialOverride?.GetType().Name} {(batch.MaterialOverride as ShaderMaterial)?.Shader?.ResourcePath}, custom data {batch.Multimesh.UseCustomData}"); }
            }
            if (node is not StaticBody3D body || body.Name.ToString() != "Boulder") { continue; }
            foreach (Node part in body.GetChildren())
            {
                if (part is MeshInstance3D shown && shown.Mesh != null)
                {
                    AddShown(shown.GlobalTransform, shown.Mesh, "boulder", "Boulder", generator, rocks, 0, false);
                }
            }
        }
    }

    private void AddShown(Transform3D at, Mesh mesh, string model, string kind, TerrainGenerator generator, List<Rock> rocks, float scale, bool metres)
    {
        Aabb box = at * mesh.GetAabb();
        Vector3 centre = box.GetCenter();
        float groundY = generator.Height(centre.X, centre.Z);
        if (box.End.Y - groundY < 0.45f) { return; }
        rocks.Add(new Rock(model, kind, centre, groundY, box.End.Y, Mathf.Max(box.Size.X, box.Size.Z), scale, metres, mesh.GenerateTriangleMesh(), at));
    }

    private int Probe(List<Rock> rocks)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        Dictionary<string, (int Rocks, int Clipping, float Worst, string Example)> byModel = new();
        foreach (Rock rock in rocks)
        {
            float worst = 0.0f;
            string where = "";
            float top = Mathf.Min(rock.Top - rock.Ground, 1.3f);
            foreach (float h in new[] { 0.35f, 0.7f, 1.05f })
            {
                if (h > top - 0.05f) { continue; }
                for (int k = 0; k < 8; k++)
                {
                    float angle = k * Mathf.Pi / 4.0f;
                    Vector3 dir = new(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
                    Vector3 to = rock.Centre with { Y = rock.Ground + h };
                    Vector3 from = to - dir * (rock.Reach + 1.0f);
                    // Uphill on a steep slope the ray would start inside the ground: no place to walk in from.
                    if (_generator!.Height(from.X, from.Z) > from.Y - 0.1f) { continue; }
                    Transform3D back = rock.At.AffineInverse();
                    Godot.Collections.Dictionary meshHit = rock.Shape.IntersectRay(back * from, (back.Basis * dir).Normalized());
                    float shown = meshHit.Count > 0 ? from.DistanceTo(rock.At * meshHit["position"].AsVector3()) : -1.0f;
                    if (shown > from.DistanceTo(to)) { shown = -1.0f; }
                    if (shown < 0.0f) { continue; }
                    // Rock less proud of the ground where it's met than a slab that counts (TerrainChunk.SlabHeight)
                    // is stepped over: his feet may be in it a little, as in a pebble. 0.1 m more for the
                    // ground curving under a flat slab (the rule reads the slope at its middle).
                    Vector3 met = from + dir * shown;
                    if (met.Y - _generator.Height(met.X, met.Z) < TerrainChunk.SlabHeight + 0.1f) { continue; }
                    float solid = Hit(space, from, to, 1u);
                    float into = (solid < 0.0f ? from.DistanceTo(to) : solid) - shown;
                    if (into > worst) { worst = into; where = $"h {h:0.00} from {k * 45}°"; }
                }
            }
            string key = rock.Kind + " " + rock.Model;
            byModel.TryGetValue(key, out var entry);
            entry.Rocks++;
            if (worst > 0.35f)
            {
                entry.Clipping++;
                if (worst > entry.Worst)
                {
                    entry.Worst = worst;
                    entry.Example = $"at ({rock.Centre.X:0},{rock.Ground:0},{rock.Centre.Z:0}) {rock.Top - rock.Ground:0.0} m tall, {rock.Reach:0.0} m wide, scale {rock.Scale:0.00}, {where}";
                }
            }
            byModel[key] = entry;
            if (worst > 1.0f && _told++ < 6)
            {
                string near = "";
                foreach (Node cover in GetTree().GetNodesInGroup(TerrainChunk.CoverGroup))
                {
                    foreach (Node child in cover.GetChildren())
                    {
                        if (child is CollisionShape3D shape && shape.GlobalPosition.DistanceTo(rock.Centre) < rock.Reach + 1.0f && shape.Shape is BoxShape3D b)
                        {
                            near += $" [{cover.Name} at {shape.GlobalPosition - rock.Centre:0.00} size {b.Size:0.00} inTree {shape.IsInsideTree()} layer {((CollisionObject3D)cover).CollisionLayer} disabled {shape.Disabled}]";
                        }
                    }
                }
                GD.Print($"DETAIL {key} scale {rock.Scale:0.00} visible-by-rule {rock.Scale * 0.8f:0.00} top {rock.Top - rock.Ground:0.00} reach {rock.Reach:0.0} {where}:{(near.Length > 0 ? near : " no cover box near")}");
            }
        }
        int clipping = 0;
        foreach (var pair in byModel)
        {
            clipping += pair.Value.Clipping;
            GD.Print($"ROCK {pair.Key}: {pair.Value.Clipping}/{pair.Value.Rocks} let him in" +
                (pair.Value.Clipping > 0 ? $" (worst {pair.Value.Worst:0.00} m, {pair.Value.Example})" : ""));
        }
        return clipping;
    }

    private static float Hit(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to, uint mask)
    {
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to, mask);
        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        return hit.Count > 0 ? from.DistanceTo(hit["position"].AsVector3()) : -1.0f;
    }
}
