using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>
/// Co-op marks (a child of NetSession): B marks the spot you look at for the whole party, with a
/// tall beam of light and your name and its distance over it, seen through hills and trees. Each
/// player has one mark at a time (a new one moves it); it fades after a while.
///
/// Network: a client sends its mark to the host, which checks it (a player in the session, a sane
/// spot, not too often) and passes it to everyone else with the player's name.
/// </summary>
public partial class NetPings : Node
{
    [Export] public Key PingKey { get; set; } = Key.B;
    /// <summary>How far you can mark, in m.</summary>
    [Export] public float Reach { get; set; } = 250.0f;
    /// <summary>A mark lasts this long, in s (the last FadeSeconds it fades).</summary>
    [Export] public float Lasts { get; set; } = 15.0f;
    [Export] public float FadeSeconds { get; set; } = 2.0f;
    /// <summary>A player can mark at most this often, in s (the host drops anything faster).</summary>
    [Export] public float Cooldown { get; set; } = 0.5f;
    [Export] public float BeamHeight { get; set; } = 40.0f;
    [Export] public Color MarkColour { get; set; } = new(1.0f, 0.78f, 0.32f);
    /// <summary>The beam is this see-through (0 clear, 1 solid).</summary>
    [Export] public float BeamAlpha { get; set; } = 0.35f;
    [Export] public float LabelHeight { get; set; } = 3.0f;
    /// <summary>Marks further than this from the carrier are refused, in m (a sanity limit).</summary>
    [Export] public float SaneDistance { get; set; } = 5000.0f;
    [Export] public string FirstHint { get; set; } = "{0} marked a spot. Press B to mark one for the others.";

    public NetSession? Session { get; set; }
    /// <summary>Marks this machine made, and marks from others shown here (tests).</summary>
    public int Sent { get; private set; }
    public int Seen { get; private set; }

    private sealed class Mark
    {
        public Node3D Node = null!;
        public Label3D Label = null!;
        public StandardMaterial3D Beam = null!;
        public string Name = "";
        public double Ends;
    }

    private readonly Dictionary<long, Mark> _marks = new();
    private readonly Dictionary<long, double> _lastFrom = new();
    private double _lastSent = -100.0;
    private bool _toldFirst;

    private bool Live => Session != null && Session.Active && Session.Joined;

