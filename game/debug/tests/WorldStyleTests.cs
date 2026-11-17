using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Map;
using DownhillRoguelite.Narrative;
using DownhillRoguelite.Progression;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// One style and solid things (the director's "uncollidable, different style" report): builds the
/// mountain in every circle (chunks across the valley), every set piece, a station, the wayside
/// sites, signs, lore marks and every animal that spawns, then checks that
/// 1. every surface drawn is in the palette (environment/WorldStyle): no art pack's own flat
///    colours, no unlisted material, no saturated or glaring tint; animals are smooth-shaded;
/// 2. a carrier-sized capsule walked into samples of every solid kind (boulders, hero rocks,
///    spires, outcrops, walls, big bushes and hedges, stumps and gear, fallen trunks, trees, dead
///    trees, set-piece parts, landmarks, cairns) is stopped by it, while grass, flowers, pebbles
///    and small shrubs have nothing to bump into.
/// Prints a STYLE line per kind (its palette entries) and a SOLID line per kind (blocked samples).
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/WorldStyleTests.tscn
/// </summary>
public partial class WorldStyleTests : Node3D
{
    private const float ProbeRadius = 0.3f;
    private const float ProbeHeight = 1.75f;
    private const int SamplesPerKind = 12;

    private readonly List<string> _failures = new();
    private readonly Dictionary<string, SortedSet<string>> _styles = new();
    private readonly Dictionary<string, List<string>> _offStyle = new();
    private readonly Dictionary<string, (int Tried, int Blocked, List<string> Misses)> _solid = new();
    private readonly List<Rid> _ground = new();
    private readonly Color[] _palette = { new(0.56f, 0.57f, 0.6f), new(0.68f, 0.6f, 0.48f), new(0.42f, 0.62f, 0.3f),
        new(0.9f, 0.95f, 1), new(0.4f, 0.3f, 0.2f), new(0.45f, 0.36f, 0.26f) };

    /// <summary>A thing to walk into: <paramref name="Reach"/> is how far its outline can stand from its middle (the probe starts outside it).</summary>
    private readonly record struct Sample(string Kind, Vector3 Centre, float Ground, float Reach, string Body);

    public override async void _Ready()
    {
        try { await Run(); }
        catch (Exception error) { Check("world style tests complete without exception", false, error.ToString()); }
        if (_failures.Count == 0) { GD.Print("WORLD STYLE TESTS: PASS"); }
        else { GD.Print("WORLD STYLE TESTS: FAIL (" + _failures.Count + ")"); }
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        TerrainSettings settings = new();
        TerrainGenerator generator = new(settings, 1);
        TerrainRoute route = new(generator);
        bool loading = TerrainStreamer.Loading;
        TerrainStreamer.Loading = true;
        List<Sample> samples = new();
        List<Sample> soft = new();
        await BuildChunks(generator, samples, soft);
        TerrainStreamer.Loading = loading;
        BuildSetPieces(route, samples);
        BuildCamp(route, samples);
        BuildAnimals();
        // Let the physics server take the new bodies before probing.
        for (int i = 0; i < 3; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }

        CheckStyle();
        Probe(samples);
        CheckSoft(soft);
    }

    // ---- The mountain: chunks across the valley in every circle -------------------------------------

