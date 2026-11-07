using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// The playable characters, in the order the start screen offers them. The first available one is the
/// default. Characters whose model isn't in the project (not added yet, or kept out of git) are skipped.
/// </summary>
[GlobalClass]
public partial class CarrierRoster : Resource
{
    public const string DefaultPath = "res://art/characters/roster.tres";

    [Export] public string[] CharacterPaths { get; set; } = System.Array.Empty<string>();

    public static CarrierRoster? Load(string path = DefaultPath)
    {
        if (!ResourceLoader.Exists(path))
        {
            return null;
        }
        return ResourceLoader.Load(path) as CarrierRoster;
    }

    /// <summary>Every character whose model is present, in roster order.</summary>
    public List<CarrierCharacter> Available()
    {
        List<CarrierCharacter> list = new();
        foreach (string path in CharacterPaths)
        {
            if (ResourceLoader.Exists(path) && ResourceLoader.Load(path) is CarrierCharacter character && character.Available)
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
