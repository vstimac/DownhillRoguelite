using Godot;
namespace DownhillRoguelite.Core;

/// <summary>One run's selected origin and ability clock, separate from boons, equipment and shared resources.</summary>
public sealed class CharacterRun
{
    public CarrierCharacter? Definition { get; private set; }
    public CharacterStats Stats { get; private set; } = new();
    public float CooldownLeft { get; private set; }
    public float ActiveLeft { get; private set; }
    public bool Active => ActiveLeft > 0;
    public string Id => Definition?.Id ?? "default";
    public float Speed { get { if (Active) { return Stats.Speed * Stats.ActiveSpeed; } return Stats.Speed; } }
    public float StaminaCost { get { if (Active) { return Stats.StaminaCost * Stats.ActiveStaminaCost; } return Stats.StaminaCost; } }
    public float ColdResistance { get { float value = Stats.ColdResistance; if (Active) { value += Stats.ActiveColdResistance; } return Mathf.Clamp(value, 0, 0.9f); } }
    public float ImpactFactor { get { float factor = 1.0f - Stats.ImpactResistance; if (Active) { factor *= 1.0f - Stats.ActiveImpactResistance; } return Mathf.Clamp(factor, 0, 1); } }

    public void Select(CarrierCharacter? character)
    {
        Definition = character;
        Stats = character?.Stats ?? new CharacterStats();
        if (!Stats.Valid()) { GD.PushWarning("Invalid character stats; using neutral values."); Stats = new CharacterStats(); }
        CooldownLeft = 0; ActiveLeft = 0;
    }
    public bool Activate()
    {
        if (CooldownLeft > 0 || Stats.AbilityName.Length == 0) { return false; }
        CooldownLeft = Stats.Cooldown;
        ActiveLeft = Stats.Duration;
        return true;
    }
    public void Tick(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0) { return; }
        CooldownLeft = Mathf.Max(0, CooldownLeft - seconds);
        ActiveLeft = Mathf.Max(0, ActiveLeft - seconds);
    }
}