    private async Task BuildChunks(TerrainGenerator generator, List<Sample> samples, List<Sample> soft)
    {
        ShaderMaterial ground = new() { Shader = ResourceLoader.Load<Shader>("res://terrain/terrain_ground.gdshader") };
        ShaderMaterial grass = new() { Shader = ResourceLoader.Load<Shader>("res://terrain/forest_grass.gdshader") };
        Shader carpet = ResourceLoader.Load<Shader>("res://terrain/grass_carpet.gdshader");
        Material rock = WorldStyle.Rock;
        Material bark = WorldStyle.For(Look.Bark);
        SurfaceType?[] surfaces = new SurfaceType?[Enum.GetValues<TerrainSurface>().Length];
        float circle = 1050.0f;
        Dictionary<string, int> seen = new();
        for (int c = 0; c < 10; c++)
        {
            float depth = c == 0 ? 60.0f : c * circle + circle * 0.5f;
            float z = 0.0f;
            while (generator.DepthAt(generator.Height(generator.SpineX(z), z)) < depth && z > -80000.0f) { z -= 20.0f; }
            float x = generator.SpineX(z);
            int across = c == 2 ? 2 : 1;
            for (int i = -across; i <= across; i++)
            {
                for (int j = 0; j <= (c == 2 ? 1 : 0); j++)
                {
                    Vector2I coord = generator.ChunkAt(x, z) + new Vector2I(i, j);
                    TerrainChunkData data = generator.BuildChunk(coord, _palette, 0);
                    Collect(data, generator, samples, soft, seen);
                    TerrainChunk chunk = new();
                    chunk.Build(data, generator, surfaces, ground, rock, bark, null, null, grass, carpet);
                    AddChild(chunk);
                    _ground.Add(chunk.GetRid());
                    for (int f = 0; f < 600 && chunk.PlantsPending; f++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
                    Check("the chunk at " + coord + " finishes its plants", !chunk.PlantsPending, "circle " + c);
                    Walk(chunk, "terrain");
                }
            }
        }
    }

    /// <summary>Picks samples of every kind from a chunk's plan (the solid ones to walk into, the soft ones to walk through).</summary>
    private static void Collect(TerrainChunkData data, TerrainGenerator generator, List<Sample> samples, List<Sample> soft, Dictionary<string, int> seen)
    {
        void Add(List<Sample> list, Sample sample)
        {
            seen.TryGetValue(sample.Kind, out int count);
            if (count >= SamplesPerKind) { return; }
            seen[sample.Kind] = count + 1;
            list.Add(sample);
        }
        foreach (ObstacleData obstacle in data.Obstacles)
        {
            float groundY = generator.Height(obstacle.Position.X, obstacle.Position.Z);
            float radius = Mathf.Max(obstacle.Size.X, obstacle.Size.Z) * 0.75f;
            if (obstacle.Kind == ObstacleKind.Trunk)
            {
                // Lying along the slope: walk into its middle.
                Add(samples, new Sample("fallen trunk", obstacle.Position, groundY, Mathf.Max(obstacle.Size.X, obstacle.Size.Y) * 0.55f, "Trunk"));
                continue;
            }
            string kind = obstacle.Size.Y >= 6.0f ? "spire" : Mathf.Max(obstacle.Size.X, obstacle.Size.Z) >= 3.5f ? "hero rock" : "boulder";
            if (obstacle.Size.Y * 0.7f < WorldStyle.KneeHeight) { continue; }
            Add(samples, new Sample(kind, obstacle.Position, groundY, radius, "Boulder"));
        }
        foreach (PlantData plant in data.Plants)
        {
            float visible = plant.Scale - TerrainChunk.GroundSink(plant);
            float groundY = plant.Position.Y;
            string file = plant.Model.GetFile();
            switch (plant.Kind)
            {
                case PlantKind.Tree:
                    groundY = generator.Height(plant.Position.X, plant.Position.Z);
                    float trunk = plant.TrunkRadius > 0 ? plant.TrunkRadius : Mathf.Clamp(plant.Scale * 0.035f, 0.12f, 0.4f);
                    Add(samples, new Sample(file.StartsWith("snag", StringComparison.Ordinal) ? "dead tree" : "tree", plant.Position with { Y = groundY }, groundY, trunk, "TreeTrunks"));
                    break;
                case PlantKind.Rock:
                    if (!plant.HeightInMetres) { break; }
                    string rockKind = file.Contains("wall", StringComparison.Ordinal) ? "wall" : "outcrop rock";
                    if (TerrainChunk.BlocksCarrier(PlantKind.Rock, visible))
                    {
                        Add(samples, new Sample(rockKind, plant.Position, groundY, CoverReach(plant), "CoverRocks"));
                    }
                    else if (visible < WorldStyle.KneeHeight * 0.6f)
                    {
                        Add(soft, new Sample("loose stone", plant.Position, groundY, 0.0f, ""));
                    }
                    break;
                case PlantKind.Shrub:
                    if (TerrainChunk.BlocksCarrier(PlantKind.Shrub, visible))
                    {
                        Add(samples, new Sample("big bush or hedge", plant.Position, groundY, CoverReach(plant), "CoverBushes"));
                    }
                    else if (visible < 0.9f)
                    {
                        Add(soft, new Sample("small shrub", plant.Position, groundY, 0.0f, ""));
                    }
                    break;
                case PlantKind.Debris:
                    if (TerrainChunk.BlocksCarrier(PlantKind.Debris, visible))
                    {
                        Add(samples, new Sample("stump or gear", plant.Position, groundY, CoverReach(plant), "CoverWood"));
                    }
                    break;
                case PlantKind.Grass:
                case PlantKind.Flower:
                case PlantKind.Pebble:
                    Add(soft, new Sample(plant.Kind.ToString().ToLowerInvariant(), plant.Position, groundY, 0.0f, ""));
                    break;
            }
        }
    }

    /// <summary>How far a ground-cover plant's outline reaches from its foot (flat slabs are far wider than tall).</summary>
    private static float CoverReach(PlantData plant)
    {
        if (ModelFit.FirstMesh(plant.Model, out Transform3D local) is not Mesh mesh) { return plant.Scale * 1.5f; }
        Aabb bounds = local * mesh.GetAabb();
        float scale = plant.HeightInMetres ? plant.Scale / Mathf.Max(bounds.Size.Y, 0.01f) : plant.Scale;
        return Mathf.Max(bounds.Size.X, bounds.Size.Z) * scale * 0.75f;
    }

    // ---- Set pieces, the camp, signs, wayside, lore ---------------------------------------------------

    private void BuildSetPieces(TerrainRoute route, List<Sample> samples)
    {
        int index = 0;
        foreach (string file in DirAccess.GetFilesAt(SetPiecePlacer.Folder))
        {
            string name = file.Replace(".remap", "");
            if (!name.EndsWith(".tres", StringComparison.Ordinal) || ResourceLoader.Load(SetPiecePlacer.Folder + "/" + name) is not SetPiece piece) { continue; }
            // Far off the chunks built above, in a row: nothing else stands near them.
            Vector3 centre = new(3000.0f + index * 40.0f, 0.0f, -300.0f);
            centre.Y = route.GroundHeight(centre.X, centre.Z);
            SetPieceNode node = new();
            AddChild(node);
            node.Build(piece, centre, 0.0f, route, 0.0f);
            string kind = piece.Kind == SetPieceKind.Landmark ? "landmark" : "set piece";
            Walk(node, kind + " " + piece.Id);
            int solidParts = 0;
            foreach (Node child in node.GetChildren())
            {
                if (child is not StaticBody3D body) { continue; }
                solidParts++;
                Node3D holder = (Node3D)body.GetChild(0);
                Aabb bounds = holder.Transform * ModelFit.Bounds(holder);
                Vector3 aim = bounds.GetCenter();
                bool shaped = body.FindChildren("*", "CollisionShape3D", false, false) is { Count: > 0 } found
                    && ((CollisionShape3D)found[0]).Shape is ConcavePolygonShape3D;
                if (shaped)
                {
                    aim += bounds.Size.X >= bounds.Size.Z ? new Vector3(bounds.Size.X * 0.35f, 0.0f, 0.0f) : new Vector3(0.0f, 0.0f, bounds.Size.Z * 0.35f);
                }
                Vector3 middle = body.GlobalTransform * aim;
                float groundY = (body.GlobalTransform * (aim with { Y = bounds.Position.Y })).Y;
                if (bounds.Size.Y < WorldStyle.KneeHeight) { continue; }
                samples.Add(new Sample(kind, middle with { Y = groundY }, groundY, Mathf.Max(bounds.Size.X, bounds.Size.Z) * 0.75f, "Solid@" + body.GetInstanceId()));
            }
            // Every part that looks solid and stands taller than a knee has a body.
            int shouldBlock = 0;
            foreach (SetPiecePart part in piece.Parts)
            {
                if (part != null && SetPieceNode.Blocks(part, part.Height)) { shouldBlock++; }
            }
            Check("set piece " + piece.Id + ": every solid-looking part above knee height blocks", solidParts == shouldBlock && solidParts == node.SolidCount,
                solidParts + " solid of " + piece.Parts.Count + " parts");
            index++;
        }
    }

    private void BuildCamp(TerrainRoute route, List<Sample> samples)
    {
        Vector3 at = new(3000.0f, 0.0f, -500.0f);
        at.Y = route.GroundHeight(at.X, at.Z);
        StationSite station = new() { Position = at };
        AddChild(station);
        station.Build(1);
        Walk(station, "station camp");

        int n = 0;
        foreach (string file in DirAccess.GetFilesAt("res://environment/wayside/resources"))
        {
            if (ResourceLoader.Load("res://environment/wayside/resources/" + file.Replace(".remap", "")) is not WaysideDefinition definition) { continue; }
            WaysideSite site = new() { Position = at + new Vector3(30.0f + n * 4.0f, 0.0f, 0.0f) };
            site.Setup(definition, "style" + n, Array.Empty<string>(), false);
            AddChild(site);
            Walk(site, "wayside site");
            n++;
        }
        Node3D[] signs = { new RouteSign(), new JunctionSignpost(), new PathMarker(), new WaypointCairn(), new WaypointMarker(), new RelicShrine() };
        for (int i = 0; i < signs.Length; i++)
        {
            signs[i].Position = at + new Vector3(-30.0f - i * 6.0f, 0.0f, 0.0f);
            AddChild(signs[i]);
            Walk(signs[i], "sign and marker " + signs[i].GetType().Name);
        }
        WaypointCairn cairn = (WaypointCairn)signs[3];
        samples.Add(new Sample("waypoint cairn", cairn.GlobalPosition, cairn.GlobalPosition.Y, 0.5f, "Solid"));
        int f = 0;
        foreach (LoreForm form in Enum.GetValues<LoreForm>())
        {
            LoreMark mark = new() { Position = at + new Vector3(0.0f, 0.0f, 30.0f + f * 5.0f) };
            mark.Setup(new LoreEntry { Id = "style_" + form, Heading = "", Text = "", Form = form });
            AddChild(mark);
            Walk(mark, "lore mark " + form);
            f++;
        }
    }

    private void BuildAnimals()
    {
        WildlifeTable? table = ResourceLoader.Load<WildlifeTable>("res://wildlife/default_wildlife.tres");
        if (table == null)
        {
            Check("the wildlife table loads", false, "");
            return;
        }
        int spawning = 0;
        float worstFlat = 0.0f;
        string worst = "";
        foreach (WildlifeSpecies species in table.Species)
        {
            bool spawns = false;
            for (int c = 0; c < 10; c++) { spawns |= species.LivesIn(c); }
            string source = species.Model?.ResourcePath ?? "";
            if (!spawns) { continue; }
            spawning++;
            Check("animal " + species.DisplayName + " is not a toy-block model", !source.Contains("cube_pets", StringComparison.Ordinal), source);
            WildlifeAnimal animal = new();
            animal.Setup(species, table, 7);
            AddChild(animal);
            animal.GlobalPosition = new Vector3(3200.0f + spawning * 5.0f, -1000.0f, 0.0f);
            Walk(animal, "animal " + species.DisplayName);
            float flat = FlatShare(animal);
            if (flat > worstFlat) { worstFlat = flat; worst = species.DisplayName; }
        }
        Check("every animal that spawns is smooth-shaded (no paper facets)", worstFlat < 0.05f && spawning > 5,
            spawning + " species, most faceted " + worst + " " + (worstFlat * 100.0f).ToString("0", CultureInfo.InvariantCulture) + " % split normals");
        BeastRoster? beasts = ResourceLoader.Load<BeastRoster>("res://wildlife/default_beasts.tres");
        if (beasts == null)
        {
            Check("the night beasts load", false, "");
            return;
        }
        int n = 0;
        foreach (BeastSpecies beast in beasts.Beasts)
        {
            string source = beast.Model?.ResourcePath ?? "";
            Check("night beast " + beast.DisplayName + " is not a toy-block model", beast.Model != null && !source.Contains("cube_pets", StringComparison.Ordinal), source);
            if (beast.Model == null) { continue; }
            Node3D model = beast.Model.Instantiate<Node3D>();
            NightBeast.Shape(model, beast);
            model.Position = new Vector3(3400.0f + n * 6.0f, -1000.0f, 0.0f);
            AddChild(model);
            Walk(model, "night beast " + beast.DisplayName);
            float flat = FlatShare(model);
            Check("night beast " + beast.DisplayName + " is smooth-shaded", flat < 0.05f, (flat * 100.0f).ToString("0", CultureInfo.InvariantCulture) + " % split normals");
            n++;
        }
    }

    /// <summary>The share of an animal's corners whose normal differs from another corner at the same spot (folded facets).</summary>
    private static float FlatShare(Node root)
    {
        int corners = 0;
        int split = 0;
        foreach (Node node in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (node is not MeshInstance3D mesh || mesh.Mesh is not ArrayMesh array) { continue; }
            for (int s = 0; s < array.GetSurfaceCount(); s++)
            {
                Godot.Collections.Array arrays = array.SurfaceGetArrays(s);
                Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                Vector3[] normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                Dictionary<Vector3I, Vector3> first = new();
                for (int v = 0; v < vertices.Length && v < normals.Length; v++)
                {
                    Vector3I key = (Vector3I)(vertices[v] * 2000.0f).Round();
                    corners++;
                    if (first.TryGetValue(key, out Vector3 normal)) { if (normal.Dot(normals[v]) < 0.99f) { split++; } }
                    else { first[key] = normals[v]; }
                }
            }
        }
        return corners == 0 ? 0.0f : (float)split / corners;
    }

    // ---- The style rule ------------------------------------------------------------------------------

    /// <summary>Every drawn surface under <paramref name="root"/>, by kind (the node's name for the terrain's batches).</summary>
    private void Walk(Node root, string kind)
    {
        foreach (Node node in root.FindChildren("*", "GeometryInstance3D", true, false))
        {
            if (node is MeshInstance3D mesh && mesh.Mesh != null)
            {
                string what = kind == "terrain" ? KindOf(mesh) : kind;
                for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
                {
                    Note(what, mesh.MaterialOverride ?? mesh.GetSurfaceOverrideMaterial(i) ?? mesh.Mesh.SurfaceGetMaterial(i), mesh.Name);
                }
            }
            else if (node is MultiMeshInstance3D batch && batch.Multimesh?.Mesh != null)
            {
                string what = kind == "terrain" ? KindOf(batch) : kind;
                Mesh drawn = batch.Multimesh.Mesh;
                for (int i = 0; i < drawn.GetSurfaceCount(); i++)
                {
                    Note(what, batch.MaterialOverride ?? drawn.SurfaceGetMaterial(i), batch.Name);
                }
            }
        }
    }

    private static string KindOf(GeometryInstance3D node)
    {
        string name = node.Name.ToString();
        Node? parent = node.GetParent();
        if (name.StartsWith("Ground", StringComparison.Ordinal)) { return "terrain surface"; }
        if (parent is StaticBody3D body && parent is not TerrainChunk)
        {
            return body.FindChildren("*", "CollisionShape3D", false, false).Count > 0
                && ((CollisionShape3D)body.FindChildren("*", "CollisionShape3D", false, false)[0]).Shape is CylinderShape3D
                ? "fallen trunks" : "boulders, hero rocks and spires";
        }
        if (name.StartsWith("TreeBatch", StringComparison.Ordinal)) { return "trees and dead trees"; }
        if (name.StartsWith("GrassBatch", StringComparison.Ordinal)) { return "grass tufts and reeds"; }
        if (name.StartsWith("FlowerBatch", StringComparison.Ordinal)) { return "flowers"; }
        if (name.StartsWith("PebbleBatch", StringComparison.Ordinal)) { return "pebbles"; }
        if (name.StartsWith("ShrubBatch", StringComparison.Ordinal)) { return "bushes, hedges and dwarf pines"; }
        if (name.StartsWith("RockBatch", StringComparison.Ordinal)) { return "rocks, outcrops, scree and walls"; }
        if (name.StartsWith("DebrisBatch", StringComparison.Ordinal)) { return "stumps, logs, mushrooms and lost gear"; }
        return "grass carpet";
    }

    private void Note(string kind, Material? material, string node)
    {
        if (!_styles.TryGetValue(kind, out SortedSet<string>? names)) { names = new SortedSet<string>(); _styles[kind] = names; }
        if (WorldStyle.IsEffect(material)) { names.Add("(light effect)"); return; }
        if (WorldStyle.IsInStyle(material, out string why))
        {
            string entry = WorldStyle.NameOf(material);
            if (entry.Length == 0) { entry = material is ShaderMaterial shader ? shader.Shader.ResourcePath.GetFile().GetBaseName() : "generated vertex colours"; }
            names.Add(entry);
            return;
        }
        if (!_offStyle.TryGetValue(kind, out List<string>? list)) { list = new List<string>(); _offStyle[kind] = list; }
        if (list.Count < 4) { list.Add(node + ": " + why); }
    }

    private void CheckStyle()
    {
        foreach ((string kind, SortedSet<string> names) in _styles)
        {
            GD.Print("STYLE " + kind + ": " + string.Join(", ", names));
        }
        foreach ((string kind, SortedSet<string> _) in _styles)
        {
            bool clean = !_offStyle.TryGetValue(kind, out List<string>? off);
            Check("every surface of " + kind + " is in the palette", clean, clean ? "" : string.Join("; ", off!));
        }
        foreach (string kind in new[] { "terrain surface", "trees and dead trees", "grass tufts and reeds", "bushes, hedges and dwarf pines",
            "rocks, outcrops, scree and walls", "boulders, hero rocks and spires", "fallen trunks", "stumps, logs, mushrooms and lost gear", "flowers", "station camp" })
        {
            Check("the style check saw " + kind, _styles.ContainsKey(kind), "");
        }
    }

    // ---- Walking into things -------------------------------------------------------------------------

    /// <summary>
    /// Walks a carrier-sized capsule at ankle height into each sample from eight sides. A sample passes when
    /// the probe is stopped by that thing itself. A neighbour of its own (a boulder, another set-piece part)
    /// standing in the way is set aside and the walk repeated; when every side is closed by the shared cover
    /// of the chunk (rocks or bushes all round), the sample is "crowded": blocked, but not by itself.
    /// </summary>
    private void Probe(List<Sample> samples)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        CapsuleShape3D capsule = new() { Radius = ProbeRadius, Height = ProbeHeight };
        Dictionary<string, int> crowded = new();
        foreach (Sample sample in samples)
        {
            if (!_solid.TryGetValue(sample.Kind, out var tally)) { tally = (0, 0, new List<string>()); }
            Godot.Collections.Array<Rid> exclude = new(_ground);
            bool blocked = false;
            bool open = false;
            string seen = "";
            float start = sample.Reach + ProbeRadius + 0.5f;
            for (int d = 0; d < 8 && !blocked; d++)
            {
                float angle = d * Mathf.Pi * 0.25f + 0.4f;
                Vector3 direction = new(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
                Vector3 from = new Vector3(sample.Centre.X, sample.Ground + 0.05f + ProbeHeight * 0.5f, sample.Centre.Z) + direction * start;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    PhysicsShapeQueryParameters3D query = new()
                    {
                        Shape = capsule,
                        Transform = new Transform3D(Basis.Identity, from),
                        Motion = -direction * start,
                        Exclude = exclude,
                    };
                    float[] fractions = space.CastMotion(query);
                    if (fractions.Length < 2 || fractions[1] >= 1.0f) { open = true; seen = "nothing"; break; }
                    query.Transform = new Transform3D(Basis.Identity, from - direction * start * fractions[1]);
                    Godot.Collections.Dictionary rest = space.GetRestInfo(query);
                    if (rest.Count == 0) { open = true; seen = "nothing"; break; }
                    if (InstanceFromId(rest["collider_id"].AsUInt64()) is not CollisionObject3D hit) { open = true; seen = "a freed body"; break; }
                    if (Matches(hit, rest["shape"].AsInt32(), sample)) { blocked = true; break; }
                    if (Shared(hit)) { seen = hit.Name + " (shared cover)"; break; }
                    exclude.Add(hit.GetRid());
                }
            }
            tally.Tried++;
            if (blocked) { tally.Blocked++; }
            else if (!open)
            {
                crowded.TryGetValue(sample.Kind, out int c);
                crowded[sample.Kind] = c + 1;
            }
            else if (tally.Misses.Count < 3)
            {
                tally.Misses.Add(sample.Centre.ToString("0.0") + " (reach " + sample.Reach.ToString("0.0", CultureInfo.InvariantCulture) + " m, " + NearestCover(sample) + ")");
            }
            _solid[sample.Kind] = tally;
        }
        foreach ((string kind, var tally) in _solid)
        {
            crowded.TryGetValue(kind, out int c);
            GD.Print("SOLID " + kind + ": " + tally.Blocked + " of " + tally.Tried + " samples stop the carrier"
                + (c > 0 ? " (" + c + " more hemmed in by other cover)" : ""));
            Check("the carrier can't walk through " + kind, tally.Blocked > 0 && tally.Blocked + c == tally.Tried && c * 4 <= tally.Tried,
                tally.Blocked + " of " + tally.Tried + (tally.Misses.Count > 0 ? ", walked through at " + string.Join(" ", tally.Misses) : ""));
        }
        foreach (string kind in new[] { "boulder", "hero rock", "spire", "outcrop rock", "wall", "big bush or hedge", "stump or gear", "fallen trunk", "tree", "dead tree", "set piece", "landmark", "waypoint cairn" })
        {
            Check("collision was tried on " + kind, _solid.ContainsKey(kind), "");
        }
    }

