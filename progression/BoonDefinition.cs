using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// A blessing offered at a station (M12): chosen once, it lasts for the rest of the run. Each
/// field multiplies the matching RunModifiers value (1 = no change); ColdResistance adds.
/// </summary>
[GlobalClass]
public partial class BoonDefinition : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export] public Color Glow { get; set; } = new(1.0f, 0.75f, 0.4f);

    [ExportGroup("Effects (multipliers)")]
    [Export] public float StaminaMax { get; set; } = 1.0f;
    [Export] public float StaminaRecovery { get; set; } = 1.0f;
    [Export] public float WalkDrain { get; set; } = 1.0f;
    [Export] public float LoadDrain { get; set; } = 1.0f;
    [Export] public float NeedsDrain { get; set; } = 1.0f;
    [Export] public float Grip { get; set; } = 1.0f;
    [Export] public float Balance { get; set; } = 1.0f;
    [Export] public float WalkSpeed { get; set; } = 1.0f;
    [Export] public float SleepHeal { get; set; } = 1.0f;
    /// <summary>Added to cold resistance (0.3 = 30 % of the cold kept off).</summary>
    [Export] public float ColdResistance { get; set; }

    public void ApplyTo(RunModifiers modifiers)
    {
        modifiers.StaminaMax *= StaminaMax;
        modifiers.StaminaRecovery *= StaminaRecovery;
        modifiers.WalkDrain *= WalkDrain;
        modifiers.LoadDrain *= LoadDrain;
        modifiers.NeedsDrain *= NeedsDrain;
        modifiers.Grip *= Grip;
        modifiers.Balance *= Balance;
        modifiers.WalkSpeed *= WalkSpeed;
        modifiers.SleepHeal *= SleepHeal;
        modifiers.ColdResistance = Mathf.Min(0.9f, modifiers.ColdResistance + ColdResistance);
    }
}
