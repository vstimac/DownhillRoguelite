using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Long jumps down the mountain for testing: to a station, a depth, a circle or a biome, further
/// down the route, or to the nearest spider web. The ground at the far end is built at once (a
/// hitch) so the carrier lands on it; everything else round it streams in as it does on foot.
/// /where says where the carrier is; /stations and /biomes list the places.
/// </summary>
public partial class DevConsole
{
    /// <summary>The words /tp takes for the long jumps.</summary>
    private static readonly string[] TravelWords = { "station", "depth", "circle", "biome", "down", "web" };
    /// <summary>How far ahead the route is searched for a depth, in m along it.</summary>
    private const float RouteSearchLength = 200000.0f;
    private const float RouteSearchStep = 4.0f;
    /// <summary>Set down this far uphill of a station's fire, in m (not in it).</summary>
    private const float StationStandOff = 4.0f;
    /// <summary>A circle or biome is entered this far below where it begins, in m of descent (past the blend).</summary>
    private const float PastTheBorder = 150.0f;
    /// <summary>Stood this far in front of a web, facing it, in m.</summary>
    private const float WebStandOff = 2.5f;

    private void RegisterTravel()
    {
        Register("where", "where", "Says how deep the carrier is: circle, biome, stage and the next station.", args => Where());
        Register("stations", "stations [count]", "Lists the stations down the route (12): depth and circle.", args =>
        {
            TerrainStreamer streamer = Streamer();
            int count = Mathf.Clamp((int)Number(args, 0, 12.0f), 1, 60);
            StringBuilder text = new();
            for (int i = 1; i <= count; i++)
            {
                Vector3 at = streamer.Generator!.Station(i);
                float depth = DepthOf(streamer, streamer.Generator.Height(at.X, at.Z));
                text.Append($"{i}: {depth:0} m, {CircleName(depth)}\n");
            }
            return text.ToString().TrimEnd();
        });
        Register("biomes", "biomes", "Lists the biomes and the circles, with the depth each begins at.", args =>
        {
            TerrainStreamer streamer = Streamer();
            StringBuilder text = new();
            foreach (Biome biome in streamer.Generator!.Settings.Biomes)
            {
                text.Append($"{biome.Name}: from {biome.FromDepth:0} m ({CircleName(biome.FromDepth)})\n");
            }
            return text.ToString().TrimEnd();
        });
    }

    private static bool IsTravel(string[] args) => args.Length >= 1 && TravelWords.Contains(args[0].ToLowerInvariant());

