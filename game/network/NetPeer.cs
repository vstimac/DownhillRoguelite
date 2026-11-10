namespace DownhillRoguelite.Network;

/// <summary>One player in a co-op session, as this machine knows them.</summary>
public sealed class NetPeer
{
    public long Id { get; init; }
    public string Name { get; set; } = "";
    public string Character { get; set; } = "";
    public string[] Worn { get; set; } = System.Array.Empty<string>();
    /// <summary>Their base shirt and pack colours (the character creator's).</summary>
    public Godot.Color Shirt { get; set; } = new(0.28f, 0.35f, 0.43f);
    public Godot.Color Pack { get; set; } = new(0.36f, 0.29f, 0.18f);
    public bool Dead { get; set; }
    /// <summary>Where they were last (feet), for the player list and spectating.</summary>
    public Godot.Vector3 Position { get; set; }
    /// <summary>Seconds since their last state arrived (the host drops the silent).</summary>
    public double Silent { get; set; }
}
