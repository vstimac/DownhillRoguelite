using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>An editable manual laboratory entry, not an automated scene that quits the game.</summary>
[GlobalClass]
public partial class DevPlaygroundEntry : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string Title { get; set; } = "";
    [Export] public string Category { get; set; } = "";
    [Export(PropertyHint.File, "*.tscn")] public string ScenePath { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Controls { get; set; } = "";
    /// <summary>
    /// What the lab sets up once it is loaded, steps split by ';': `skip-start` (no title screen),
    /// `at=Pad 6 m` (the first checkpoint whose name starts so), `time=23`, `item=res://...tres`,
    /// `hurt=40`, `stamina=0.3`, `beast` (a night beast comes once the ground is there).
    /// </summary>
    [Export(PropertyHint.MultilineText)] public string Setup { get; set; } = "";
}
