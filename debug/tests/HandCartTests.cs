using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The hand cart (Alt+C, or at stations): it settles on the ground and parks; taken by the handles
/// it trails the carrier downhill and slows them by its weight; let go, it parks again; things put
/// on its bed become cargo and add to its weight.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/HandCartTests.tscn
/// </summary>
public partial class HandCartTests : Node3D
{
	[Export] public PlayerController? PlayerBody { get; set; }
	[Export] public TerrainStreamer? Streamer { get; set; }
	[Export] public CartSpawner? Spawner { get; set; }
	[Export] public float StartDown { get; set; } = 60.0f;
	/// <summary>How long the carrier walks with the cart, in s.</summary>
	[Export] public float WalkSeconds { get; set; } = 6.0f;
	/// <summary>Farthest the handles may trail from where they are held while walking, in m.</summary>
	[Export] public float TrailTolerance { get; set; } = 1.0f;

	private readonly List<string> _failures = new();

	public override async void _Ready()
	{
		if (PlayerBody == null || Streamer == null || Spawner == null)
		{
			GD.PushError("HandCartTests: PlayerBody, Streamer and Spawner are needed.");
			GetTree().Quit(1);
			return;
		}
		IRouteQuery? route = GetNodeOrNull<RunState>("/root/RunState")?.Route;
		Vector3 start = Streamer.SpawnPoint;
		Vector3 downhill = Vector3.Forward;
		if (route != null)
		{
			start = route.RoutePoint(Streamer.SpawnPoint.Z - StartDown);
			Vector3 next = route.RoutePoint(Streamer.SpawnPoint.Z - StartDown - 20.0f);
			downhill = new Vector3(next.X - start.X, 0.0f, next.Z - start.Z).Normalized();
		}
		PlayerBody.Teleport(start + Vector3.Up * 0.1f, Mathf.Atan2(-downhill.X, -downhill.Z));
		PlayerBody.Health?.Refill();
		await Wait(3.0f);

		HandCart? cart = Spawner.Spawn();
		if (cart == null)
		{
			Check("Alt+C sets a cart down", false, "no cart");
			Finish();
			return;
		}
		float settled = await Until(() => cart.Parked, 6.0f);
		await Wait(0.2f);
		Vector3 parked = cart.GlobalPosition;
		await Wait(1.0f);
		Check("it settles on the ground and stays parked", cart.Parked && cart.GlobalPosition.DistanceTo(parked) < 0.05f,
			$"after {settled:0.0} s, frozen {cart.Freeze}, moved {cart.GlobalPosition.DistanceTo(parked):0.00} m");

		// Turn it so the handles point downhill, stand in front of them facing away, take it and walk downhill.
		Vector3 at = cart.GlobalPosition;
		cart.GlobalTransform = new Transform3D(Basis.LookingAt(downhill, Vector3.Up), at);
		Vector3 stand = at + downhill * (-cart.HandleTip.Z + cart.HitchDistance);
		PlayerBody.Teleport(stand + Vector3.Up * 0.5f, Mathf.Atan2(-downhill.X, -downhill.Z));
		await Wait(0.3f);
		cart.Take(PlayerBody);
		float factor = PlayerBody.TowFactor(0.0f);
		Check("taking it slows the carrier by its weight", cart.Towing && Mathf.IsEqualApprox(GameEvents.TowMass, cart.Mass) && factor < 1.0f,
			$"tow mass {GameEvents.TowMass:0} kg (cart {cart.Mass:0}), speed factor {factor:0.00}");
		Vector3 cartStart = cart.GlobalPosition;
		PlayerBody.DirectionOverride = downhill;
		float worst = 0.0f;
		float elapsed = 0.0f;
		float dt = 1.0f / Engine.PhysicsTicksPerSecond;
		while (elapsed < WalkSeconds && cart.Towing)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			elapsed += dt;
			if (elapsed > 1.5f)
			{
				worst = Mathf.Max(worst, cart.HandlePoint.DistanceTo(cart.Hitch()));
			}
		}
		PlayerBody.DirectionOverride = null;
		float travelled = cart.GlobalPosition.DistanceTo(cartStart);
		Vector3 behind = cart.GlobalPosition - PlayerBody.GlobalPosition;
		behind.Y = 0.0f;
		Check("it trails the carrier downhill", cart.Towing && travelled > 5.0f && worst < TrailTolerance && behind.Normalized().Dot(downhill) < -0.5f,
			$"held {cart.Towing}, travelled {travelled:0.0} m, handles off the hitch up to {worst:0.00} m, behind {-behind.Normalized().Dot(downhill):0.00}");
		float upright = cart.GlobalBasis.Y.Dot(Vector3.Up);
		Check("it stays on its wheels", upright > 0.7f, $"upright {upright:0.00}");

		// Let go: it parks again.
		cart.LetGo();
		float parkedAfter = await Until(() => cart.Parked, 6.0f);
		Check("let go, it parks", cart.Parked && !cart.Towing && GameEvents.TowMass == 0.0f,
			$"after {parkedAfter:0.0} s, frozen {cart.Freeze}, tow mass {GameEvents.TowMass:0}");

		// Something set on the bed becomes cargo.
		ItemDefinition stone = new() { ItemId = "test_stone", DisplayName = "Stone", MassKg = 8.0f };
		WorldItem item = new() { Definition = stone, Name = "TestStone" };
		AddChild(item);
		item.GlobalPosition = cart.ToGlobal(cart.BedCentre);
		float before = cart.Mass;
		cart.GatherCargo();
		Check("things on the bed ride as cargo and weigh", item.GetParent() == cart.Cargo && Mathf.IsEqualApprox(cart.CargoMass, 8.0f) && Mathf.IsEqualApprox(cart.Mass, before + 8.0f),
			$"on the cart {item.GetParent() == cart.Cargo}, cargo {cart.CargoMass:0} kg, cart {before:0} -> {cart.Mass:0} kg");
		Finish();
	}

	private void Finish()
	{
		if (_failures.Count == 0)
		{
			GD.Print("HANDCART TESTS: PASS");
			GetTree().Quit(0);
		}
		else
		{
			GD.Print("HANDCART TESTS: FAIL (" + string.Join(", ", _failures) + ")");
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
