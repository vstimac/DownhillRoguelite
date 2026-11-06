using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// The weather where the carrier is, published by the weather system. Intensities are 0 to 1.
/// </summary>
/// <param name="Kind">The dominant front.</param>
/// <param name="Fog">Extra fog (visibility).</param>
/// <param name="Rain">Rainfall.</param>
/// <param name="Snow">Snowfall.</param>
/// <param name="Heat">Dry heat.</param>
/// <param name="Wind">Wind velocity right now (gusts included), horizontal, in m/s.</param>
/// <param name="GripFactor">Multiplier on surface grip from wet or snowy ground.</param>
public readonly record struct WeatherSnapshot(
    WeatherKind Kind,
    float Fog,
    float Rain,
    float Snow,
    float Heat,
    Vector3 Wind,
    float GripFactor)
{
    public static WeatherSnapshot Calm => new(WeatherKind.Clear, 0.0f, 0.0f, 0.0f, 0.0f, Vector3.Zero, 1.0f);

    public float WindSpeed => Wind.Length();
}