    private string NearestCover(Sample sample)
    {
        float best = float.MaxValue;
        string what = "no cover within 3 m";
        foreach (Node node in GetTree().GetNodesInGroup(TerrainChunk.CoverGroup))
        {
            foreach (Node child in node.GetChildren())
            {
                if (child is not CollisionShape3D shape || shape.Shape is not BoxShape3D box) { continue; }
                Vector3 at = shape.GlobalPosition;
                float far = new Vector2(at.X - sample.Centre.X, at.Z - sample.Centre.Z).Length();
                if (far < best && far < 3.0f)
                {
                    best = far;
                    what = "nearest " + node.Name + " box " + box.Size.ToString("0.00") + " at " + far.ToString("0.00", CultureInfo.InvariantCulture)
                        + " m, from " + (at.Y - box.Size.Y * 0.5f - sample.Ground).ToString("0.00", CultureInfo.InvariantCulture) + " m over the foot";
                }
            }
        }
        return what;
    }

    /// <summary>One body holding many things (a chunk's trunks, rocks, bushes or wood): can't be set aside for one sample.</summary>
    private static bool Shared(Node hit)
    {
        return hit.GetParent() is TerrainChunk && (hit.IsInGroup(TerrainChunk.CoverGroup) || hit.Name == "TreeTrunks");
    }

