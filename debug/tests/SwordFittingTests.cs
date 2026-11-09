using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The sword fitting scene by its keys: it sets up with the sword out; the arrows shift and Shift
/// turns the sword in the hand; Tab moves to the hip and the shield; poses and scrubbing work; Ctrl+S
/// saves a fit that loads back with the same values.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/SwordFittingTests.tscn
/// </summary>
public partial class SwordFittingTests : Node
{
	[Export] public string SavePath { get; set; } = "user://test_sword_fit.tres";

	private readonly List<string> _failures = new();

	public override async void _Ready()
	{
		PackedScene scene = GD.Load<PackedScene>("res://debug/SwordFitting.tscn");
		SwordFitting fitting = scene.Instantiate<SwordFitting>();
		fitting.SavePath = SavePath;
		AddChild(fitting);
		await Frames(30);
		MeleeCombat? combat = fitting.PlayerBody?.GetNodeOrNull<MeleeCombat>("MeleeCombat");
		Check("the scene sets up with the sword in the hand", fitting.IsSetUp && combat != null && combat.SwordInHand && combat.Sword != null,
			$"set up {fitting.IsSetUp}, in hand {combat?.SwordInHand}");

		await Press(Key.Up);
		await Press(Key.Up);
		await Press(Key.Right, shift: true);
		Vector3 handPos = fitting.CurrentFit.HandPosition;
		Vector3 handRot = fitting.CurrentFit.HandRotation;
		Check("arrows shift and Shift turns the sword in the hand", Mathf.IsEqualApprox(handPos.Y, 0.02f) && Mathf.IsEqualApprox(handRot.Y, 5.0f),
			$"shift {handPos}, turn {handRot}");

		foreach (Key pose in new[] { Key.Key2, Key.Key3, Key.Key6, Key.Key7, Key.Key1 })
		{
			await Press(pose);
			await Press(Key.Period);
			await Press(Key.Space);
			await Frames(5);
			await Press(Key.Space);
		}
		await Press(Key.Tab);
		bool onHip = combat != null && !combat.SwordInHand;
		await Press(Key.Pagedown);
		await Press(Key.Tab);
		bool shieldShown = combat?.Shield != null && combat.Shield.Visible;
		await Press(Key.Left, shift: true);
		await Press(Key.Equal);
		Check("Tab moves to the hip, then to the shield, each fitted on its own", onHip && shieldShown && combat!.SwordInHand
			&& Mathf.IsEqualApprox(fitting.CurrentFit.HipPosition.Z, -0.01f) && Mathf.IsEqualApprox(fitting.CurrentFit.ShieldRotation.Y, -5.0f)
			&& fitting.CurrentFit.ShieldScale > 1.0f,
			$"sword on hip {onHip}, shield shown {shieldShown}, hip {fitting.CurrentFit.HipPosition}, shield turn {fitting.CurrentFit.ShieldRotation}, size {fitting.CurrentFit.ShieldScale}");

		await Press(Key.S, ctrl: true);
		SwordFit? saved = ResourceLoader.Load<SwordFit>(SavePath, null, ResourceLoader.CacheMode.Ignore);
		Check("Ctrl+S saves a fit that loads back the same", saved != null && saved.HandPosition.IsEqualApprox(handPos) && saved.HandRotation.IsEqualApprox(handRot)
			&& Mathf.IsEqualApprox(saved.ShieldScale, fitting.CurrentFit.ShieldScale),
			$"message '{fitting.Message}', loaded {saved != null}, hand {saved?.HandPosition}");
		DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));

		if (_failures.Count == 0)
		{
			GD.Print("SWORD FITTING TESTS: PASS");
			GetTree().Quit(0);
		}
		else
		{
			GD.Print("SWORD FITTING TESTS: FAIL (" + string.Join(", ", _failures) + ")");
			GetTree().Quit(1);
		}
	}

	private async Task Press(Key key, bool shift = false, bool ctrl = false)
	{
		Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true, ShiftPressed = shift, CtrlPressed = ctrl });
		await Frames(2);
		Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false, ShiftPressed = shift, CtrlPressed = ctrl });
		await Frames(2);
	}

	private async Task Frames(int count)
	{
		for (int i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
