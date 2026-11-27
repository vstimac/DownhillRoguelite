using System.Collections.Generic;
using System.Linq;
using System.Text;
using DownhillRoguelite.Player;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// /enemies and /spawn for every creature on the mountain: the humanoid enemies (EnemyDirector), the
/// night beasts (BeastDirector: the she-wolf, the big cats, the giant spider) and the animals
/// (WildlifeSpawner: grazers, small animals and the wolf packs). An animal or beast is named by its
/// display name in lower case with _ for spaces and dashes (she_wolf, giant_spider, polar).
/// </summary>
public partial class DevConsole
{
    /// <summary>Side by side, this far apart (m), when more than one is brought.</summary>
    private const float SpawnSpacing = 2.5f;
    private const int MostAtOnce = 20;

    private static string IdOf(string displayName) => displayName.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

    private string Kinds()
    {
        StringBuilder text = new();
        EnemyDirector? enemies = Find<EnemyDirector>(GetTree().Root);
        BeastDirector? beasts = Find<BeastDirector>(GetTree().Root);
        WildlifeSpawner? wildlife = Find<WildlifeSpawner>(GetTree().Root);
        if (enemies != null)
        {
            text.Append("Enemies: ").Append(string.Join("  ", enemies.Kinds.Select(kind => kind.Id).Distinct())).Append('\n');
        }
        if (beasts?.Roster != null)
        {
            text.Append("Night beasts (they hunt only at night: /time 22 first): ").Append(string.Join("  ", beasts.Roster.Beasts.Select(b => IdOf(b.DisplayName)).Distinct())).Append('\n');
        }
        if (wildlife?.Table != null)
        {
            text.Append("Animals: ").Append(string.Join("  ", wildlife.Table.Species.Select(a => IdOf(a.DisplayName)).Distinct())).Append('\n');
        }
        return text.Length > 0 ? text.ToString().TrimEnd() + "\n/spawn <kind> [metres] [count]" : "Nothing can be spawned in this scene.";
    }

    private string Spawn(string[] args)
    {
        if (args.Length == 0)
        {
            return "Usage: /spawn <kind> [metres] [count]. /enemies lists the kinds.";
        }
        string id = IdOf(args[0]);
        float distance = Number(args, 1, 10.0f);
        int count = Mathf.Clamp((int)Number(args, 2, 1.0f), 1, MostAtOnce);

        EnemyDirector? enemies = Find<EnemyDirector>(GetTree().Root);
        if (enemies != null && enemies.Kinds.Any(kind => kind.Id == id))
        {
            string name = "";
            int brought = 0;
            for (int i = 0; i < count; i++)
            {
                HumanoidEnemy? enemy = enemies.SpawnAhead(id, distance + i * SpawnSpacing);
                if (enemy != null)
                {
                    name = enemy.Species?.DisplayName ?? id;
                    brought++;
                }
            }
            return Brought(name, brought);
        }

        PlayerController player = Carrier();
        BeastDirector? beasts = Find<BeastDirector>(GetTree().Root);
        BeastSpecies? beast = beasts?.Roster?.Beasts.FirstOrDefault(b => IdOf(b.DisplayName) == id);
        if (beasts != null && beast != null)
        {
            int brought = 0;
            foreach (Vector3 ground in Spots(player, distance, count))
            {
                beasts.BringAt(beast, ground);
                brought++;
            }
            string night = beasts.ForceNight ? "" : " By day it does not hunt and soon leaves: /time 22 first.";
            return Brought(beast.DisplayName, brought) + night;
        }

        WildlifeSpawner? wildlife = Find<WildlifeSpawner>(GetTree().Root);
        WildlifeSpecies? animal = wildlife?.Table?.Species.FirstOrDefault(a => IdOf(a.DisplayName) == id);
        if (wildlife != null && animal != null)
        {
            WildlifeHerd? herd = count > 1 ? new WildlifeHerd() : null;
            int brought = 0;
            foreach (Vector3 ground in Spots(player, distance, count))
            {
                WildlifeAnimal spawned = wildlife.Spawn(animal, ground);
                herd?.Join(spawned);
                brought++;
            }
            return Brought(animal.DisplayName, brought);
        }
        return $"No kind '{args[0]}'. /enemies lists them.";
    }

    private static string Brought(string name, int count)
    {
        if (count == 0)
        {
            return $"No ground ahead for {name}.";
        }
        return count == 1 ? $"{name} is here." : $"{count} × {name} are here.";
    }

    /// <summary>Points on the ground <paramref name="distance"/> m ahead of the carrier, side by side.</summary>
    private static IEnumerable<Vector3> Spots(PlayerController player, float distance, int count)
    {
        Vector3 ahead = -player.GlobalBasis.Z;
        ahead.Y = 0.0f;
        ahead = ahead.LengthSquared() > 0.001f ? ahead.Normalized() : Vector3.Forward;
        Vector3 side = ahead.Cross(Vector3.Up);
        PhysicsDirectSpaceState3D space = player.GetWorld3D().DirectSpaceState;
        for (int i = 0; i < count; i++)
        {
            float offset = (i - (count - 1) * 0.5f) * SpawnSpacing;
            Vector3 at = player.GlobalPosition + ahead * distance + side * offset;
            Godot.Collections.Dictionary hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 50.0f, at + Vector3.Down * 100.0f, 1));
            if (hit.Count > 0)
            {
                yield return (Vector3)hit["position"];
            }
        }
    }
}
