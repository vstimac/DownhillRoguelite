using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// The ground underfoot matches the ground the player sees. Classify gives the ground's kind for
/// its look (colour, carpet, what grows there); on grassy ground the look then wears to trodden
/// earth on a path, to bare earth or gravel in patches, and the grass carpet thins. Footing reads
/// the same numbers the look is built from, so footsteps, grip and prints follow what is shown:
/// grass where the carpet grows, gravel (Scree) where the stone texture shows, bare earth (Dirt)
/// elsewhere. Snow, mud, scree and rock are as Classify says, except that a path's band steeper than
/// RockSlope (where the ground shader's rock look is past half way) is rock. A firm path never turns to gravel.
/// </summary>
public sealed partial class TerrainGenerator
{
    /// <summary>How much of the stone texture the ground shader shows per unit of the stone channel (UV.x).</summary>
    public const float StoneShow = 0.85f;
    /// <summary>The most stone channel a firm (not hazardous) path's band carries: worn earth, never loose gravel.</summary>
    private const float FirmPathStone = 0.55f;

    /// <summary>
    /// The ground underfoot at a point with this slope and path weight (see the class summary).
    /// SurfaceAt uses it; TerrainTests checks it against the chunk's own vertex data.
    /// </summary>
    public TerrainSurface Footing(float x, float z, float slope, float pathWeight, RouteKind kind)
    {
        TerrainSurface surface = Classify(x, z, slope, pathWeight, kind);
        if (surface != TerrainSurface.Grass && surface != TerrainSurface.Mud)
        {
            return surface;
        }
        // A path's band (Classify keeps paths soft at any slope) this steep shows bare rock.
        if (slope >= _settings.RockSlope)
        {
            return TerrainSurface.Rock;
        }
        if (surface == TerrainSurface.Mud)
        {
            return surface;
        }
        float depth = DepthAt(Height(x, z));
        float bare = Bareness(x, z, slope, pathWeight, depth);
        float carpet = CarpetDensity(x, z, slope, surface, pathWeight, SnowCoverAt(x, z, pathWeight), bare);
        if (carpet >= 0.5f)
        {
            return TerrainSurface.Grass;
        }
        float stone = GrassyStone(PathWearAt(pathWeight), bare, GravelShare(depth, slope), depth, pathWeight, kind);
        return stone * StoneShow >= 0.5f ? TerrainSurface.Scree : TerrainSurface.Dirt;
    }

    /// <summary>How much a path's band is worn to trodden earth (0 none, PathWear at its middle).</summary>
    private float PathWearAt(float pathWeight)
    {
        return Mathf.SmoothStep(0.9f, 0.99f, pathWeight) * _settings.PathWear;
    }

    /// <summary>How much a bare patch is gravel rather than earth: stony circles and steeper ground are gravelly.</summary>
    private float GravelShare(float depth, float slope)
    {
        return Mathf.Clamp(StoninessAt(depth) + (slope - 20.0f) / 40.0f, 0.0f, 1.0f);
    }

    /// <summary>
    /// The stone channel (UV.x) of grassy ground: gravel through a stony path's trodden earth, and
    /// gravel patches. On a firm path's band it stays below what reads as stone.
    /// </summary>
    private float GrassyStone(float wear, float bare, float gravel, float depth, float pathWeight, RouteKind kind)
    {
        float stone = Mathf.Max(wear * 0.55f * StoninessAt(depth), bare * gravel);
        if (pathWeight > 0.5f && kind != RouteKind.Hazardous)
        {
            stone = Mathf.Min(stone, FirmPathStone);
        }
        return stone;
    }

    /// <summary>How deep snow lies at a point (0 none, 1 deep; 0.5 at the snow line), as the ground shader lays it.</summary>
    private float SnowCoverAt(float x, float z, float pathWeight)
    {
        float scale = 1.0f / _settings.SurfaceNoiseWavelength;
        float snow = _snow.Sample(x * scale, z * scale);
        float band = Mathf.Max(_settings.SnowEdgeBand, 0.001f);
        return Mathf.Clamp((snow - SnowThresholdAt(x, z, pathWeight)) / (2.0f * band) + 0.5f, 0.0f, 1.0f);
    }
}
