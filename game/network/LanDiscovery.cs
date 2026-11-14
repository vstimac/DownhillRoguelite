using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>
/// The host's side of LAN discovery: while this machine hosts (and is visible on the home network)
/// it listens on its own UDP port (DiscoveryPort, beside the game's ENet port) and answers every
/// well-formed request with a short, versioned description of the expedition: its name, game port,
/// players and room, whether it is under way, and the protocol version. Nothing secret goes out (no
/// seed, no addresses but the one the request came from). Requests are size-checked and parsed
/// strictly, and replies are rate-limited (overall and per sender). If the port is taken (another
/// host on this computer) it tries the next few; LanScanner asks all of them. NetSession starts it
/// with Host and frees it when the session ends. The request and reply formats live here too.
/// </summary>
public partial class LanDiscovery : Node
{
    public const string RequestMagic = "DHRL-DISCOVER/1";
    public const string ReplyMagic = "DHRL-EXPEDITION/1";
    public const int MaxRequestBytes = 64;
    public const int MaxReplyBytes = 512;
    public const int MaxNameLength = 32;
    public const int MaxNonceLength = 20;

    [Export] public int DiscoveryPort { get; set; } = 7778;
    /// <summary>How many ports after DiscoveryPort it tries when that one is taken (LanScanner asks the same range).</summary>
    [Export] public int PortFallbacks { get; set; } = 3;
    [Export] public int MaxRepliesPerSecond { get; set; } = 20;
    /// <summary>One sender gets at most one answer in this many seconds.</summary>
    [Export] public float PerSenderInterval { get; set; } = 0.2f;
    [Export] public int MaxPacketsPerFrame { get; set; } = 32;
    [Export] public int MaxTrackedSenders { get; set; } = 128;

    public NetSession? Session { get; set; }
    /// <summary>The port it actually listens on (0: not listening).</summary>
    public int BoundPort { get; private set; }
    public int Answered { get; private set; }
    public int Ignored { get; private set; }

