using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// A job a station's board can offer (authored in progression/contracts/resources/): what to carry,
/// how far, and what delivering it is worth. The cargo is a real item from inventory/items/; the
/// carrier gets a copy of their own when they take the job. Values are INITIAL TUNING.
/// </summary>
[GlobalClass]
public partial class ContractDefinition : Resource
{
    /// <summary>Stable id (never shown to the player).</summary>
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public ContractKind Kind { get; set; } = ContractKind.Courier;
    /// <summary>Who pinned the note (shown on the board).</summary>
    [Export] public string Giver { get; set; } = "";
    /// <summary>The note's text, one or two short sentences.</summary>
    [Export(PropertyHint.MultilineText)] public string OfferText { get; set; } = "";
    /// <summary>The cargo: an ItemDefinition resource.</summary>
    [Export(PropertyHint.File, "*.tres")] public string CargoItemPath { get; set; } = "";
    /// <summary>How many stations further down it must go (1: the next one).</summary>
    [Export] public int StationsAhead { get; set; } = 1;
    /// <summary>First station whose board may offer it.</summary>
    [Export] public int MinStation { get; set; } = 1;
    /// <summary>Score for delivering it.</summary>
    [Export] public int Score { get; set; } = 30;
    /// <summary>How likely it is picked against other eligible contracts.</summary>
    [Export] public float Weight { get; set; } = 1.0f;
    /// <summary>How easily the cargo is damaged: 0 never (a stone), 1 ordinary, 2 glass.</summary>
    [Export] public float Fragility { get; set; } = 1.0f;
    /// <summary>The condition (0–100) it must arrive in to count; below it the job fails on hand-over. 0: any.</summary>
    [Export(PropertyHint.Range, "0,100")] public float MinCondition { get; set; }
    /// <summary>Days (on the world clock) from taking it to its deadline; 0: no deadline.</summary>
    [Export] public float DeadlineDays { get; set; }
    /// <summary>First circle (0 = Limbo) whose stations may offer it.</summary>
    [Export] public int MinCircle { get; set; }
    /// <summary>Last circle whose stations may offer it; -1: no limit.</summary>
    [Export] public int MaxCircle { get; set; } = -1;

    /// <summary>Whether a board at this station, in this circle, may offer it.</summary>
    public bool EligibleAt(int station, int circle = 0)
    {
        if (station < MinStation || station < 1 || circle < MinCircle)
        {
            return false;
        }
        return MaxCircle < 0 || circle <= MaxCircle;
    }

    /// <summary>What is wrong with the definition, or an empty string if nothing is.</summary>
    public string Problem()
    {
        if (Id.Length == 0)
        {
            return "has no id";
        }
        if (DisplayName.Length == 0)
        {
            return Id + " has no name";
        }
        if (StationsAhead < 1)
        {
            return Id + " must go at least one station down";
        }
        if (Score < 0 || Weight < 0.0f)
        {
            return Id + " has a negative score or weight";
        }
        if (Fragility < 0.0f || MinCondition < 0.0f || MinCondition > 100.0f || DeadlineDays < 0.0f)
        {
            return Id + " has a negative fragility or deadline, or a condition outside 0-100";
        }
        if (MaxCircle >= 0 && MaxCircle < MinCircle)
        {
            return Id + " has no circle it can be offered in";
        }
        if (CargoItemPath.Length == 0 || !ResourceLoader.Exists(CargoItemPath))
        {
            return Id + ": cargo " + CargoItemPath + " doesn't exist";
        }
        if (ResourceLoader.Load(CargoItemPath) is not Resource cargo || cargo.Get("ItemId").AsString().Length == 0)
        {
            return Id + ": cargo " + CargoItemPath + " is not an item";
        }
        return "";
    }
}
