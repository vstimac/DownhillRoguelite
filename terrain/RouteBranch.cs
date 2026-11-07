using DownhillRoguelite.Core;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// One branch path of a stage: its kind, which side of the main path it swings to and how far,
/// where it leaves (SplitZ) and rejoins (JoinZ) the main path, and the main path's x at both ends
/// (a hazardous branch cuts straight between them).
/// </summary>
public readonly record struct RouteBranch(
    RouteKind Kind,
    float Side,
    float Offset,
    float SplitZ,
    float JoinZ,
    float SplitSpineX,
    float JoinSpineX);
