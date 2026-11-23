using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// Single player, only when asked for (GameOptions.PauseWhileOpen, off by default): stops the world
/// (SceneTree.Paused) while the pack is set down and open, so sorting the load never costs a fall,
/// warmth or daylight. By default the pack is set down in the running world, as in co-op.
/// The HUD and the pack screen keep running (ProcessMode Always); GameEvents.PagesFrozen tells them
/// this pause is theirs and not the pause menu's.
/// Co-op never stops: the others play on in the same world, and a stopped host would stop its
/// creatures and its carrier (and the pack being set down) for everyone.
/// </summary>
public partial class PageFreeze : Node
{
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        bool want = GameEvents.IsPackOpen && GameOptions.PauseWhileOpen && !GameEvents.SharedWorldActive;
        SceneTree tree = GetTree();
        if (want)
        {
            if (!GameEvents.PagesFrozen)
            {
                GameEvents.PublishPagesFrozen(true);
            }
            // Also after the pause menu resumes while a page is still open.
            if (!tree.Paused)
            {
                tree.Paused = true;
            }
        }
        else if (GameEvents.PagesFrozen)
        {
            GameEvents.PublishPagesFrozen(false);
            tree.Paused = false;
        }
    }

    public override void _ExitTree()
    {
        if (GameEvents.PagesFrozen)
        {
            GameEvents.PublishPagesFrozen(false);
            GetTree().Paused = false;
        }
    }
}
