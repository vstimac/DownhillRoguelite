using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// Applies the options that belong to the whole window: the interface size (the window's content
/// scale, so menus and HUD grow together for 2K/4K screens), the in-world path lines and overlays,
/// and the colour-blind filter layer. Other options are read where they act (camera, hotbar, dials).
/// </summary>
public partial class OptionsApplier : Node
{
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GameOptions.Load();
        AddChild(new ColourFilterLayer { Name = "ColourFilter" });
        GameOptions.Changed += Apply;
        GetTree().Root.ContentScaleFactor = GameOptions.UiScale;
    }

    public override void _ExitTree()
    {
        GameOptions.Changed -= Apply;
    }

    /// <summary>On a change in Options (at start the lines follow DebugDraw3D's own start-up and --promo).</summary>
    private void Apply()
    {
        Window root = GetTree().Root;
        root.ContentScaleFactor = GameOptions.UiScale;
        Debug.DebugDraw3D.SetEnabled(GameOptions.PathLines);
    }
}
