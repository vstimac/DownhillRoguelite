using Godot;
namespace DownhillRoguelite.Core;

/// <summary>Fictional gameplay tuning for a character; defaults are neutral. No mutable run state lives in this asset.</summary>
[GlobalClass]
public partial class CharacterStats : Resource
{
    [Export] public float Health { get; set; } = 1;
    [Export] public float Stamina { get; set; } = 1;
    [Export] public float Recovery { get; set; } = 1;
    [Export] public float Speed { get; set; } = 1;
    [Export] public float StaminaCost { get; set; } = 1;
    [Export] public float LoadEffort { get; set; } = 1;
    [Export] public float NeedsDrain { get; set; } = 1;
    [Export] public float Grip { get; set; } = 1;
    [Export] public float ColdResistance { get; set; }
    [Export] public float ImpactResistance { get; set; }
    [ExportGroup("Ability")]
    [Export] public string AbilityName { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string AbilityDescription { get; set; } = "";
    [Export] public float Cooldown { get; set; } = 60;
    [Export] public float Duration { get; set; }
    [Export] public float RestoreStaminaFraction { get; set; }
    [Export] public float ActiveSpeed { get; set; } = 1;
    [Export] public float ActiveStaminaCost { get; set; } = 1;
    [Export] public float ActiveImpactResistance { get; set; }
    [Export] public float ActiveColdResistance { get; set; }

    public bool Valid()
    {
        foreach (float number in new[] { Health, Stamina, Recovery, Speed, StaminaCost, LoadEffort, NeedsDrain, Grip, ActiveSpeed, ActiveStaminaCost, Cooldown })
        { if (!float.IsFinite(number) || number <= 0 || number > 1000) { return false; } }
        foreach (float fraction in new[] { ColdResistance, ImpactResistance, RestoreStaminaFraction, ActiveImpactResistance, ActiveColdResistance })
        { if (!float.IsFinite(fraction) || fraction < 0 || fraction > 1) { return false; } }
        return float.IsFinite(Duration) && Duration >= 0;
    }

    public string Summary()
    {
        string text = $"Health {Health:P0} · Stamina {Stamina:P0} · Speed {Speed:P0}\nRecovery {Recovery:P0} · Needs drain {NeedsDrain:P0}";
        if (AbilityName.Length > 0) { text += $"\nH — {AbilityName} · {Cooldown:0}s cooldown\n{AbilityDescription}"; }
        return text;
    }
}
