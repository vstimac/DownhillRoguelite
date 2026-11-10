namespace DownhillRoguelite.Narrative;

/// <summary>What a found text is written on; it picks the model it stands on.</summary>
public enum LoreForm
{
    /// <summary>A signpost, often broken.</summary>
    Sign,
    /// <summary>A loose page weighed down on a crate.</summary>
    Note,
    /// <summary>An open book on a small table.</summary>
    Ledger,
    /// <summary>Words cut into a standing stone.</summary>
    Stone,
    /// <summary>A letter tucked into an abandoned bedroll.</summary>
    Letter,
    /// <summary>A heap of stones with a page pinned under the top one (a witness cairn).</summary>
    Cairn,
}