    private PacketPeerUdp? _socket;
    private readonly Dictionary<string, ulong> _lastReply = new();
    private ulong _windowStart;
    private int _windowReplies;
    private string _id = "";

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _id = $"{GD.Randi():x8}{GD.Randi():x8}";
        for (int i = 0; i <= PortFallbacks; i++)
        {
            PacketPeerUdp socket = new();
            if (socket.Bind(DiscoveryPort + i, "*") == Error.Ok)
            {
                _socket = socket;
                BoundPort = DiscoveryPort + i;
                break;
            }
            socket.Close();
        }
        if (_socket == null)
        {
            GD.Print($"[LanDiscovery] couldn't listen on ports {DiscoveryPort}-{DiscoveryPort + PortFallbacks}; the expedition won't show on the home network");
            return;
        }
        GD.Print($"[LanDiscovery] answering on UDP port {BoundPort}");
    }

    public override void _ExitTree()
    {
        Stop();
    }

    public void Stop()
    {
        if (_socket != null)
        {
            _socket.Close();
            _socket = null;
            GD.Print($"[LanDiscovery] stopped ({Answered} answered, {Ignored} ignored)");
        }
        BoundPort = 0;
    }

    public override void _Process(double delta)
    {
        if (_socket == null || Session == null)
        {
            return;
        }
        int handled = 0;
        while (_socket.GetAvailablePacketCount() > 0)
        {
            byte[] packet = _socket.GetPacket();
            string ip = _socket.GetPacketIP();
            int port = _socket.GetPacketPort();
            handled++;
            if (handled > MaxPacketsPerFrame)
            {
                Ignored++;
                continue;
            }
            string? nonce = ParseRequest(packet);
            if (nonce == null || ip.Length == 0 || port <= 0)
            {
                Ignored++;
                continue;
            }
            if (!Allowed(ip, port))
            {
                Ignored++;
                continue;
            }
            byte[] reply = BuildReply(nonce);
            if (reply.Length > MaxReplyBytes)
            {
                Ignored++;
                continue;
            }
            _socket.SetDestAddress(ip, port);
            if (_socket.PutPacket(reply) == Error.Ok)
            {
                Answered++;
                GD.Print($"[LanDiscovery] answered {ip}:{port} players={Session.PlayerCount}/{Session.SessionMax}");
            }
        }
    }

    /// <summary>Rate limits: so many answers a second in all, and one per sender per PerSenderInterval.</summary>
    private bool Allowed(string ip, int port)
    {
        ulong now = Time.GetTicksMsec();
        if (now - _windowStart >= 1000)
        {
            _windowStart = now;
            _windowReplies = 0;
        }
        if (_windowReplies >= MaxRepliesPerSecond)
        {
            return false;
        }
        string sender = $"{ip}:{port}";
        if (_lastReply.TryGetValue(sender, out ulong last) && now - last < (ulong)(PerSenderInterval * 1000.0f))
        {
            return false;
        }
        if (_lastReply.Count >= MaxTrackedSenders)
        {
            _lastReply.Clear();
        }
        _lastReply[sender] = now;
        _windowReplies++;
        return true;
    }

    private byte[] BuildReply(string nonce)
    {
        NetSession session = Session!;
        string state = "waiting";
        if (session.InProgress)
        {
            state = "in_progress";
        }
        Godot.Collections.Dictionary info = new()
        {
            ["id"] = _id,
            ["name"] = LanServerInfo.Clip(session.ServerName, MaxNameLength),
            ["port"] = session.Port,
            ["players"] = session.PlayerCount,
            ["max"] = session.SessionMax,
            ["state"] = state,
            ["jip"] = true,
            ["pwd"] = false,
            ["net"] = NetSession.ProtocolVersion,
            ["game"] = LanServerInfo.Clip((string)ProjectSettings.GetSetting("application/config/version", ""), 24),
            ["nonce"] = nonce,
        };
        return (ReplyMagic + " " + Json.Stringify(info)).ToUtf8Buffer();
    }

    // --- The formats (shared with LanScanner) ---

    public static byte[] BuildRequest(string nonce)
    {
        return $"{RequestMagic} {nonce}".ToAsciiBuffer();
    }

    /// <summary>The nonce of a well-formed request ("DHRL-DISCOVER/1 digits"), or null.</summary>
    public static string? ParseRequest(byte[] packet)
    {
        if (packet.Length < RequestMagic.Length + 2 || packet.Length > MaxRequestBytes)
        {
            return null;
        }
        string text = packet.GetStringFromAscii();
        if (!text.StartsWith(RequestMagic + " ", System.StringComparison.Ordinal))
        {
            return null;
        }
        string nonce = text[(RequestMagic.Length + 1)..];
        if (nonce.Length == 0 || nonce.Length > MaxNonceLength)
        {
            return null;
        }
        foreach (char c in nonce)
        {
            if (c < '0' || c > '9')
            {
                return null;
            }
        }
        return nonce;
    }

    /// <summary>A reply checked field by field (types, ranges, lengths), with its echoed nonce; null if anything is off.</summary>
    public static LanServerInfo? ParseReply(byte[] packet, out string nonce)
    {
        nonce = "";
        if (packet.Length <= ReplyMagic.Length + 2 || packet.Length > MaxReplyBytes)
        {
            return null;
        }
        string text = packet.GetStringFromUtf8();
        if (!text.StartsWith(ReplyMagic + " ", System.StringComparison.Ordinal))
        {
            return null;
        }
        Variant parsed = Json.ParseString(text[(ReplyMagic.Length + 1)..]);
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            return null;
        }
        Godot.Collections.Dictionary data = parsed.AsGodotDictionary();
        string? id = Text(data, "id", 32);
        string? name = Text(data, "name", MaxNameLength);
        string? state = Text(data, "state", 16);
        string? net = Text(data, "net", 32);
        string? game = Text(data, "game", 24);
        string? echoed = Text(data, "nonce", MaxNonceLength);
        int port = Number(data, "port");
        int players = Number(data, "players");
        int max = Number(data, "max");
        if (id == null || id.Length == 0 || name == null || state == null || net == null || game == null || echoed == null)
        {
            return null;
        }
        if (port <= 0 || port > 65535 || max < 1 || max > 16 || players < 0 || players > max)
        {
            return null;
        }
        if (state != "waiting" && state != "in_progress")
        {
            return null;
        }
        nonce = echoed;
        string shown = name;
        if (shown.Length == 0)
        {
            shown = "Expedition";
        }
        return new LanServerInfo
        {
            Id = id, Name = shown, Port = port, Players = players, MaxPlayers = max, InProgress = state == "in_progress",
            JoinInProgress = Flag(data, "jip", true), PasswordRequired = Flag(data, "pwd", false), NetVersion = net, GameVersion = game,
        };
    }

    private static string? Text(Godot.Collections.Dictionary data, string key, int length)
    {
        if (!data.TryGetValue(key, out Variant value) || value.VariantType != Variant.Type.String)
        {
            return null;
        }
        string text = value.AsString();
        if (text.Length > length)
        {
            return null;
        }
        return LanServerInfo.Clip(text, length);
    }

    /// <summary>A whole number (JSON numbers arrive as floats), or -1.</summary>
    private static int Number(Godot.Collections.Dictionary data, string key)
    {
        if (!data.TryGetValue(key, out Variant value))
        {
            return -1;
        }
        if (value.VariantType == Variant.Type.Int)
        {
            return (int)Mathf.Clamp(value.AsInt64(), -1, 1000000);
        }
        if (value.VariantType == Variant.Type.Float)
        {
            double number = value.AsDouble();
            if (!double.IsFinite(number) || number != System.Math.Floor(number) || number < -1.0 || number > 1000000.0)
            {
                return -1;
            }
            return (int)number;
        }
        return -1;
    }

    private static bool Flag(Godot.Collections.Dictionary data, string key, bool fallback)
    {
        if (data.TryGetValue(key, out Variant value) && value.VariantType == Variant.Type.Bool)
        {
            return value.AsBool();
        }
        return fallback;
    }
}
