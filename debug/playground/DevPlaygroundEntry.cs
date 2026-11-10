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
}