    private static bool Matches(CollisionObject3D hit, int shapeIndex, Sample sample)
    {
        string name = hit.Name.ToString();
        string body = sample.Body;
        if (body is "Boulder" or "Trunk")
        {
            // Obstacle bodies share a name, so Godot renames all but the first: match them by what they are.
            if (hit.GetParent() is not TerrainChunk || Shared(hit)) { return false; }
            Godot.Collections.Array<Node> shapes = hit.FindChildren("*", "CollisionShape3D", false, false);
            bool isTrunk = shapes.Count > 0 && ((CollisionShape3D)shapes[0]).Shape is CylinderShape3D;
            return shapes.Count > 0 && isTrunk == (body == "Trunk")
                && new Vector2(hit.GlobalPosition.X - sample.Centre.X, hit.GlobalPosition.Z - sample.Centre.Z).Length() < 0.05f;
        }
        if (body.StartsWith("Solid@", StringComparison.Ordinal))
        {
            return hit.GetInstanceId().ToString(CultureInfo.InvariantCulture) == body.Substring(6);
        }
        if (!name.StartsWith(body, StringComparison.Ordinal)) { return false; }
        if (!Shared(hit)) { return true; }
        // A shared body: the shape that stopped the probe must be this sample's own.
        if (hit.ShapeOwnerGetOwner(hit.ShapeFindOwner(shapeIndex)) is not CollisionShape3D own) { return false; }
        return new Vector2(own.GlobalPosition.X - sample.Centre.X, own.GlobalPosition.Z - sample.Centre.Z).Length() < 0.75f;
    }

