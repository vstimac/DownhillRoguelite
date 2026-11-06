namespace DownhillRoguelite.Core;

/// <summary>The carrier's health and stamina, published once per physics frame.</summary>
/// <param name="Health">Current health.</param>
/// <param name="MaxHealth">Health when unhurt.</param>
/// <param name="Stamina">Current stamina.</param>
/// <param name="MaxStamina">Stamina when rested.</param>
/// <param name="Exhausted">Out of stamina: no sprinting or jumping until it partly recovers.</param>
/// <param name="Sprinting">Whether the carrier is sprinting this frame.</param>
/// <param name="Dead">Health reached zero.</param>
/// <param name="LastDamage">Size of the most recent hit.</param>
/// <param name="LastDamageSource">What the most recent hit was against (surface name).</param>
public readonly record struct VitalsSnapshot(
    float Health,
    float MaxHealth,
    float Stamina,
    float MaxStamina,
    bool Exhausted,
    bool Sprinting,
    bool Dead,
    float LastDamage,
    string LastDamageSource);
