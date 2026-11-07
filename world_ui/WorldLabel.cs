using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// Shared settings for in-world text (signs, cairns, stations, markers): it stays behind terrain,
/// shrinks with distance like any object, and fades out beyond a reading range instead of
/// hanging huge in the sky or showing through the mountain.
/// </summary>
public static class WorldLabel
{
    /// <summary>Default reading range for signs and cairns, in metres.</summary>
    public const float ReadRange = 30.0f;

    /// <summary>The last stretch over which a label fades out, in metres.</summary>
    public const float FadeMargin = 8.0f;

    /// <summary>Applies the reading range and depth test to <paramref name="label"/>.</summary>
    public static Label3D Readable(Label3D label, float range = ReadRange)
    {
        label.VisibilityRangeEnd = range;
        label.VisibilityRangeEndMargin = FadeMargin;
        label.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
        label.NoDepthTest = false;
        label.FixedSize = false;
        return label;
    }
}
