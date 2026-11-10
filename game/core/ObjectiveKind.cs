namespace DownhillRoguelite.Core;

/// <summary>What sort of goal an objective is (the HUD and journal order and style them by kind).</summary>
public enum ObjectiveKind
{
    /// <summary>The one main goal: the next station before dark, or sleeping at this one.</summary>
    Main,
    /// <summary>The descent's long goal: the next circle down.</summary>
    Circle,
    /// <summary>A first-run guide step (pick up food, read a sign, take a boon).</summary>
    Guide,
    /// <summary>A job from a station's board, often asked for by the traveller at its fire.</summary>
    Errand,
    /// <summary>A found relic waiting to be left at a station altar.</summary>
    Relic,
    /// <summary>Something worth a detour close to the path (a spring, an abandoned pack).</summary>
    Nearby,
}
