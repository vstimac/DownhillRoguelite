using Godot;

namespace DownhillRoguelite.Network;

/// <summary>
/// One expedition found on the home network: what its host answered to a discovery request (name,
/// game port, players, whether it is under way), where the answer came from, and when it was last
/// heard (entries the host stops answering for expire). Built only from a reply that passed
/// LanDiscovery.Parse, so every field is bounded.
/// </summary>
public sealed class LanServerInfo
{
    /// <summary>A random tag the host picks each time it starts hosting: the same expedition heard twice (by broadcast and on 127.0.0.1) is one entry.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public int Port { get; set; }
    public int Players { get; set; }
    public int MaxPlayers { get; set; }
    /// <summary>The host is past the summit (has reached a station): "In progress"; otherwise "Waiting".</summary>
    public bool InProgress { get; set; }
    public bool JoinInProgress { get; set; } = true;
    public bool PasswordRequired { get; set; }
    /// <summary>The host's co-op protocol (NetSession.ProtocolVersion); another one can't be joined.</summary>
    public string NetVersion { get; set; } = "";
    public string GameVersion { get; set; } = "";
    /// <summary>Round trip of the last request it answered, in milliseconds (-1: not measured).</summary>
    public int PingMs { get; set; } = -1;
    /// <summary>It answered on 127.0.0.1: the host runs on this computer.</summary>
    public bool Local { get; set; }
    /// <summary>When it was last heard (Time.GetTicksMsec).</summary>
    public ulong SeenAt { get; set; }

    public string Endpoint => $"{Address}:{Port}";
    public bool Full => Players >= MaxPlayers;
    public bool SameVersion => NetVersion == NetSession.ProtocolVersion;
    public bool Joinable => SameVersion && !Full && !PasswordRequired && (JoinInProgress || !InProgress);

    /// <summary>"Waiting", "In progress", or why it can't be joined.</summary>
    public string StatusText()
    {
        if (!SameVersion)
        {
            return "Other version";
        }
        if (Full)
        {
            return "Full";
        }
        if (InProgress)
        {
            return "In progress";
        }
        return "Waiting";
    }

    /// <summary>"Local" for this computer, else the ping ("12 ms"), or "…" before one is measured.</summary>
    public string PingText()
    {
        if (Local)
        {
            return "Local";
        }
        if (PingMs < 0)
        {
            return "…";
        }
        return $"{PingMs} ms";
    }

    public static string Clip(string text, int length)
    {
        string clean = text.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
        if (clean.Length > length)
        {
            clean = clean[..length];
        }
        return clean;
    }

    public override string ToString()
    {
        return $"{Name} at {Endpoint} {Players}/{MaxPlayers} {StatusText()} {PingText()}";
    }
}