    private static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>The session ended: every mark goes.</summary>
    public void Clear()
    {
        foreach (Mark mark in _marks.Values)
        {
            mark.Node.QueueFree();
        }
        _marks.Clear();
        _lastFrom.Clear();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.PhysicalKeycode != PingKey)
        {
            return;
        }
        // Only while playing (the mouse looks around), not in the map, the pack or a menu.
        if (!Live || Session!.LocalDead || Input.MouseMode != Input.MouseModeEnum.Captured || key.AltPressed || key.CtrlPressed)
        {
            return;
        }
        if (Aim(out Vector3 at))
        {
            MarkAt(at);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Where the camera looks, on the ground or whatever stands in the way (not the carrier).</summary>
    private bool Aim(out Vector3 at)
    {
        at = Vector3.Zero;
        Camera3D? camera = GetViewport()?.GetCamera3D();
        if (camera == null)
        {
            return false;
        }
        Vector3 from = camera.GlobalPosition;
        Vector3 to = from - camera.GlobalBasis.Z * Reach;
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to);
        PlayerController? player = Session?.LocalPlayer();
        if (player != null)
        {
            query.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
        }
        Godot.Collections.Dictionary hit = camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return false;
        }
        at = (Vector3)hit["position"];
        return true;
    }

    /// <summary>Marks a spot for everyone (the key, or the co-op probe).</summary>
    public void MarkAt(Vector3 at)
    {
        if (!Live || !at.IsFinite() || Now - _lastSent < Cooldown)
        {
            return;
        }
        _lastSent = Now;
        Sent++;
        NetSession session = Session!;
        Show(session.LocalId, session.LocalName, at, false);
        if (session.Mode == NetSession.Role.Host)
        {
            foreach (long id in session.Peers.Keys)
            {
                RpcId(id, MethodName.PeerPing, 1L, session.LocalName, at);
            }
        }
        else
        {
            RpcId(1, MethodName.Ping, at);
        }
    }

    /// <summary>Host: a client marked a spot; shown here and passed to the others.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Ping(Vector3 at)
    {
        long sender = Multiplayer.GetRemoteSenderId();
        NetSession? session = Session;
        if (session == null || session.Mode != NetSession.Role.Host || !session.Peers.TryGetValue(sender, out NetPeer? peer) || !Sane(at))
        {
            return;
        }
        if (_lastFrom.TryGetValue(sender, out double last) && Now - last < Cooldown)
        {
            return;
        }
        _lastFrom[sender] = Now;
        Show(sender, peer.Name, at, true);
        foreach (long id in session.Peers.Keys)
        {
            if (id != sender)
            {
                RpcId(id, MethodName.PeerPing, sender, peer.Name, at);
            }
        }
    }

    /// <summary>Client: someone's mark, from the host.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PeerPing(long who, string name, Vector3 at)
    {
        if (Session?.Mode != NetSession.Role.Client || who == Session.LocalId || !Sane(at))
        {
            return;
        }
        if (name.Length > 24)
        {
            name = name.Substring(0, 24);
        }
        Show(who, name, at, true);
    }

    private bool Sane(Vector3 at)
    {
        if (!at.IsFinite())
        {
            return false;
        }
        PlayerController? player = Session?.LocalPlayer();
        if (player == null)
        {
            return true;
        }
        return player.GlobalPosition.DistanceTo(at) <= SaneDistance;
    }

    private void Show(long who, string name, Vector3 at, bool fromOther)
    {
        if (fromOther)
        {
            Seen++;
            if (!_toldFirst)
            {
                _toldFirst = true;
                GameEvents.PublishHint(string.Format(FirstHint, name));
            }
        }
        if (!_marks.TryGetValue(who, out Mark? mark))
        {
            mark = Build(name);
            _marks[who] = mark;
            AddChild(mark.Node);
        }
        mark.Name = name;
        mark.Node.GlobalPosition = at;
        mark.Ends = Now + Lasts;
        mark.Node.Visible = true;
    }

    private Mark Build(string name)
    {
        Node3D node = new() { Name = "Mark" };
        StandardMaterial3D beam = new()
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(MarkColour, BeamAlpha),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false,
        };
        node.AddChild(new MeshInstance3D
        {
            Name = "Beam",
            Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.18f, Height = BeamHeight, RadialSegments = 8, Rings = 1 },
            MaterialOverride = beam,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = Vector3.Up * (BeamHeight * 0.5f),
        });
        node.AddChild(new MeshInstance3D
        {
            Name = "Foot",
            Mesh = new TorusMesh { InnerRadius = 0.55f, OuterRadius = 0.75f, Rings = 24, RingSegments = 6 },
            MaterialOverride = beam,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = Vector3.Up * 0.05f,
        });
        Label3D label = new()
        {
            Name = "Label",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 40,
            PixelSize = 0.0012f,
            OutlineSize = 10,
            NoDepthTest = true,
            FixedSize = true,
            Modulate = MarkColour,
            Position = Vector3.Up * LabelHeight,
        };
        node.AddChild(label);
        return new Mark { Node = node, Label = label, Beam = beam, Name = name };
    }

    public override void _Process(double delta)
    {
        if (_marks.Count == 0)
        {
            return;
        }
        Camera3D? camera = GetViewport()?.GetCamera3D();
        double now = Now;
        foreach (Mark mark in _marks.Values)
        {
            if (!mark.Node.Visible)
            {
                continue;
            }
            double left = mark.Ends - now;
            if (left <= 0.0)
            {
                mark.Node.Visible = false;
                continue;
            }
            float fade = Mathf.Clamp((float)left / Mathf.Max(FadeSeconds, 0.01f), 0.0f, 1.0f);
            mark.Beam.AlbedoColor = new Color(MarkColour, BeamAlpha * fade);
            mark.Label.Modulate = new Color(MarkColour, fade);
            string distance = "";
            if (camera != null)
            {
                distance = $"\n{Mathf.RoundToInt(camera.GlobalPosition.DistanceTo(mark.Node.GlobalPosition))} m";
            }
            mark.Label.Text = mark.Name + distance;
        }
    }
}
