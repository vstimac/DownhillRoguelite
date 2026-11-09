using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>The beasts that can hunt at night (BeastDirector picks among them by circle and weight).</summary>
[GlobalClass]
public partial class BeastRoster : Resource
{
    [Export] public BeastSpecies[] Beasts { get; set; } = System.Array.Empty<BeastSpecies>();
}
