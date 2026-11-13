using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// Single player: stops the world (SceneTree.Paused) while the inventory page or the packing page is
/// open, so sorting the load never costs a fall, warmth or daylight (GameOptions.PauseWhileOpen).
/// The HUD and the pack screen keep running (ProcessMode Always); GameEvents.PagesFrozen tells them
/// this pause is theirs and not the pause menu's.
/// </summary>
public partial class PageFreeze : Node
{
    public InventoryScreen? Inventory { get; set; }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        bool open = (Inventory != null && Inventory.IsOpen && Inventory.Visible) || GameEvents.IsPackOpen;
        bool want = open && GameOptions.PauseWhileOpen;
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
