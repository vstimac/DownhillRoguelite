using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// What gets scattered through the world and how much. Entries are parallel arrays: item i is
/// ItemPaths[i], picked with weight Weights[i], only below MinDepths[i] metres. Edit
/// inventory/world/default_supplies.tres.
/// </summary>
[GlobalClass]
public partial class SupplyTable : Resource
{
    [Export] public string[] ItemPaths { get; set; } = System.Array.Empty<string>();
    [Export] public float[] Weights { get; set; } = System.Array.Empty<float>();
    [Export] public float[] MinDepths { get; set; } = System.Array.Empty<float>();

    /// <summary>Items per km along the walkable route at the start of a run (before difficulty).</summary>
    [Export] public float RoutePerKm { get; set; } = 8.0f;
    /// <summary>Items scattered anywhere, per 1000 m², at the start of a run (before difficulty).</summary>
    [Export] public float Density { get; set; } = 0.03f;
    /// <summary>Route items lie within this distance of the route, in m.</summary>
    [Export] public float RouteBand { get; set; } = 12.0f;
    /// <summary>Route items along a hazardous branch, as a multiple of RoutePerKm (M10).</summary>
    [Export] public float HazardousMultiplier { get; set; } = 1.5f;
    /// <summary>Route items along a supply-rich branch, as a multiple of RoutePerKm.</summary>
    [Export] public float SupplyRichMultiplier { get; set; } = 4.0f;
    /// <summary>Branch items lie within this distance of the branch, in m (branches are narrower).</summary>
    [Export] public float BranchBand { get; set; } = 6.0f;
    /// <summary>Caches (several items together) per km of hazardous branch.</summary>
    [Export] public float CachesPerKm { get; set; } = 2.0f;
    [Export] public int CacheSize { get; set; } = 3;
    /// <summary>Items of a cache lie within this distance of its centre, in m.</summary>
    [Export] public float CacheSpread { get; set; } = 1.2f;
    /// <summary>Density multiplier lost per 1000 m of depth (deeper is leaner).</summary>
    [Export] public float DepthFalloffPer1000 { get; set; } = 0.25f;
    /// <summary>Density never drops below this fraction, however deep.</summary>
    [Export] public float MinDensityFactor { get; set; } = 0.3f;
}
