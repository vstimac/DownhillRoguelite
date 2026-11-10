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

    /// <summary>What it does, plainly, from its numbers: "Stamina +20%", "Hunger and thirst 15% slower".</summary>
    public string Effects()
    {
        System.Collections.Generic.List<string> parts = new();
        void Add(float factor, string more, string less)
        {
            int percent = Mathf.RoundToInt(Mathf.Abs(factor - 1.0f) * 100.0f);
            if (percent == 0)
            {
                return;
            }
            if (factor > 1.0f)
            {
                parts.Add(string.Format(more, percent));
            }
            else
            {
                parts.Add(string.Format(less, percent));
            }
        }
        Add(StaminaMax, "Stamina +{0}%", "Stamina -{0}%");
        Add(StaminaRecovery, "Stamina recovers {0}% faster", "Stamina recovers {0}% slower");
        Add(WalkDrain, "Climbing costs {0}% more stamina", "Climbing costs {0}% less stamina");
        Add(LoadDrain, "The load costs {0}% more stamina", "The load costs {0}% less stamina");
        Add(NeedsDrain, "Hunger and thirst {0}% faster", "Hunger and thirst {0}% slower");
        Add(Grip, "Grip +{0}%", "Grip -{0}%");
        Add(Balance, "Balance +{0}%", "Balance -{0}%");
        Add(WalkSpeed, "Walk {0}% faster", "Walk {0}% slower");
        Add(SleepHeal, "Sleep heals {0}% more", "Sleep heals {0}% less");
        if (ColdResistance > 0.0f)
        {
            parts.Add($"Cold {Mathf.RoundToInt(ColdResistance * 100.0f)}% weaker");
        }
        return string.Join(" · ", parts);
    }

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
