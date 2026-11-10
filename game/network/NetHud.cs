using System.Text;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>
/// The co-op corner note: who is in the session (and who has fallen), the session's status, and
/// while fallen whom you watch and when you come back. Shown only during a session; presentation only.
/// </summary>
public partial class NetHud : CanvasLayer
{
    [Export] public Vector2 Corner { get; set; } = new(18.0f, 300.0f);
    [Export] public float Width { get; set; } = 300.0f;

    public NetSession? Session { get; set; }
    public Label? Text => _label;

    private Label? _label;
    private double _refresh;

    public override void _Ready()
    {
        Layer = 5;
        ProcessMode = ProcessModeEnum.Always;
        _label = UiStyle.MakeLabel("", UiStyle.SmallSize, false, UiStyle.Paper);
        _label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        _label.AddThemeConstantOverride("shadow_offset_x", 2);
        _label.AddThemeConstantOverride("shadow_offset_y", 2);
        _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _label.Position = Corner;
        _label.Size = new Vector2(Width, 10.0f);
        _label.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_label);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0.0 || _label == null)
        {
            return;
        }
        _refresh = 0.25;
        NetSession? session = Session;
        Visible = session != null && session.Active;
        if (!Visible || session == null)
        {
            return;
        }
        StringBuilder text = new();
        string role = "Co-op";
        if (session.Mode == NetSession.Role.Host)
        {
            role = $"Co-op · hosting on port {session.Port}";
        }
        text.AppendLine(role);
        text.AppendLine($"• {session.LocalName} (you){Fallen(session.LocalDead)}");
        foreach (NetPeer peer in session.Peers.Values)
        {
            text.AppendLine($"• {peer.Name}{Fallen(peer.Dead)}");
        }
        if (session.LocalDead)
        {
            if (session.Watching != null)
            {
                text.AppendLine($"Watching {session.Watching.PlayerName} (Tab: next)");
            }
            text.AppendLine($"Back at the last station in {Mathf.CeilToInt(session.RespawnIn)} s");
        }
        if (!session.Joined)
        {
            text.AppendLine(session.Status);
        }
        _label.Text = text.ToString().TrimEnd();
    }

    private static string Fallen(bool dead)
    {
        if (dead)
        {
            return " — fallen";
        }
        return "";
    }
}
