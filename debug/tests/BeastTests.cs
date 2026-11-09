using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the night beasts (docs/night-beasts.md): none come by day; at night one
/// arrives behind the carrier, out of sight; it stalks at a distance; it growls (wind-up) before it
/// charges and a hit throws the carrier into the air; a late sidestep makes it miss; with food in the
/// pack the she-wolf tears it out instead; fire or dawn sends every beast away; deeper circles bring
/// more beasts and the lion only from its own circle on.
/// The director's own _PhysicsProcess is off; the test drives Step frame by frame.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/BeastTests.tscn
/// </summary>
public partial class BeastTests : Node3D
{
	[Export] public PlayerController? PlayerBody { get; set; }
	[Export] public TerrainStreamer? Streamer { get; set; }
	[Export] public BeastDirector? Director { get; set; }
	/// <summary>The test stands this far down the route from the summit, in m.</summary>
	[Export] public float StartDown { get; set; } = 150.0f;

	private readonly List<string> _failures = new();
	private int _launches;
	private string _dropped = "";

	public override async void _Ready()
	{
		if (PlayerBody == null || Streamer == null || Director?.Roster == null)
		{
			GD.PushError("BeastTests: PlayerBody, Streamer and Director (with Roster) are needed.");
			GetTree().Quit(1);
			return;
		}
		Director.SetPhysicsProcess(false);
		Director.CircleOverride = 0;
		GameEvents.LaunchRequested += OnLaunch;
		GameEvents.DropRequested += OnDrop;
		// Down the route, so there is mountain behind (uphill) as well; the summit has only its back side.
		IRouteQuery? route = GetNodeOrNull<RunState>("/root/RunState")?.Route;
		Vector3 start = Streamer.SpawnPoint;
		if (route != null)
		{
			start = route.RoutePoint(Streamer.SpawnPoint.Z - StartDown);
		}
		await Place(PlayerBody, start, 4.0f);

		await TestDay(PlayerBody, Director);
		await TestArrival(PlayerBody, Director);
		await TestStalk(PlayerBody, Director);
		await TestCharge(PlayerBody, Director);
		await TestSidestep(PlayerBody, Director);
		await TestNoticed(PlayerBody, Director);
		await TestMegaphone(PlayerBody, Director);
		await TestFood(PlayerBody, Director);
		await TestDawn(PlayerBody, Director);
		TestDepth(PlayerBody, Director);

		GameEvents.LaunchRequested -= OnLaunch;
		GameEvents.DropRequested -= OnDrop;
		if (_failures.Count == 0)
		{
			GD.Print("BEAST TESTS: PASS");
			GetTree().Quit(0);
		}
		else
		{
			GD.Print($"BEAST TESTS: FAIL ({_failures.Count})");
			GetTree().Quit(1);
		}
	}

	private void OnLaunch(Vector3 horizontal, float height, string cause) => _launches++;

	private void OnDrop(string itemId) => _dropped = itemId;

	private async Task TestDay(PlayerController player, BeastDirector director)
	{
		await Run(player, director, director.ArrivalDelay.Y + 5.0f, 1.0f, false);
		Check("no beasts come by day", director.Beasts.Count == 0 && !director.Hunting,
			$"{director.Beasts.Count} beasts, hunting {director.Hunting}");
	}

