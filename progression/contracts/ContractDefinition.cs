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

    /// <summary>Whether a board at this station may offer it.</summary>
    public bool EligibleAt(int station)
    {
        return station >= MinStation && station >= 1;
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
