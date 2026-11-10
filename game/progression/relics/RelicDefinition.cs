using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// A relic (progression/relics/resources/): an original, story-rich object left somewhere on the
/// mountain, carried as a real item (its ItemPath) with real mass and cells. Found on a side path,
/// it can be left where it lies, carried, or left at a station's altar to be studied: run score, and
/// a "studied" entry with one of its interpretations that later runs remember. Values are INITIAL
/// TUNING.
/// </summary>
[GlobalClass]
public partial class RelicDefinition : Resource
{
    /// <summary>Stable id (never shown to the player).</summary>
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    /// <summary>What the carrier notes on first finding it (one or two sentences).</summary>
    [Export(PropertyHint.MultilineText)] public string SeenText { get; set; } = "";
    /// <summary>Readings of it once studied, true and contradictory; one per run, chosen by the seed.</summary>
    [Export] public string[] Interpretations { get; set; } = System.Array.Empty<string>();
    /// <summary>The item it is carried as (an ItemDefinition resource).</summary>
    [Export(PropertyHint.File, "*.tres")] public string ItemPath { get; set; } = "";
    /// <summary>The circle it belongs to (0 = Limbo), or -1 for any (it is described, not enforced, for now).</summary>
    [Export] public int CircleAffinity { get; set; } = -1;
    /// <summary>The stages (0: summit to Station 1) it may lie in, inclusive.</summary>
    [Export] public Vector2I StageRange { get; set; } = new(1, 3);
    /// <summary>Score for leaving it at an altar intact (in proportion when damaged).</summary>
    [Export] public int BaseScore { get; set; } = 40;
    /// <summary>How easily knocks damage it: 0 never, 1 ordinary, 2 glass-like.</summary>
    [Export] public float Fragility { get; set; }
    /// <summary>Chance it lies on the mountain in a run.</summary>
    [Export(PropertyHint.Range, "0,1")] public float SpawnChance { get; set; } = 1.0f;
    [Export] public bool CanDeliver { get; set; } = true;

    /// <summary>The cargo id its carried copy is tagged with (ItemDefinition.CargoInstanceId).</summary>
    public string CargoId => "relic:" + Id;

    /// <summary>What is wrong with the definition, or an empty string if nothing is.</summary>
    public string Problem()
    {
        if (Id.Length == 0)
        {
            return "has no id";
        }
        if (DisplayName.Length == 0 || SeenText.Length == 0)
        {
            return Id + " has no name or seen text";
        }
        if (Interpretations.Length == 0)
        {
            return Id + " has no interpretation";
        }
        if (StageRange.X < 0 || StageRange.Y < StageRange.X)
        {
            return Id + " has no stage it can lie in";
        }
        if (BaseScore < 0 || Fragility < 0.0f)
        {
            return Id + " has a negative score or fragility";
        }
        if (ItemPath.Length == 0 || !ResourceLoader.Exists(ItemPath))
        {
            return Id + ": item " + ItemPath + " doesn't exist";
        }
        return "";
    }
}