	private async Task TestArrival(PlayerController player, BeastDirector director)
	{
		director.Clear();
		float waited = 0.0f;
		float dt = 1.0f / Engine.PhysicsTicksPerSecond;
		Vector3 carrier = player.GlobalPosition;
		while (director.Beasts.Count == 0 && waited < director.ArrivalDelay.Y + 2.0f)
		{
			director.Step(dt, player.GlobalPosition, 0.0f, false, false);
			waited += dt;
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
		bool arrived = director.Beasts.Count == 1;
		float distance = 0.0f;
		bool behind = false;
		string name = "";
		if (arrived)
		{
			NightBeast beast = director.Beasts[0];
			Vector3 offset = beast.GlobalPosition - carrier;
			distance = new Vector2(offset.X, offset.Z).Length();
			behind = offset.Z > 0.0f;
			name = beast.Species?.DisplayName ?? "";
		}
		Check("at night a beast arrives behind the carrier, out of sight",
			arrived && waited >= director.ArrivalDelay.X - 0.1f && distance >= director.SpawnDistance.X - 2.0f && behind && name == "she-wolf",
			$"{director.Beasts.Count} beast(s) ({name}) after {waited:0.0} s, {distance:0} m away, uphill {behind}");
	}

	private async Task TestStalk(PlayerController player, BeastDirector director)
	{
		if (director.Beasts.Count == 0)
		{
			Check("a beast stalks at a distance", false, "no beast");
			return;
		}
		NightBeast beast = director.Beasts[0];
		BeastSpecies species = beast.Species!;
		beast.AttackTimer = 1000.0f;
		float closest = float.MaxValue;
		float dt = 1.0f / Engine.PhysicsTicksPerSecond;
		for (float elapsed = 0.0f; elapsed < 15.0f; elapsed += dt)
		{
			director.Step(dt, player.GlobalPosition, 0.0f, false, false);
			if (elapsed > 8.0f)
			{
				closest = Mathf.Min(closest, Flat(beast.GlobalPosition - player.GlobalPosition));
			}
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
		float far = Flat(beast.GlobalPosition - player.GlobalPosition);
		Check("a beast stalks at a distance without attacking", beast.State == NightBeast.Mode.Stalk && closest >= species.StalkRadius.X * 0.6f && far <= species.StalkRadius.Y + 10.0f && _launches == 0,
			$"closest {closest:0.0} m, now {far:0.0} m (ring {species.StalkRadius.X:0}–{species.StalkRadius.Y:0} m), launches {_launches}");
	}

	private async Task TestCharge(PlayerController player, BeastDirector director)
	{
		if (director.Beasts.Count == 0)
		{
			Check("a charge throws the carrier", false, "no beast");
			return;
		}
		NightBeast beast = director.Beasts[0];
		BeastSpecies species = beast.Species!;
		_launches = 0;
		float startY = player.GlobalPosition.Y;
		float peak = startY;
		bool woundUp = false;
		bool chargedAfterWindUp = false;
		int hitsBefore = beast.Hits;
		beast.AttackTimer = 0.0f;
		float dt = 1.0f / Engine.PhysicsTicksPerSecond;
		for (float elapsed = 0.0f; elapsed < species.WindUp + 6.0f && _launches == 0; elapsed += dt)
		{
			director.Step(dt, player.GlobalPosition, 0.0f, false, false);
			woundUp = woundUp || beast.State == NightBeast.Mode.WindUp;
			chargedAfterWindUp = chargedAfterWindUp || (woundUp && beast.State == NightBeast.Mode.Charge);
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
		for (float elapsed = 0.0f; elapsed < 1.5f; elapsed += dt)
		{
			director.Step(dt, player.GlobalPosition, 0.0f, false, false);
			peak = Mathf.Max(peak, player.GlobalPosition.Y);
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
		float rise = peak - startY;
		Check("it growls before it charges, and a hit throws the carrier into the air",
			woundUp && chargedAfterWindUp && _launches == 1 && beast.Hits == hitsBefore + 1 && rise >= species.ThrowHeight * 0.6f,
			$"wind-up {woundUp}, charge {chargedAfterWindUp}, launches {_launches}, carrier rose {rise:0.0} m (aim {species.ThrowHeight:0.0} m)");
		await Recover(player, director);
	}

	private async Task TestSidestep(PlayerController player, BeastDirector director)
	{
		if (director.Beasts.Count == 0)
		{
			Check("a late sidestep makes the charge miss", false, "no beast");
			return;
		}
		NightBeast beast = director.Beasts[0];
		await Stalk(player, director, 6.0f);
		_launches = 0;
		int hitsBefore = beast.Hits;
		bool stepped = false;
		bool missed = false;
		bool charged = false;
		float closest = float.MaxValue;
		beast.AttackTimer = 0.0f;
		float dt = 1.0f / Engine.PhysicsTicksPerSecond;
		for (float elapsed = 0.0f; elapsed < 8.0f && !missed; elapsed += dt)
		{
			if (!stepped && beast.State == NightBeast.Mode.Charge)
			{
				Vector3 toward = beast.GlobalPosition - player.GlobalPosition;
				toward.Y = 0.0f;
				if (toward.Length() < 5.0f)
				{
					// Dive aside at the last moment: 2.5 m across the charge.
					Vector3 across = toward.Normalized().Cross(Vector3.Up);
					player.Teleport(player.GlobalPosition + across * 2.5f, 0.0f);
					stepped = true;
				}
			}
			director.Step(dt, player.GlobalPosition, 0.0f, false, false);
			charged = charged || beast.State == NightBeast.Mode.Charge;
			closest = Mathf.Min(closest, Flat(beast.GlobalPosition - player.GlobalPosition));
			missed = stepped && beast.State == NightBeast.Mode.Retreat;
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
		Check("a late sidestep makes the charge miss", stepped && missed && _launches == 0 && beast.Hits == hitsBefore,
			$"charged {charged}, came within {closest:0.0} m, stepped aside {stepped}, beast overshot {missed}, launches {_launches}, now {beast.State}");
		await Recover(player, director);
	}

	private async Task TestNoticed(PlayerController player, BeastDirector director)
	{
		if (director.Beasts.Count == 0)
		{
			Check("a noticed carrier is attacked sooner", false, "no beast");
			return;
		}
		NightBeast beast = director.Beasts[0];
		await Stalk(player, director, 6.0f);
		float dt = 1.0f / Engine.PhysicsTicksPerSecond;
		float[] spent = new float[2];
		float[] levels = { 1.0f, 2.0f };
		for (int i = 0; i < levels.Length; i++)
		{
			GameEvents.PublishDetectability(levels[i]);
			beast.AttackTimer = 100.0f;
			for (float elapsed = 0.0f; elapsed < 1.0f; elapsed += dt)
			{
				director.Step(dt, player.GlobalPosition, 0.0f, false, false);
				await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			}
			spent[i] = 100.0f - beast.AttackTimer;
		}
		GameEvents.PublishDetectability(1.0f);
		beast.AttackTimer = 1000.0f;
		Check("a noticed carrier (podium, megaphone echo) is attacked sooner",
			beast.State == NightBeast.Mode.Stalk && spent[1] > spent[0] * 1.8f,
			$"attack countdown in 1 s: {spent[0]:0.00} s noticed x1, {spent[1]:0.00} s noticed x2");
	}

	private async Task TestMegaphone(PlayerController player, BeastDirector director)
	{
		if (director.Beasts.Count == 0)
		{
			Check("a megaphone blast throws off a crouching beast", false, "no beast");
			return;
		}
		NightBeast beast = director.Beasts[0];
		await Stalk(player, director, 6.0f);
		_launches = 0;
		beast.AttackTimer = 0.0f;
		float dt = 1.0f / Engine.PhysicsTicksPerSecond;
		for (float elapsed = 0.0f; elapsed < 3.0f && beast.State != NightBeast.Mode.WindUp; elapsed += dt)
		{
			director.Step(dt, player.GlobalPosition, 0.0f, false, false);
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
		bool crouched = beast.State == NightBeast.Mode.WindUp;
		float far = Flat(beast.GlobalPosition - player.GlobalPosition);
		GameEvents.PublishMegaphone(player.GlobalPosition, far + 5.0f, 4.0f, 10.0f);
		bool brokeOff = beast.State == NightBeast.Mode.Retreat && beast.ConfusedFor > 0.0f;
		bool attacked = false;
		for (float elapsed = 0.0f; elapsed < 3.5f; elapsed += dt)
		{
			beast.AttackTimer = 0.0f;
			director.Step(dt, player.GlobalPosition, 0.0f, false, false);
			attacked = attacked || beast.State == NightBeast.Mode.WindUp || beast.State == NightBeast.Mode.Charge;
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
		Check("a megaphone blast breaks off a crouching beast, and it doesn't attack while thrown off",
			crouched && brokeOff && !attacked && _launches == 0,
			$"crouched {crouched} at {far:0.0} m, broke off {brokeOff}, attacked while confused {attacked}, launches {_launches}");
		beast.AttackTimer = 1000.0f;
		await Stalk(player, director, 2.0f);
	}

	private async Task TestFood(PlayerController player, BeastDirector director)
	{
		if (director.Beasts.Count == 0)
		{
			Check("the she-wolf tears food out of the pack", false, "no beast");
			return;
		}
		NightBeast beast = director.Beasts[0];
		await Stalk(player, director, 6.0f);
		GameEvents.PublishCarriedItems(new List<string> { "rope", "dried_fruit" });
		_launches = 0;
		_dropped = "";
		beast.AttackTimer = 0.0f;
		float dt = 1.0f / Engine.PhysicsTicksPerSecond;
		for (float elapsed = 0.0f; elapsed < beast.Species!.WindUp + 6.0f && _dropped.Length == 0; elapsed += dt)
		{
			director.Step(dt, player.GlobalPosition, 0.0f, false, false);
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
		GameEvents.PublishCarriedItems(new List<string>());
		Check("with food in the pack the she-wolf tears it out (and only knocks the carrier)",
			_dropped == "dried_fruit" && _launches == 1,
			$"dropped '{_dropped}', launches {_launches}");
		await Recover(player, director);
	}

	private async Task TestDawn(PlayerController player, BeastDirector director)
	{
		if (director.Beasts.Count == 0)
		{
			Check("dawn sends the beasts away", false, "no beast");
			return;
		}
		NightBeast beast = director.Beasts[0];
		float dt = 1.0f / Engine.PhysicsTicksPerSecond;
		director.Step(dt, player.GlobalPosition, 0.0f, true, false);
		bool leftAtFire = beast.State == NightBeast.Mode.Leave && !director.Hunting;
		await Run(player, director, 20.0f, 1.0f, false);
		Check("fire or dawn sends every beast away", leftAtFire && director.Beasts.Count == 0,
			$"left at the fire {leftAtFire}, {director.Beasts.Count} still around after 20 s of daylight");
	}

	private void TestDepth(PlayerController player, BeastDirector director)
	{
		director.Clear();
		PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
		director.CircleOverride = 0;
		int limbo = director.MostBeasts();
		bool lionInLimbo = false;
		for (int i = 0; i < 12; i++)
		{
			NightBeast? beast = director.Bring(player.GlobalPosition, space);
			lionInLimbo = lionInLimbo || beast?.Species?.DisplayName == "lion";
		}
		director.Clear();
		director.CircleOverride = 6;
		int deep = director.MostBeasts();
		director.CircleOverride = 0;
		Check("deeper circles bring more beasts, and the lion stays out of Limbo", limbo == 1 && deep == 3 && !lionInLimbo,
			$"at most {limbo} in Limbo, {deep} in the seventh circle, lion in Limbo {lionInLimbo}");
	}

	/// <summary>Lets the carrier land and get up again, the beast keeping its distance meanwhile.</summary>
	private async Task Recover(PlayerController player, BeastDirector director)
	{
		await Stalk(player, director, 4.0f);
		if (director.Beasts.Count > 0)
		{
			director.Beasts[0].AttackTimer = 1000.0f;
		}
		await Place(player, player.GlobalPosition, 1.0f);
	}

	private async Task Stalk(PlayerController player, BeastDirector director, float seconds)
	{
		foreach (NightBeast beast in director.Beasts)
		{
			beast.AttackTimer = 1000.0f;
		}
		await Run(player, director, seconds, 0.0f, false);
	}

	private async Task Run(PlayerController player, BeastDirector director, float seconds, float daylight, bool safe)
	{
		float dt = 1.0f / Engine.PhysicsTicksPerSecond;
		for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
		{
			director.Step(dt, player.GlobalPosition, daylight, safe, false);
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
	}

	private static float Flat(Vector3 offset) => new Vector2(offset.X, offset.Z).Length();

	private async Task Place(PlayerController player, Vector3 position, float settle)
	{
		player.Teleport(position + Vector3.Up * 0.1f, 0.0f);
		player.Health?.Refill();
		float dt = 1.0f / Engine.PhysicsTicksPerSecond;
		for (float elapsed = 0.0f; elapsed < settle; elapsed += dt)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
	}

	private void Check(string name, bool passed, string detail)
	{
		if (passed)
		{
			GD.Print($"PASS  {name}: {detail}");
		}
		else
		{
			GD.Print($"FAIL  {name}: {detail}");
			_failures.Add(name);
		}
	}
}
