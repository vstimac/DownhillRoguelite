using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The sword: the Mixamo clips load and lay over the carrier; Z draws it into the hand (the mouse
/// buttons stop leaning); a swing lands on a target in front and costs stamina; pressing again chains
/// the next blow; the shield stops a blow from in front but not from behind; a kick shoves hard; and
/// it goes back on the hip.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/CombatTests.tscn
/// </summary>
public partial class CombatTests : Node3D
{
	[Export] public PlayerController? PlayerBody { get; set; }
	[Export] public TerrainStreamer? Streamer { get; set; }
	[Export] public float StartDown { get; set; } = 60.0f;

	private readonly List<string> _failures = new();

	public override async void _Ready()
	{
		MeleeCombat? combat = PlayerBody?.GetNodeOrNull<MeleeCombat>("MeleeCombat");
		if (PlayerBody == null || Streamer == null || combat == null)
		{
			GD.PushError("CombatTests: PlayerBody (with MeleeCombat) and Streamer are needed.");
			GetTree().Quit(1);
			return;
		}
		IRouteQuery? route = GetNodeOrNull<RunState>("/root/RunState")?.Route;
		Vector3 start = Streamer.SpawnPoint;
		if (route != null)
		{
			start = route.RoutePoint(Streamer.SpawnPoint.Z - StartDown);
		}
		PlayerBody.Teleport(start + Vector3.Up * 0.1f, 0.0f);
		PlayerBody.Health?.Refill();
		PlayerBody.Stamina?.Refill();
		await Wait(2.0f);

		int mapped = combat.Overlay?.Mapped ?? 0;
		Check("the sword clips load and map onto the carrier's rig", combat.Animator != null && combat.Animator.Loaded && mapped >= 40 && combat.Sword != null,
			$"loaded {combat.Animator?.Loaded}, bones mapped {mapped}, sword {combat.Sword != null}");

		combat.ToggleDraw();
		await Until(() => combat.State == MeleeCombat.Stance.Ready, 3.0f);
		await Wait(0.4f);
		Check("Z draws the sword into the hand", combat.State == MeleeCombat.Stance.Ready && combat.SwordInHand && GameEvents.SwordDrawn
			&& (combat.Overlay?.UpperWeight ?? 0.0f) > 0.9f,
			$"stance {combat.State}, in hand {combat.SwordInHand}, drawn {GameEvents.SwordDrawn}, overlay {combat.Overlay?.UpperWeight:0.00}");

		// A target right in front.
		Vector3 facing = -PlayerBody.GlobalBasis.Z;
		facing.Y = 0.0f;
		facing = facing.Normalized();
		MeleeDummy dummy = new() { Name = "Dummy" };
		AddChild(dummy);
		dummy.GlobalPosition = PlayerBody.GlobalPosition + facing * 1.3f;
		float staminaBefore = PlayerBody.Stamina?.Current ?? 0.0f;
		combat.Attack();
		await Until(() => dummy.Blows > 0, 2.0f);
		float spent = staminaBefore - (PlayerBody.Stamina?.Current ?? 0.0f);
		Check("a swing lands on the target in front and costs stamina", dummy.Blows == 1 && dummy.DamageTaken > 0.0f && spent > combat.AttackCost * 0.5f
			&& dummy.LastPush.Dot(facing) > 0.0f,
			$"blows {dummy.Blows}, damage {dummy.DamageTaken:0}, stamina spent {spent:0.0}, push {dummy.LastPush}");

		// Pressing again during the swing chains the next blow.
		combat.Attack();
		await Until(() => combat.ComboIndex >= 1 || combat.State == MeleeCombat.Stance.Ready, 3.0f);
		bool chained = combat.ComboIndex >= 1;
		await Until(() => dummy.Blows >= 2, 2.0f);
		Check("pressing again chains the next blow", chained && dummy.Blows >= 2, $"blow {combat.ComboIndex + 1} of the chain, blows {dummy.Blows}");
		await Until(() => combat.State == MeleeCombat.Stance.Ready, 4.0f);

		// The shield.
		combat.SetBlock(true);
		await Wait(0.4f);
		bool front = GameEvents.TryBlock(-facing, "test");
		bool back = GameEvents.TryBlock(facing, "test");
		Check("the shield stops a blow from in front, not from behind", combat.State == MeleeCombat.Stance.Blocking && front && !back,
			$"stance {combat.State}, front {front}, back {back}, blocks {combat.BlocksMade}");
		combat.SetBlock(false);
		await Until(() => combat.State == MeleeCombat.Stance.Ready, 2.0f);

		// A kick shoves harder than a swing.
		int blows = dummy.Blows;
		combat.Kick();
		await Until(() => dummy.Blows > blows, 2.0f);
		Check("a kick shoves the target hard", dummy.Blows > blows && dummy.LastPush.Length() >= combat.KickPush * 0.9f && combat.KicksLanded == 1,
			$"kicks landed {combat.KicksLanded}, push {dummy.LastPush.Length():0.0} m/s");
		await Until(() => combat.State == MeleeCombat.Stance.Ready, 3.0f);

		combat.ToggleDraw();
		await Until(() => combat.State == MeleeCombat.Stance.Sheathed, 3.0f);
		await Wait(0.5f);
		Check("Z puts it back on the hip", combat.State == MeleeCombat.Stance.Sheathed && !combat.SwordInHand && !GameEvents.SwordDrawn
			&& (combat.Overlay?.UpperWeight ?? 1.0f) < 0.1f,
			$"stance {combat.State}, in hand {combat.SwordInHand}, drawn {GameEvents.SwordDrawn}, overlay {combat.Overlay?.UpperWeight:0.00}");

		// A copy of the game without the sword clips (a build made from git alone): Z must say so,
		// not start a sword that only makes its sound. A sword with no rig to drive stands in for it.
		MeleeCombat bare = new() { Name = "BareSword" };
		AddChild(bare);
		await Wait(0.3f);
		string hint = "";
		void Heard(string text)
		{
			hint = text;
		}
		GameEvents.HintShown += Heard;
		bare._UnhandledInput(new InputEventAction { Action = MeleeCombat.ActionDraw, Pressed = true });
		bare.ToggleDraw();
		GameEvents.HintShown -= Heard;
		Check("without the sword clips Z says so and the sword stays away", bare.State == MeleeCombat.Stance.Sheathed && !GameEvents.SwordDrawn && hint == MeleeCombat.MissingClipsHint,
			$"stance {bare.State}, drawn {GameEvents.SwordDrawn}, hint '{hint}'");
		bare.QueueFree();
		Finish();
	}

	private void Finish()
	{
		if (_failures.Count == 0)
		{
			GD.Print("COMBAT TESTS: PASS");
			GetTree().Quit(0);
		}
		else
		{
			GD.Print("COMBAT TESTS: FAIL (" + string.Join(", ", _failures) + ")");
			GetTree().Quit(1);
		}
	}

	private async Task Wait(float seconds)
	{
		float dt = 1.0f / Engine.PhysicsTicksPerSecond;
		for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
	}

	private async Task<float> Until(System.Func<bool> done, float timeout)
	{
		float dt = 1.0f / Engine.PhysicsTicksPerSecond;
		float elapsed = 0.0f;
		while (!done() && elapsed < timeout)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			elapsed += dt;
		}
		return elapsed;
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
