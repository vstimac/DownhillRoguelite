using System.Collections.Generic;
using System.Text.RegularExpressions;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>
/// Co-op: spider webs torn by one player come down for everyone (a child of NetSession). Every
/// machine strings the same webs (they follow the trees), so only the web's key travels: the tearing
/// machine tells the host, the host tells the others, and each takes that web down quietly (no snare,
/// no spider called on their side).
/// </summary>
public partial class NetWebs : Node
{
    /// <summary>At most this many webs a second from one player (more is not walking).</summary>
    [Export] public int MostPerSecond { get; set; } = 4;

    public NetSession? Session { get; set; }
    /// <summary>Webs this machine announced, and webs it took down for others (tests).</summary>
    public int Sent { get; private set; }
    public int Received { get; private set; }

    private static readonly Regex KeyShape = new(@"^-?\d{1,9}:-?\d{1,9}:-?\d{1,9}:-?\d{1,9}$");
    private readonly Dictionary<long, (double Second, int Count)> _rate = new();

    private bool Live => Session != null && Session.Active && Session.Joined;
    private static double Now => Time.GetTicksMsec() / 1000.0;

    public override void _Ready()
    {
        GameEvents.WebTorn += OnWebTorn;
    }

    public override void _ExitTree()
    {
        GameEvents.WebTorn -= OnWebTorn;
    }

    private void OnWebTorn(string key)
    {
        if (!Live || !KeyShape.IsMatch(key))
        {
            return;
        }
        Sent++;
        NetSession session = Session!;
        if (session.Mode == NetSession.Role.Host)
        {
            foreach (long id in session.Peers.Keys)
            {
                RpcId(id, MethodName.PeerTorn, key);
            }
        }
        else
        {
            RpcId(1, MethodName.Torn, key);
        }
    }

    /// <summary>Host: a client tore a web; down here too and passed to the others.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Torn(string key)
    {
        long sender = Multiplayer.GetRemoteSenderId();
        NetSession? session = Session;
        if (session == null || session.Mode != NetSession.Role.Host || !session.Peers.ContainsKey(sender) || !KeyShape.IsMatch(key) || TooMany(sender))
        {
            return;
        }
        Take(key);
        foreach (long id in session.Peers.Keys)
        {
            if (id != sender)
            {
                RpcId(id, MethodName.PeerTorn, key);
            }
        }
    }

    /// <summary>Client: a web someone tore, from the host.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PeerTorn(string key)
    {
        if (Session?.Mode != NetSession.Role.Client || !KeyShape.IsMatch(key))
        {
            return;
        }
        Take(key);
    }

    private void Take(string key)
    {
        Received++;
        GameEvents.RequestWebGone(key);
    }

    private bool TooMany(long sender)
    {
        double second = System.Math.Floor(Now);
        if (!_rate.TryGetValue(sender, out (double Second, int Count) rate) || rate.Second != second)
        {
            rate = (second, 0);
        }
        rate.Count++;
        _rate[sender] = rate;
        return rate.Count > MostPerSecond;
    }
}
