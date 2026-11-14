using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// `-- --side-photo` in any scene: a camera beside the player (on its right, following it), for
/// checking how the backpack sits on the back from the side in that scene's own conditions.
/// `--side-walk` / `--side-sprint` also hold forward (and sprint). Added by DebugDraw3D.
/// </summary>
public partial class SidePhoto : Camera3D
{
    [Export] public float Distance { get; set; } = 2.4f;
    [Export] public float Height { get; set; } = 1.25f;
    [Export] public float Aim { get; set; } = 1.15f;
    private PlayerController? _player;

    public override void _Process(double delta)
    {
        if (_player == null)
        {
            _player = Find(GetTree().Root);
            if (_player == null) { return; }
            string[] args = OS.GetCmdlineUserArgs();
            bool sprint = System.Array.IndexOf(args, "--side-sprint") >= 0;
            if (sprint || System.Array.IndexOf(args, "--side-walk") >= 0) { Input.ActionPress("player_move_forward"); }
            if (sprint) { Input.ActionPress("player_sprint"); }
        }
        Current = true;
        Vector3 side = _player.GlobalBasis.X.Normalized();
        GlobalPosition = _player.GlobalPosition + side * Distance + Vector3.Up * Height;
        LookAt(_player.GlobalPosition + Vector3.Up * Aim);
    }

    private static PlayerController? Find(Node node)
    {
        if (node is PlayerController player) { return player; }
        foreach (Node child in node.GetChildren())
        {
            if (Find(child) is PlayerController found) { return found; }
        }
        return null;
    }
}
