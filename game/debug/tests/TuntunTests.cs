using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Tuntun Sahur (Alt+T): he appears on the ground ten metres ahead of the carrier, runs to him and
/// swings; a fast bat knocks the carrier down as a ragdoll, a slow one only shoves him.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/TuntunTests.tscn
/// </summary>
public partial class TuntunTests : Node3D
{
	[Export] public PlayerController? PlayerBody { get; set; }
	[Export] public TerrainStreamer? Streamer { get; set; }
	[Export] public TuntunSpawner? Spawner { get; set; }
	[Export] public float StartDown { get; set; } = 60.0f;
	/// <summary>Longest the test waits for him to reach the carrier and swing, in s.</summary>
	[Export] public float HitTimeout { get; set; } = 8.0f;

	private readonly List<string> _failures = new();
	private int _knockdowns;
	private int _launches;

	public override async void _Ready()
	{
		if (PlayerBody == null || Streamer == null || Spawner == null)
		{
			GD.PushError("TuntunTests: PlayerBody, Streamer and Spawner are needed.");
			GetTree().Quit(1);
			return;
		}
		GameEvents.KnockdownRequested += (_, _) => _knockdowns++;
		GameEvents.LaunchRequested += (_, _, _) => _launches++;
		IRouteQuery? route = GetNodeOrNull<RunState>("/root/RunState")?.Route;
		Vector3 start = Streamer.SpawnPoint;
		if (route != null)
		{
			start = route.RoutePoint(Streamer.SpawnPoint.Z - StartDown);
		}
		await Frames(PlayerBody, start, 3.0f);

		TuntunSahur? tuntun = Spawner.Spawn();
		Vector3 carrier = PlayerBody.GlobalPosition;
		float ahead = 0.0f;
		if (tuntun != null)
		{
			Vector3 offset = tuntun.GlobalPosition - carrier;
			ahead = new Vector2(offset.X, offset.Z).Length();
		}
		// He starts on the slope's surface (not the carrier's height), so he stands at once.
		await Wait(0.3f);
		bool grounded = tuntun != null && tuntun.IsOnFloor();
		Check("Alt+T brings him ten metres ahead, on the ground", tuntun != null && Mathf.Abs(ahead - Spawner.SpawnDistance) < 0.5f && grounded,
			$"spawned {tuntun != null}, {ahead:0.0} m away, on the ground {grounded}");
		if (tuntun == null)
		{
			Finish();
			return;
		}

		// A fast swing: he reaches the carrier, the bat finds the capsule and the carrier goes down.
		tuntun.StrikeSeconds = new Vector2(0.16f, 0.16f);
		float waited = await Until(() => tuntun.Hits > 0, HitTimeout);
		await Wait(0.1f);
		Check("he runs to the carrier and the bat hits him", tuntun.Hits > 0, $"after {waited:0.0} s, hits {tuntun.Hits}");
		Check("a fast bat makes the carrier a ragdoll", tuntun.LastHitKnockedDown && _knockdowns == 1 && GameEvents.IsDowned,
			$"bat {tuntun.LastHitSpeed:0.0} m/s (ragdoll from {tuntun.RagdollSpeed:0.0}), knockdowns {_knockdowns}, downed {GameEvents.IsDowned}");

		// Back up; a slow swing only shoves.
		await Until(() => !GameEvents.IsDowned, 10.0f);
		tuntun.StrikeSeconds = new Vector2(2.0f, 2.0f);
		int hits = tuntun.Hits;
		int launches = _launches;
		await Until(() => tuntun.Hits > hits, HitTimeout + 4.0f);
		Check("a slow bat only shoves him", tuntun.Hits > hits && !tuntun.LastHitKnockedDown && _launches > launches && _knockdowns == 1,
			$"bat {tuntun.LastHitSpeed:0.0} m/s, launches {_launches - launches}, knockdowns {_knockdowns}");

		TuntunSahur? second = Spawner.Spawn();
		await Wait(0.1f);
		Check("pressing it again replaces him", second != null && !IsInstanceValid(tuntun), $"old one gone {!IsInstanceValid(tuntun)}");
		Finish();
	}

	private void Finish()
	{
		if (_failures.Count == 0)
		{
			GD.Print("TUNTUN TESTS: PASS");
			GetTree().Quit(0);
		}
		else
		{
			GD.Print("TUNTUN TESTS: FAIL (" + string.Join(", ", _failures) + ")");
			GetTree().Quit(1);
		}
	}

	private async Task Frames(PlayerController player, Vector3 position, float settle)
	{
		player.Teleport(position + Vector3.Up * 0.1f, 0.0f);
		player.Health?.Refill();
		await Wait(settle);
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
