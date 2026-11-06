using DownhillRoguelite.Core;

namespace DownhillRoguelite.Environment;

/// <summary>One weather front: what it is, how strong (0 to 1) and where its wind blows from (radians around Y).</summary>
public readonly record struct WeatherFront(WeatherKind Kind, float Intensity, float WindAngle);
