namespace DownhillRoguelite.Inventory;

/// <summary>Where the pack is: on the back, coming off, open on the ground, or going back on.</summary>
public enum PackingPhase
{
    Carried,
    SettingDown,
    Open,
    PuttingOn,
}