    /// <summary>/tp station|depth|circle|biome|down|web ...</summary>
    private string Travel(string[] args)
    {
        PlayerController player = Carrier();
        string word = args[0].ToLowerInvariant();
        if (word == "web")
        {
            return ToWeb(player, Find<TerrainStreamer>(GetTree().Root));
        }
        TerrainStreamer streamer = Streamer();
        TerrainGenerator generator = streamer.Generator!;
        string rest = string.Join(' ', args.Skip(1));
        switch (word)
        {
            case "station":
            {
                int index = (int)Number(args, 1, 1.0f);
                if (index < 1)
                {
                    return "Stations count from 1 (the first below the start).";
                }
                Vector3 fire = generator.Station(index);
                Vector3 uphill = fire + Vector3.Back * StationStandOff;
                return Land(player, streamer, new Vector3(uphill.X, 0.0f, uphill.Z), fire, $"station {index}");
            }
            case "depth":
                return ToDepth(player, streamer, Number(args, 1, 0.0f), "");
            case "down":
                return ToDepth(player, streamer, DepthOf(streamer, player.GlobalPosition.Y) + Number(args, 1, 500.0f), "");
            case "circle":
            {
                CircleTable circles = Circles();
                int circle = -1;
                if (int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                {
                    circle = number - 1;
                }
                else
                {
                    circle = Array.FindIndex(circles.Names, name => name.Equals(rest, StringComparison.OrdinalIgnoreCase));
                }
                if (circle < 0 || circle >= circles.Names.Length)
                {
                    return $"Circles: 1-{circles.Names.Length} or a name ({string.Join(", ", circles.Names)}).";
                }
                return ToDepth(player, streamer, circle * circles.DepthPerCircle + PastTheBorder, circles.Names[circle]);
            }
            case "biome":
            {
                Biome? biome = generator.Settings.Biomes.FirstOrDefault(b => b.Name.Equals(rest, StringComparison.OrdinalIgnoreCase))
                    ?? generator.Settings.Biomes.FirstOrDefault(b => b.Name.Contains(rest, StringComparison.OrdinalIgnoreCase));
                if (biome == null || rest.Length == 0)
                {
                    return "Biomes: " + string.Join(", ", generator.Settings.Biomes.Select(b => b.Name)) + ".";
                }
                return ToDepth(player, streamer, biome.FromDepth + PastTheBorder, biome.Name);
            }
            default:
                return ToWeb(player, streamer);
        }
    }

    /// <summary>Onto the route where it first goes <paramref name="depth"/> m below the start.</summary>
    private string ToDepth(PlayerController player, TerrainStreamer streamer, float depth, string what)
    {
        TerrainGenerator generator = streamer.Generator!;
        if (depth < 0.0f)
        {
            return "Depth is in metres below the start (0 or more).";
        }
        float z = generator.Settings.SpawnZ;
        float end = z - RouteSearchLength;
        while (z > end && DepthOf(streamer, generator.Height(generator.SpineX(z), z)) < depth)
        {
            z -= RouteSearchStep;
        }
        if (z <= end)
        {
            return $"The route doesn't reach {depth:0} m within {RouteSearchLength / 1000.0f:0} km.";
        }
        Vector3 at = new(generator.SpineX(z), 0.0f, z);
        Vector3 ahead = new(generator.SpineX(z - 10.0f), 0.0f, z - 10.0f);
        string label = what.Length > 0 ? what + ", " : "";
        return Land(player, streamer, at, ahead, $"{label}{depth:0} m down");
    }

    private string ToWeb(PlayerController player, TerrainStreamer? streamer)
    {
        SpiderWebs? webs = Find<SpiderWebs>(GetTree().Root);
        SpiderWeb? nearest = webs?.Webs.Where(GodotObject.IsInstanceValid)
            .OrderBy(web => web.GlobalPosition.DistanceSquaredTo(player.GlobalPosition)).FirstOrDefault();
        if (nearest == null)
        {
            int from = webs?.MinCircle ?? 3;
            return $"No spider web near. They hang between close trees from {Circles().NameOf(from)} (circle {from + 1}) down, within about 45 m: /tp circle {from + 1}, walk into a wood and try again.";
        }
        Vector3 front = nearest.GlobalPosition + nearest.GlobalBasis.Z * WebStandOff;
        Vector3 other = nearest.GlobalPosition - nearest.GlobalBasis.Z * WebStandOff;
        // The side nearer the carrier, so the jump doesn't land behind a trunk they came round.
        if (other.DistanceSquaredTo(player.GlobalPosition) < front.DistanceSquaredTo(player.GlobalPosition))
        {
            front = other;
        }
        return Land(player, streamer, new Vector3(front.X, 0.0f, front.Z), nearest.GlobalPosition, $"a spider web {player.GlobalPosition.DistanceTo(nearest.GlobalPosition):0} m away (walk into it)");
    }

    /// <summary>Builds the ground there, then sets the carrier on it facing <paramref name="look"/>.</summary>
    private string Land(PlayerController player, TerrainStreamer? streamer, Vector3 at, Vector3 look, string what)
    {
        int built = 0;
        at.Y = player.GlobalPosition.Y;
        if (streamer?.Generator != null)
        {
            built = streamer.BuildAround(at);
            at.Y = streamer.Generator.Height(at.X, at.Z);
        }
        Godot.Collections.Dictionary hit = player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 60.0f, at + Vector3.Down * 60.0f, 1));
        if (hit.Count > 0)
        {
            at = (Vector3)hit["position"];
        }
        Vector3 facing = look - at;
        float yaw = facing.LengthSquared() > 0.01f ? Mathf.Atan2(-facing.X, -facing.Z) : player.Rotation.Y;
        player.Teleport(at + Vector3.Up * 0.15f, yaw);
        string ground = built > 0 ? $", {built} pieces of ground built" : "";
        string where = streamer?.Generator != null ? " " + Where() : "";
        return $"Moved to {what}: ({at.X:0}, {at.Y:0}, {at.Z:0}){ground}.{where}";
    }

    private string Where()
    {
        PlayerController player = Carrier();
        TerrainStreamer streamer = Streamer();
        TerrainGenerator generator = streamer.Generator!;
        Vector3 at = player.GlobalPosition;
        float depth = DepthOf(streamer, at.Y);
        string biome = "";
        Biome[] biomes = generator.Settings.Biomes;
        if (biomes.Length > 0)
        {
            generator.BiomesAt(Mathf.Max(depth, 0.0f), out int from, out int to, out float blend);
            biome = blend > 0.05f && blend < 0.95f ? $"{biomes[from].Name} into {biomes[to].Name}" : biomes[blend >= 0.5f ? to : from].Name;
        }
        int next = generator.NextStationIndex(at.Z);
        Vector3 station = generator.Station(next);
        float away = new Vector2(station.X - at.X, station.Z - at.Z).Length();
        return $"{depth:0} m down, {CircleName(depth)}, biome {biome}, stage {next - 1}, station {next} is {away:0} m away.";
    }

    private static float DepthOf(TerrainStreamer streamer, float height) => streamer.SpawnPoint.Y - height;

    private string CircleName(float depth)
    {
        CircleTable circles = Circles();
        int circle = circles.CircleAt(Mathf.Max(depth, 0.0f));
        return $"circle {circle + 1} {circles.NameOf(circle)}";
    }

    private CircleTable Circles() => GetNodeOrNull<RunState>("/root/RunState")?.Circles ?? new CircleTable();

    private TerrainStreamer Streamer()
    {
        TerrainStreamer? streamer = Find<TerrainStreamer>(GetTree().Root);
        if (streamer?.Generator == null)
        {
            throw new InvalidOperationException("no streamed mountain in this scene");
        }
        return streamer;
    }
}
