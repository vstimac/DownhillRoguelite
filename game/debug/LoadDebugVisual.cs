using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Draws the current load's items as boxes on the carrier's back, swinging about the feet with the
/// balance tilt, so the simulated load is visible before the inventory has real models. Place it
/// as a child of the player. Reads LoadSnapshot and BalanceSnapshot events. Off in play now that the
/// pack has a model (the boxes showed through it, low on the carrier's back): `-- --load-boxes` shows them.
/// </summary>
public partial class LoadDebugVisual : Node3D
{
	/// <summary>Draw the boxes; also turned on by the `--load-boxes` command-line argument.</summary>
	[Export] public bool ShowBoxes { get; set; }
	[Export] public Color ItemColor { get; set; } = new(0.3f, 0.5f, 0.85f);
	/// <summary>Visual tilt as a multiple of the simulated tilt.</summary>
	[Export] public float LeanScale { get; set; } = 1.0f;

	private readonly List<MeshInstance3D> _boxes = new();
	private Vector3[] _positions = System.Array.Empty<Vector3>();
	private StandardMaterial3D? _material;
	private Vector2 _tilt;

	public override void _Ready()
	{
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--load-boxes") >= 0)
		{
			ShowBoxes = true;
		}
		if (!ShowBoxes)
		{
			Visible = false;
			SetProcess(false);
			return;
		}
		_material = new StandardMaterial3D { AlbedoColor = ItemColor, Roughness = 0.8f };
		GameEvents.LoadChanged += OnLoadChanged;
		GameEvents.BalanceUpdated += OnBalanceUpdated;
		OnLoadChanged(GameEvents.CurrentLoad);
	}

	public override void _ExitTree()
	{
		GameEvents.LoadChanged -= OnLoadChanged;
		GameEvents.BalanceUpdated -= OnBalanceUpdated;
		_material?.Dispose();
		_material = null;
	}

	private void OnLoadChanged(LoadSnapshot load)
	{
		foreach (MeshInstance3D box in _boxes)
		{
			box.QueueFree();
		}
		_boxes.Clear();

		_positions = load.ItemPositions;
		for (int i = 0; i < load.ItemPositions.Length; i++)
		{
			MeshInstance3D box = new()
			{
				Name = $"Item{i}",
				Mesh = new BoxMesh { Size = load.ItemSizes[i] },
				MaterialOverride = _material,
			};
			AddChild(box);
			_boxes.Add(box);
		}
	}

	private void OnBalanceUpdated(BalanceSnapshot snapshot)
	{
		_tilt = snapshot.Tilt * LeanScale;
	}

	public override void _Process(double delta)
	{
		// The pack is on the ground while packing; the boxes don't follow a ragdoll.
		Visible = !GameEvents.IsPacking && !GameEvents.IsDowned;

		// Same rotation as the balance model: roll about forward (-Z), pitch about -right.
		Quaternion lean = new Quaternion(Vector3.Forward, _tilt.X) * new Quaternion(Vector3.Right, -_tilt.Y);
		for (int i = 0; i < _boxes.Count && i < _positions.Length; i++)
		{
			// Carrier space has Z forward; the player's local forward is -Z.
			Vector3 local = new(_positions[i].X, _positions[i].Y, -_positions[i].Z);
			_boxes[i].Position = lean * local;
			_boxes[i].Quaternion = lean;
		}
	}
}
