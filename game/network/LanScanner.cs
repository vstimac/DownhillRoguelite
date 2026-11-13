using System;
using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>
/// The joining side of LAN discovery: while running it asks the home network for expeditions every
/// RefreshSeconds (a broadcast, each home-network address's /24 broadcast, and 127.0.0.1, so two
/// copies of the game on one computer find each other), on every port LanDiscovery may listen on.
/// It sends from a port the system picks, so it never fights a host on this computer for the
/// discovery port. Nothing blocks: the socket is polled each frame. Answers are checked
/// (LanDiscovery.ParseReply, and the nonce must be one of ours), merged by the host's tag (the same
/// expedition heard twice is one entry) and dropped once the host stops answering. Ping is the time
/// from our request to its answer. Stop (or leaving the tree) closes the socket.
/// </summary>
public partial class LanScanner : Node
{
    [Export] public int DiscoveryPort { get; set; } = 7778;
    [Export] public int PortFallbacks { get; set; } = 3;
    [Export] public float RefreshSeconds { get; set; } = 1.5f;
    /// <summary>An expedition not heard for this long is dropped from the list.</summary>
    [Export] public float StaleSeconds { get; set; } = 5.0f;
    [Export] public int MaxServers { get; set; } = 32;
    [Export] public int MaxPacketsPerFrame { get; set; } = 64;
    /// <summary>Answers slower than this (milliseconds) are too late to count as ours.</summary>
    [Export] public int MaxPingMs { get; set; } = 3000;

    /// <summary>The list changed: one found, updated or dropped.</summary>
    public event Action? Changed;

    public bool Running => _socket != null;
    public IReadOnlyList<LanServerInfo> Servers => _sorted;
    public int Sent { get; private set; }
    public int Rejected { get; private set; }

    private PacketPeerUdp? _socket;
    private readonly Dictionary<string, LanServerInfo> _servers = new();
    private readonly List<LanServerInfo> _sorted = new();
    private readonly Dictionary<string, ulong> _nonces = new();
    private readonly HashSet<string> _ownAddresses = new();
    private double _timer;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _ExitTree()
    {
        Stop();
    }

    /// <summary>Opens the socket (on a port the system picks) and asks at once; false if it can't.</summary>
    public bool Start()
    {
        if (_socket != null)
        {
            Refresh();
            return true;
        }
        PacketPeerUdp socket = new();
        socket.SetBroadcastEnabled(true);
        Error error = socket.Bind(0, "*");
        if (error != Error.Ok)
        {
            GD.Print($"[LanScanner] couldn't open a socket ({error})");
            socket.Close();
            return false;
        }
        _socket = socket;
        _ownAddresses.Clear();
        foreach (string address in IP.GetLocalAddresses())
        {
            _ownAddresses.Add(address);
        }
        Refresh();
        return true;
    }

    public void Stop()
    {
        if (_socket != null)
        {
            _socket.Close();
            _socket = null;
        }
        _nonces.Clear();
        if (_servers.Count > 0)
        {
            _servers.Clear();
            _sorted.Clear();
            Changed?.Invoke();
        }
    }

    /// <summary>Asks again now (the list keeps what it has until entries go stale).</summary>
    public void Refresh()
    {
        _timer = 0.0;
        Ask();
    }

    public override void _Process(double delta)
    {
        if (_socket == null)
        {
            return;
        }
        _timer += delta;
        if (_timer >= RefreshSeconds)
        {
            _timer = 0.0;
            Ask();
        }
        bool changed = Receive();
        if (Expire())
        {
            changed = true;
        }
        if (changed)
        {
            Sort();
            Changed?.Invoke();
        }
    }

    private void Ask()
    {
        if (_socket == null)
        {
            return;
        }
        ulong now = Time.GetTicksMsec();
        string nonce = now.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _nonces[nonce] = now;
        List<string> old = new();
        foreach (KeyValuePair<string, ulong> sent in _nonces)
        {
            if (now - sent.Value > (ulong)MaxPingMs)
            {
                old.Add(sent.Key);
            }
        }
        foreach (string key in old)
        {
            _nonces.Remove(key);
        }
        byte[] request = LanDiscovery.BuildRequest(nonce);
        foreach (string target in Targets())
        {
            for (int i = 0; i <= PortFallbacks; i++)
            {
                _socket.SetDestAddress(target, DiscoveryPort + i);
                if (_socket.PutPacket(request) == Error.Ok)
                {
                    Sent++;
                }
            }
        }
    }

    /// <summary>127.0.0.1, the broadcast address, and each home-network address's /24 broadcast (Windows sends the plain broadcast on one adapter only).</summary>
    private List<string> Targets()
    {
        List<string> targets = new() { "127.0.0.1", "255.255.255.255" };
        foreach (string address in _ownAddresses)
        {
            string[] parts = address.Split('.');
            if (parts.Length != 4 || address.StartsWith("127.", StringComparison.Ordinal) || address.StartsWith("169.254.", StringComparison.Ordinal))
            {
                continue;
            }
            string broadcast = $"{parts[0]}.{parts[1]}.{parts[2]}.255";
            if (!targets.Contains(broadcast))
            {
                targets.Add(broadcast);
            }
        }
        return targets;
    }

    private bool Receive()
    {
        bool changed = false;
        int handled = 0;
        while (_socket != null && _socket.GetAvailablePacketCount() > 0)
        {
            byte[] packet = _socket.GetPacket();
            string ip = _socket.GetPacketIP();
            handled++;
            if (handled > MaxPacketsPerFrame)
            {
                Rejected++;
                continue;
            }
            LanServerInfo? info = LanDiscovery.ParseReply(packet, out string nonce);
            if (info == null || ip.Length == 0 || !_nonces.TryGetValue(nonce, out ulong sentAt))
            {
                Rejected++;
                continue;
            }
            ulong now = Time.GetTicksMsec();
            bool local = ip.StartsWith("127.", StringComparison.Ordinal) || _ownAddresses.Contains(ip);
            info.Address = ip;
            info.Local = local;
            info.SeenAt = now;
            info.PingMs = (int)Math.Min(now - sentAt, (ulong)MaxPingMs);
            if (_servers.TryGetValue(info.Id, out LanServerInfo? known))
            {
                // The same expedition again (by another route): keep the loopback address for one on this computer.
                if (known.Address.StartsWith("127.", StringComparison.Ordinal) && !ip.StartsWith("127.", StringComparison.Ordinal))
                {
                    info.Address = known.Address;
                }
                if (known.PingMs >= 0 && known.PingMs < info.PingMs && now - known.SeenAt < 500)
                {
                    info.PingMs = known.PingMs;
                }
                info.Local = local || known.Local;
            }
            else if (_servers.Count >= MaxServers)
            {
                Rejected++;
                continue;
            }
            _servers[info.Id] = info;
            changed = true;
        }
        return changed;
    }

    private bool Expire()
    {
        ulong now = Time.GetTicksMsec();
        List<string> stale = new();
        foreach (KeyValuePair<string, LanServerInfo> server in _servers)
        {
            if (now - server.Value.SeenAt > (ulong)(StaleSeconds * 1000.0f))
            {
                stale.Add(server.Key);
            }
        }
        foreach (string id in stale)
        {
            _servers.Remove(id);
        }
        return stale.Count > 0;
    }

    private void Sort()
    {
        _sorted.Clear();
        _sorted.AddRange(_servers.Values);
        _sorted.Sort((a, b) =>
        {
            int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            if (byName != 0)
            {
                return byName;
            }
            return string.CompareOrdinal(a.Id, b.Id);
        });
    }
}
