using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// All weather settings: one profile per circle (the last one is used for every circle beyond),
/// how long fronts last and how weather turns into effects (grip, mist, storm wind, gusts).
/// </summary>
[GlobalClass]
public partial class WeatherTable : Resource
{
    /// <summary>Profiles by circle, Limbo first; circles past the end use the last one.</summary>
    [Export] public WeatherProfile[] Profiles { get; set; } = { new() };

    [ExportGroup("Fronts")]
    /// <summary>Length of one weather front, in in-game hours.</summary>
    [Export] public float FrontHours { get; set; } = 5.0f;
    /// <summary>Hours at the end of a front spent blending into the next.</summary>
    [Export] public float BlendHours { get; set; } = 1.5f;
    /// <summary>Metres of depth before a circle's end over which its weather blends into the next circle's.</summary>
    [Export] public float CircleBlendDepth { get; set; } = 150.0f;
    /// <summary>Below this intensity everything counts as clear.</summary>
    [Export] public float ClearBelow { get; set; } = 0.15f;

    [ExportGroup("Effects")]
    /// <summary>Grip lost on a fully wet surface (multiplier 1 - this).</summary>
    [Export] public float RainGripLoss { get; set; } = 0.35f;
    /// <summary>Grip lost under full snow.</summary>
    [Export] public float SnowGripLoss { get; set; } = 0.45f;
    [Export] public float MinGripFactor { get; set; } = 0.3f;
    /// <summary>Mist that comes with rain and snow, as fog per unit of precipitation.</summary>
    [Export] public float PrecipitationMist { get; set; } = 0.35f;
    /// <summary>Wind a full rain or snow front brings, as a fraction of the profile's WindSpeed.</summary>
    [Export] public float StormWind { get; set; } = 0.5f;
    /// <summary>Heat left at full night (heat fronts are mostly a daytime thing).</summary>
    [Export] public float NightHeat { get; set; } = 0.3f;

    [ExportGroup("Gusts")]
    /// <summary>Gusts add up to this fraction of the wind above the breeze.</summary>
    [Export] public float GustStrength { get; set; } = 0.6f;
    /// <summary>Rough seconds between gusts (real time).</summary>
    [Export] public float GustPeriod { get; set; } = 5.0f;
    /// <summary>How far gusts swing the wind direction, in degrees.</summary>
    [Export] public float GustSwing { get; set; } = 20.0f;

    public WeatherProfile ProfileFor(int circle)
    {
        if (Profiles.Length == 0)
        {
            return new WeatherProfile();
        }
        return Profiles[Mathf.Clamp(circle, 0, Profiles.Length - 1)];
    }
}
