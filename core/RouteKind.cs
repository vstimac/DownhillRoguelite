namespace DownhillRoguelite.Core;

/// <summary>
/// The kinds of path between two stations (M10). Safe is the main, marked path; the others branch
/// off below a station and rejoin above the next.
/// </summary>
public enum RouteKind
{
    /// <summary>The main path: gentle, marked by cairns, ordinary supplies.</summary>
    Safe,
    /// <summary>A straighter, shorter line on narrow, loose ground, with supply caches.</summary>
    Hazardous,
    /// <summary>A winding detour that takes longer, with far more food and water.</summary>
    SupplyRich,
}
