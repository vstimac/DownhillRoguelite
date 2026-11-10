namespace DownhillRoguelite.Inventory;

/// <summary>Which of the 24 grid orientations an item may be packed in.</summary>
public enum OrientationRule
{
    /// <summary>Any right-angle rotation.</summary>
    Any,
    /// <summary>Only turned about the vertical axis, never tipped (liquids).</summary>
    Upright,
}
