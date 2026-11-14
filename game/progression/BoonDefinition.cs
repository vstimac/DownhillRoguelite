using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// A blessing offered at a station (M12): chosen once, it lasts for the rest of the run. Each
/// field multiplies the matching RunModifiers value (1 = no change); ColdResistance adds. A
/// conditional boon (Beta 3.3) only works in its situation: RunState switches it on and off.
/// </summary>
[GlobalClass]
public partial class BoonDefinition : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export] public Color Glow { get; set; } = new(1.0f, 0.75f, 0.4f);
    /// <summary>When it works; anything but Always switches on and off with the situation.</summary>
    [Export] public BoonCondition Condition { get; set; } = BoonCondition.Always;

    /// <summary>Works only in one situation (at night, carrying a job, in bad weather).</summary>
    public bool IsConditional => Condition != BoonCondition.Always;

    /// <summary>The situation, as a short lead-in ("At night"); empty for a boon that always works.</summary>
    public string ConditionText()
    {
        return Condition switch
        {
            BoonCondition.AtNight => "At night",
            BoonCondition.CarryingJob => "While you carry a job",
            BoonCondition.InBadWeather => "In rain or snow",
            _ => "",
        };
    }

    private string WithCondition(string effects)
    {
        if (!IsConditional || effects.Length == 0)
        {
            return effects;
        }
        return ConditionText() + ": " + effects;
    }

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

    /// <summary>What it gives, plainly: the effects that help ("Stamina +20%").</summary>
    public string Gifts() => WithCondition(string.Join(" · ", Parts(true)));

    /// <summary>What it costs, plainly: the effects that hurt ("Walk 8% slower"); empty for a plain boon.</summary>
    public string Costs() => string.Join(" · ", Parts(false));

    /// <summary>A bargain gives something and takes something; both last the rest of the run.</summary>
    public bool IsBargain => Parts(false).Count > 0;

    /// <summary>The helpful (<paramref name="helps"/>) or harmful effects, each as a short line.</summary>
    public System.Collections.Generic.List<string> Parts(bool helps)
    {
        System.Collections.Generic.List<string> parts = new();
        // higherHelps: whether a factor above 1 is good for the carrier (more stamina) or bad (more drain).
        void Add(float factor, bool higherHelps, string more, string less)
        {
            int percent = Mathf.RoundToInt(Mathf.Abs(factor - 1.0f) * 100.0f);
            if (percent == 0)
            {
                return;
            }
            bool good = (factor > 1.0f) == higherHelps;
            if (good != helps)
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
        Add(StaminaMax, true, "Stamina +{0}%", "Stamina -{0}%");
        Add(StaminaRecovery, true, "Stamina recovers {0}% faster", "Stamina recovers {0}% slower");
        Add(WalkDrain, false, "Climbing costs {0}% more stamina", "Climbing costs {0}% less stamina");
        Add(LoadDrain, false, "The load costs {0}% more stamina", "The load costs {0}% less stamina");
        Add(NeedsDrain, false, "Hunger and thirst {0}% faster", "Hunger and thirst {0}% slower");
        Add(Grip, true, "Grip +{0}%", "Grip -{0}%");
        Add(Balance, true, "Balance +{0}%", "Balance -{0}%");
        Add(WalkSpeed, true, "Walk {0}% faster", "Walk {0}% slower");
        Add(SleepHeal, true, "Sleep heals {0}% more", "Sleep heals {0}% less");
        if (ColdResistance > 0.0f && helps)
        {
            parts.Add($"Cold {Mathf.RoundToInt(ColdResistance * 100.0f)}% weaker");
        }
        return parts;
    }

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
        return WithCondition(string.Join(" · ", parts));
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

    /// <summary>
    /// Undoes ApplyTo (a conditional boon switching off). Cold resistance is taken back as added, so
    /// a conditional boon should not rely on it near the 0.9 cap.
    /// </summary>
    public void RemoveFrom(RunModifiers modifiers)
    {
        modifiers.StaminaMax /= NonZero(StaminaMax);
        modifiers.StaminaRecovery /= NonZero(StaminaRecovery);
        modifiers.WalkDrain /= NonZero(WalkDrain);
        modifiers.LoadDrain /= NonZero(LoadDrain);
        modifiers.NeedsDrain /= NonZero(NeedsDrain);
        modifiers.Grip /= NonZero(Grip);
        modifiers.Balance /= NonZero(Balance);
        modifiers.WalkSpeed /= NonZero(WalkSpeed);
        modifiers.SleepHeal /= NonZero(SleepHeal);
        modifiers.ColdResistance = Mathf.Max(0.0f, modifiers.ColdResistance - ColdResistance);
    }

    private static float NonZero(float factor)
    {
        if (Mathf.Abs(factor) < 0.0001f)
        {
            return 1.0f;
        }
        return factor;
    }
}
