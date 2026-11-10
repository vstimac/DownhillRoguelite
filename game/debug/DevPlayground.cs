using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>F6 entry point. The console survives scene changes only in this developer session.</summary>
public partial class DevPlayground : Node
{
	[Export] public Godot.Collections.Array<DevPlaygroundEntry> Labs { get; set; } = new();
	[Export] public ulong Seed { get; set; } = 1;

	public override void _Ready()
	{
		Callable.From(() => {
			DevPlaygroundConsole console = new() { Name = "DevPlaygroundConsole", Labs = Labs, Seed = Seed };
			GetTree().Root.AddChild(console);
			_ = console.OpenLab("sandbox", showMenu: true);
		}).CallDeferred();
	}
}
