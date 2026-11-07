using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// What a difficulty changes. One resource per difficulty in core/difficulty/ (easy, normal, hard);
/// RunState picks the one for the run. See docs/survival-loop.md.
/// </summary>
[GlobalClass]
public partial class DifficultySettings : Resource
{
    [Export] public string DisplayName { get; set; } = "Normal";
    /// <summary>Real minutes per 24-hour in-game day.</summary>
    [Export] public float DayLengthMinutes { get; set; } = 36.0f;
    /// <summary>Multiplier on hunger and thirst drain.</summary>
    [Export] public float NeedsDrainMultiplier { get; set; } = 1.0f;
    /// <summary>Multiplier on how many supplies are scattered in the world.</summary>
    [Export] public float SupplyMultiplier { get; set; } = 1.0f;
    /// <summary>0 = mild nights, 1 = full cold and darkness (used from M7).</summary>
    [Export] public float NightHarshness { get; set; } = 1.0f;
    /// <summary>Deeper is harder (M12): extra hunger and thirst drain per circle descended (0.08 = +8 %).</summary>
    [Export] public float NeedsDrainPerCircle { get; set; } = 0.08f;
    /// <summary>Extra night cold and weather chill per circle descended.</summary>
    [Export] public float ColdPerCircle { get; set; } = 0.1f;
}