    /// <summary>
    /// Grass, flowers, pebbles, loose stones and small shrubs have no body of their own: no cover shape is
    /// centred on them. (One may still grow in the lee of a rock, inside that rock's box.)
    /// </summary>
    private void CheckSoft(List<Sample> soft)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        Dictionary<string, (int Tried, int Clear)> tally = new();
        foreach (Sample sample in soft)
        {
            PhysicsPointQueryParameters3D query = new() { Position = sample.Centre + Vector3.Up * 0.25f, Exclude = new Godot.Collections.Array<Rid>(_ground) };
            bool clear = true;
            foreach (Godot.Collections.Dictionary hit in space.IntersectPoint(query, 8))
            {
                if (hit["collider"].AsGodotObject() is not StaticBody3D body || !body.IsInGroup(TerrainChunk.CoverGroup)) { continue; }
                uint owner = body.ShapeFindOwner(hit["shape"].AsInt32());
                if (body.ShapeOwnerGetOwner(owner) is CollisionShape3D shape
                    && new Vector2(shape.GlobalPosition.X - sample.Centre.X, shape.GlobalPosition.Z - sample.Centre.Z).Length() < 0.15f)
                {
                    clear = false;
                }
            }
            tally.TryGetValue(sample.Kind, out var t);
            tally[sample.Kind] = (t.Tried + 1, t.Clear + (clear ? 1 : 0));
        }
        foreach ((string kind, var t) in tally)
        {
            GD.Print("SOFT " + kind + ": " + t.Clear + " of " + t.Tried + " samples walk-through");
            Check("the carrier walks through " + kind, t.Clear == t.Tried, t.Clear + " of " + t.Tried);
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print("PASS  " + name + (detail.Length > 0 ? ": " + detail : ""));
        }
        else
        {
            GD.Print("FAIL  " + name + (detail.Length > 0 ? ": " + detail : ""));
            _failures.Add(name);
        }
    }
}
