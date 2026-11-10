namespace DownhillRoguelite.Network;

/// <summary>One player in a co-op session, as this machine knows them.</summary>
public sealed class NetPeer
{
    public long Id { get; init; }
    public string Name { get; set; } = "";
    public string Character { get; set; } = "";
    public string[] Worn { get; set; } = System.Array.Empty<string>();
    public bool Dead { get; set; }
    /// <summary>Where they were last (feet), for the player list and spectating.</summary>
    public Godot.Vector3 Position { get; set; }
    /// <summary>Seconds since their last state arrived (the host drops the silent).</summary>
    public double Silent { get; set; }
}
