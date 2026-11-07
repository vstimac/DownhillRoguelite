namespace DownhillRoguelite.Core;

/// <summary>
/// How the walk to the next station looks from where the carrier stands, with its current load,
/// stamina and needs: the basis for deciding between scavenging and descending.
/// </summary>
/// <param name="Valid">False until an estimate exists.</param>
/// <param name="Station">Index of the station it is for.</param>
/// <param name="DistanceMeters">Distance along the route, in m.</param>
/// <param name="WalkSeconds">Real seconds of walking.</param>
/// <param name="RestSeconds">Real seconds of standing to recover stamina on the way.</param>
/// <param name="Rests">How many times stamina will run out on the way.</param>
/// <param name="StaminaNeeded">Stamina the walk costs in total.</param>
/// <param name="ArrivalDay">Day of arrival.</param>
/// <param name="ArrivalHour">Hour of arrival on that day.</param>
/// <param name="BeforeNightfall">Whether the carrier arrives before sunset.</param>
/// <param name="Kind">Which kind of path the estimate follows.</param>
public readonly record struct RouteEstimate(
    bool Valid,
    int Station,
    float DistanceMeters,
    float WalkSeconds,
    float RestSeconds,
    int Rests,
    float StaminaNeeded,
    int ArrivalDay,
    float ArrivalHour,
    bool BeforeNightfall,
    RouteKind Kind = RouteKind.Safe)
{
    public float TotalSeconds => WalkSeconds + RestSeconds;
}
