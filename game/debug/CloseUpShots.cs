using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Map;
using DownhillRoguelite.Narrative;
using DownhillRoguelite.Player;
using DownhillRoguelite.Progression;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug-only: photographs everything placed on the mountain up close, in daylight, one at a time,
/// on the route with the carrier standing beside it for scale: every set piece and landmark, every
/// animal that spawns, the night beasts, a station camp, the wayside sites, signs and markers,
/// cairns, shrines and lore marks. For checking the one style (WorldStyle) and sizes by eye.
/// `-- --shots-directory=C:/somewhere [--only=setpiece,animal,beast,camp,sign,lore,wayside] [--depth=260]`
/// (windowed: headless can't render).
/// </summary>
public partial class CloseUpShots : EnvironmentShots
{
    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        string directory = ProjectSettings.GlobalizePath("res://../../closeup-shots");
        string only = "";
        float depth = 260.0f;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--shots-directory=", StringComparison.Ordinal)) { directory = arg["--shots-directory=".Length..]; }
            if (arg.StartsWith("--only=", StringComparison.Ordinal)) { only = arg["--only=".Length..]; }
            if (arg.StartsWith("--depth=", StringComparison.Ordinal)) { depth = float.Parse(arg["--depth=".Length..], CultureInfo.InvariantCulture); }
        }
        Directory.CreateDirectory(directory);
        PlayerController player = GetNode<PlayerController>("World/Player");
        TerrainStreamer streamer = GetNode<TerrainStreamer>("World/Terrain");
        RunState run = GetNode<RunState>("/root/RunState");
        WorldClock? clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        await Seconds(2.0f);
        Camera3D camera = MakeShotCamera(player);
        if (run.Route == null || streamer.Generator == null)
        {
            GD.Print("FAIL  close-up shots: no route");
            GetTree().Quit(1);
            return;
        }
        TerrainGenerator generator = streamer.Generator;
        IRouteQuery route = run.Route;
        float z = route.StartZ - 5.0f;
        while (generator.DepthAt(route.RoutePoint(z).Y) < depth && z > -60000.0f) { z -= 10.0f; }
        Vector3 stage = route.RoutePoint(z);
        await StandAndLoad(player, streamer, clock, stage + new Vector3(0.0f, 0.0f, 12.0f));
        bool Wanted(string kind) => only.Length == 0 || only.Contains(kind, StringComparison.Ordinal);
        int count = 0;

        async Task Photograph(Node3D subject, string name, float minimumView = 3.0f)
        {
            Aabb bounds = WorldBounds(subject);
            if (bounds.Size == Vector3.Zero) { bounds = new Aabb(subject.GlobalPosition, Vector3.One); }
            Vector3 centre = bounds.GetCenter();
            float size = Mathf.Max(Mathf.Max(bounds.Size.X, bounds.Size.Z), Mathf.Max(bounds.Size.Y, minimumView));
            // The carrier stands beside it for scale, just clear of its outline.
            Vector3 beside = new(centre.X + bounds.Size.X * 0.5f + 1.2f, 0.0f, centre.Z + 1.0f);
            beside.Y = generator.Height(beside.X, beside.Z);
            player.Teleport(beside + Vector3.Up * 0.2f, 0.0f);
            float distance = size * 0.95f + 1.6f;
            Vector3 eye = new(centre.X - distance * 0.45f, 0.0f, centre.Z + distance * 0.9f);
            eye.Y = Mathf.Max(generator.Height(eye.X, eye.Z) + 1.7f, bounds.Position.Y + bounds.Size.Y * 0.6f + 0.8f);
            await Seconds(0.6f);
            string file = Path.Combine(directory, $"{count:D3}_{Safe(name)}.png");
            await Shoot(camera, eye, centre, file);
            GD.Print($"CLOSEUP {name}: {bounds.Size.X:0.0} x {bounds.Size.Y:0.0} x {bounds.Size.Z:0.0} m, foot {bounds.Position.Y - generator.Height(centre.X, centre.Z):+0.00;-0.00} m off the ground");
            count++;
        }

        Vector3 Ground(Vector3 at) => at with { Y = generator.Height(at.X, at.Z) };

        if (Wanted("setpiece"))
        {
            foreach (string file in DirAccess.GetFilesAt(SetPiecePlacer.Folder))
            {
                string name = file.Replace(".remap", "");
                if (!name.EndsWith(".tres", StringComparison.Ordinal) || ResourceLoader.Load(SetPiecePlacer.Folder + "/" + name) is not SetPiece piece) { continue; }
                SetPieceNode node = new();
                AddChild(node);
                node.Build(piece, Ground(stage), 0.0f, route, 0.0f);
                await Photograph(node, (piece.Kind == SetPieceKind.Landmark ? "landmark_" : "setpiece_") + piece.Id, 4.0f);
                node.QueueFree();
            }
        }
        if (Wanted("animal"))
        {
            WildlifeTable? table = ResourceLoader.Load<WildlifeTable>("res://wildlife/default_wildlife.tres");
            foreach (WildlifeSpecies species in table?.Species ?? Array.Empty<WildlifeSpecies>())
            {
                bool spawns = false;
                for (int c = 0; c < 10; c++) { spawns |= species.LivesIn(c); }
                if (!spawns) { continue; }
                WildlifeAnimal animal = new();
                animal.Setup(species, table!, 7);
                AddChild(animal);
                animal.GlobalPosition = Ground(stage);
                await Seconds(0.1f);
                animal.ProcessMode = ProcessModeEnum.Disabled;
                animal.GlobalPosition = Ground(stage);
                await Photograph(animal, "animal_" + species.DisplayName, 1.5f);
                animal.QueueFree();
            }
        }
        if (Wanted("beast"))
        {
            BeastRoster? beasts = ResourceLoader.Load<BeastRoster>("res://wildlife/default_beasts.tres");
            foreach (BeastSpecies beast in beasts?.Beasts ?? Array.Empty<BeastSpecies>())
            {
                if (beast.Model == null) { continue; }
                Node3D holder = new() { Name = "Beast" };
                AddChild(holder);
                holder.GlobalPosition = Ground(stage);
                Node3D model = beast.Model.Instantiate<Node3D>();
                NightBeast.Shape(model, beast);
                holder.AddChild(model);
                await Photograph(holder, "beast_" + beast.DisplayName, 2.0f);
                holder.QueueFree();
            }
        }
        if (Wanted("camp"))
        {
            StationSite station = new();
            AddChild(station);
            station.GlobalPosition = Ground(stage);
            station.Build(1);
            await Photograph(station, "station_camp", 6.0f);
            station.QueueFree();
        }
        if (Wanted("wayside"))
        {
            int n = 0;
            foreach (string file in DirAccess.GetFilesAt("res://environment/wayside/resources"))
            {
                if (ResourceLoader.Load("res://environment/wayside/resources/" + file.Replace(".remap", "")) is not WaysideDefinition definition) { continue; }
                WaysideSite site = new();
                site.Setup(definition, "closeup" + n, Array.Empty<string>(), false);
                AddChild(site);
                site.GlobalPosition = Ground(stage);
                await Photograph(site, "wayside_" + file.Replace(".tres", "").Replace(".remap", ""), 2.0f);
                site.QueueFree();
                n++;
            }
        }
        if (Wanted("sign"))
        {
            Node3D[] signs = { new RouteSign(), new JunctionSignpost(), new PathMarker(), new WaypointCairn(), new WaypointMarker(),
                new DownhillRoguelite.Progression.RelicShrine(), new DownhillRoguelite.Progression.RelicAltar(),
                new DownhillRoguelite.Inventory.HandCart() };
            foreach (Node3D sign in signs)
            {
                AddChild(sign);
                sign.GlobalPosition = Ground(stage);
                await Photograph(sign, "sign_" + sign.GetType().Name, 2.0f);
                sign.QueueFree();
            }
        }
        if (Wanted("lore"))
        {
            foreach (LoreForm form in Enum.GetValues<LoreForm>())
            {
                LoreMark mark = new();
                mark.Setup(new LoreEntry { Id = "closeup_" + form, Heading = "", Text = "", Form = form });
                AddChild(mark);
                mark.GlobalPosition = Ground(stage);
                await Photograph(mark, "lore_" + form, 1.5f);
                mark.QueueFree();
            }
        }
        GD.Print("PASS  close-up shots saved in " + directory + " (" + count + ")");
        GetTree().Quit(0);
    }

    private static string Safe(string name)
    {
        char[] chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-') { chars[i] = '_'; }
        }
        return new string(chars);
    }

    /// <summary>Every drawn mesh under <paramref name="root"/>, in world space.</summary>
    private static Aabb WorldBounds(Node root)
    {
        Aabb bounds = new();
        bool first = true;
        foreach (Node node in root.FindChildren("*", "GeometryInstance3D", true, false))
        {
            if (node is not GeometryInstance3D geometry || !geometry.IsVisibleInTree() || geometry is Label3D || geometry is Sprite3D || geometry is GpuParticles3D) { continue; }
            // Light beams and glows don't count towards the thing's size.
            if (geometry is MeshInstance3D drawn && DownhillRoguelite.Environment.WorldStyle.IsEffect(drawn.MaterialOverride ?? (drawn.Mesh?.GetSurfaceCount() > 0 ? drawn.GetSurfaceOverrideMaterial(0) ?? drawn.Mesh.SurfaceGetMaterial(0) : null))) { continue; }
            Aabb box = geometry.GlobalTransform * geometry.GetAabb();
            if (box.Size.LengthSquared() < 0.0001f || box.Size.Length() > 200.0f) { continue; }
            bounds = first ? box : bounds.Merge(box);
            first = false;
        }
        return bounds;
    }
}
