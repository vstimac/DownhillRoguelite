using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// The playable characters, in the order the start screen offers them. The first available one is the
/// default. Characters whose model isn't in the project (not added yet, or kept out of git) are skipped.
/// </summary>
[Tool]
[GlobalClass]
public partial class CarrierRoster : Resource
{
    public const string DefaultPath = "res://art/characters/roster.tres";

    [Export] public string[] CharacterPaths { get; set; } = System.Array.Empty<string>();

    /// <summary>
    /// Rosters and characters stay loaded once read. Loading them afresh each time let .NET finalize a
    /// character on its own thread while the game was loading the same file again, which crashed the
    /// game (Godot: "gchandle.is_released"); held here, they are never finalized mid-game.
    /// </summary>
    private static readonly Dictionary<string, CarrierRoster?> Rosters = new();
    private static readonly Dictionary<string, CarrierCharacter?> Characters = new();

    public static CarrierRoster? Load(string path = DefaultPath)
    {
        if (Rosters.TryGetValue(path, out CarrierRoster? cached))
        {
            return cached;
        }
        CarrierRoster? roster = null;
        if (ResourceLoader.Exists(path))
        {
            roster = ResourceLoader.Load(path) as CarrierRoster;
        }
        Rosters[path] = roster;
        return roster;
    }

    /// <summary>The character at <paramref name="path"/> (kept loaded; null if it isn't a character).</summary>
    public static CarrierCharacter? LoadCharacter(string path)
    {
        if (Characters.TryGetValue(path, out CarrierCharacter? cached))
        {
            return cached;
        }
        CarrierCharacter? character = null;
        if (ResourceLoader.Exists(path))
        {
            character = ResourceLoader.Load(path) as CarrierCharacter;
        }
        Characters[path] = character;
        return character;
    }

    /// <summary>Every character whose model is present, in roster order.</summary>
    public List<CarrierCharacter> Available()
    {
        List<CarrierCharacter> list = new();
        foreach (string path in CharacterPaths)
        {
            if (LoadCharacter(path) is CarrierCharacter character && character.Available)
            {
                list.Add(character);
            }
        }
        return list;
    }

    /// <summary>The character with this id if it's available, else the first available one (null if none).</summary>
    public CarrierCharacter? Pick(string id)
    {
        List<CarrierCharacter> list = Available();
        foreach (CarrierCharacter character in list)
        {
            if (character.Id == id)
            {
                return character;
            }
        }
        if (list.Count > 0)
        {
            return list[0];
        }
        return null;
    }
}
