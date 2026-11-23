using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>F6 entry point. The console survives scene changes only in this developer session.</summary>
public partial class DevPlayground : Node
{
	[Export] public Godot.Collections.Array<DevPlaygroundEntry> Labs { get; set; } = new();
	[Export] public ulong Seed { get; set; } = 1;
	/// <summary>The lab it opens with, and the one "back to the start" returns to.</summary>
	[Export] public string HomeLab { get; set; } = "sandbox";
	[Export] public string Heading { get; set; } = "THE DESCENT / DEV PLAYGROUND";
	[Export(PropertyHint.MultilineText)] public string Intro { get; set; } = "Jedno mjesto za testiranje. F10 otvara/zatvara ovaj izbornik i pauzira probu. Promjena laboratorija resetira trenutnu probu.";

	public override void _Ready()
	{
		Callable.From(() => {
			DevPlaygroundConsole console = new() { Name = "DevPlaygroundConsole", Labs = Labs, Seed = Seed, HomeLab = HomeLab, Heading = Heading, Intro = Intro };
			GetTree().Root.AddChild(console);
			_ = console.OpenLab(HomeLab, showMenu: true);
		}).CallDeferred();
	}
}
