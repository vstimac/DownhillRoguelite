using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// Alt+T brings Tuntun Sahur (TuntunSahur): he stands on the ground ten metres ahead of the carrier,
/// where the camera looks, and comes running. Pressing it again replaces him. The key is taken here
/// before the debug keys see it, so Alt+T doesn't also skip an hour (T).
/// </summary>
public partial class TuntunSpawner : Node3D
{
    [Export] public Key SpawnKey { get; set; } = Key.T;
    [Export] public bool RequireAlt { get; set; } = true;
    /// <summary>How far ahead of the carrier he appears, in m.</summary>
    [Export] public float SpawnDistance { get; set; } = 10.0f;
    /// <summary>The ground is looked for this far above and below the spot, in m.</summary>
    [Export] public float GroundSearch { get; set; } = 40.0f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    /// <summary>Hard mode (taken at a rest site): how many stand within HardmodeRadius of the carrier at the start.</summary>
    [ExportGroup("Hard mode")]
    [Export] public int HardmodeStart { get; set; } = 3;
    /// <summary>The count is kept within this distance of the carrier, in m.</summary>
    [Export] public float HardmodeRadius { get; set; } = 30.0f;
    /// <summary>New ones come in this band of distances from the carrier, in m.</summary>
    [Export] public Vector2 HardmodeSpawnBand { get; set; } = new(10.0f, 25.0f);
    /// <summary>Seconds between two arrivals when the count is short.</summary>
    [Export] public float HardmodeArrivalSeconds { get; set; } = 1.0f;

    public TuntunSahur? Current { get; private set; }
    /// <summary>The hard-mode Tuntun Sahurs this spawner brought (still in the world or on their way out).</summary>
    public System.Collections.Generic.IReadOnlyList<TuntunSahur> HardmodeTuntuns => _hardmode;

    private readonly System.Collections.Generic.List<TuntunSahur> _hardmode = new();
    private double _hardmodeNext;
    private RunState? _run;

    private LocomotionSnapshot _carrier;
    private bool _carrierKnown;

    public override void _Ready()
    {
        GameEvents.LocomotionUpdated += OnLocomotion;
        GameEvents.RespawnRequested += ClearHardmode;
        _run = GetNodeOrNull<RunState>("/root/RunState");
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
        GameEvents.RespawnRequested -= ClearHardmode;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_run == null || !_run.TuntunHardmode || !GameEvents.MaySimulateWorld || !_carrierKnown)
        {
            return;
        }
        _hardmode.RemoveAll(tuntun => !IsInstanceValid(tuntun) || tuntun.IsQueuedForDeletion());
        _hardmodeNext -= delta;
        if (_hardmodeNext > 0.0)
        {
            return;
        }
        int wanted = HardmodeStart + _run.TuntunHardmodeStations;
        int standing = 0;
        foreach (TuntunSahur tuntun in _hardmode)
        {
            if (tuntun.CanBeHit && HorizontalDistance(tuntun.GlobalPosition, _carrier.Position) <= HardmodeRadius)
            {
                standing++;
            }
        }
        if (standing >= wanted)
        {
            return;
        }
        _hardmodeNext = HardmodeArrivalSeconds;
        SpawnHardmode();
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        return new Vector2(a.X - b.X, a.Z - b.Z).Length();
    }

    /// <summary>Brings one more hard-mode Tuntun Sahur to a spot in the band around the carrier, facing them.</summary>
    private void SpawnHardmode()
    {
        float angle = (float)GD.RandRange(-Mathf.Pi, Mathf.Pi);
        float distance = (float)GD.RandRange(HardmodeSpawnBand.X, HardmodeSpawnBand.Y);
        Vector3 offset = new(Mathf.Sin(angle), 0.0f, Mathf.Cos(angle));
        Vector3 spot = Ground(_carrier.Position + offset * distance);
        TuntunSahur tuntun = new() { Name = "TuntunSahur", Persistent = true };
        AddChild(tuntun);
        tuntun.GlobalPosition = spot;
        // Facing the carrier from the start (as Alt+T does).
        tuntun.Rotation = new Vector3(0.0f, Mathf.Atan2(offset.X, offset.Z), 0.0f);
        tuntun.Track(_carrier.Position);
        _hardmode.Add(tuntun);
    }

    /// <summary>A new run (or a respawn) takes the hard-mode Tuntuns away.</summary>
    private void ClearHardmode()
    {
        foreach (TuntunSahur tuntun in _hardmode)
        {
            if (IsInstanceValid(tuntun)) { tuntun.QueueFree(); }
        }
        _hardmode.Clear();
        _hardmodeNext = 0.0;
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        _carrier = snapshot;
        _carrierKnown = true;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != SpawnKey)
        {
            return;
        }
        if (RequireAlt && !key.AltPressed)
        {
            return;
        }
        if (GetTree().Paused || (GameEvents.IsScreenOpen && !Ui.UiLayoutEditor.OnlyAltEditing) || GameEvents.IsConversing)
        {
            return;
        }
        GetViewport().SetInputAsHandled();
        Spawn();
    }

    /// <summary>Brings him (replacing the one already here); null if the carrier isn't known yet.</summary>
    public TuntunSahur? Spawn()
    {
        if (!GameEvents.MaySimulateWorld) { return null; }
        if (!_carrierKnown)
        {
            return null;
        }
        Vector3 ahead = Ahead();
        Vector3 spot = Ground(_carrier.Position + ahead * SpawnDistance);
        if (Current != null && IsInstanceValid(Current))
        {
            Current.QueueFree();
        }
        Current = new TuntunSahur { Name = "TuntunSahur" };
        AddChild(Current);
        Current.GlobalPosition = spot;
        // Facing the carrier from the start.
        Current.Rotation = new Vector3(0.0f, Mathf.Atan2(ahead.X, ahead.Z), 0.0f);
        Current.Track(_carrier.Position);
        return Current;
    }

    /// <summary>Where the camera looks, flattened; the carrier's facing without a camera.</summary>
    private Vector3 Ahead()
    {
        Vector3 ahead = _carrier.Forward;
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera != null)
        {
            ahead = -camera.GlobalBasis.Z;
        }
        ahead.Y = 0.0f;
        if (ahead.LengthSquared() < 0.0001f)
        {
            ahead = Vector3.Forward;
        }
        return ahead.Normalized();
    }

    private Vector3 Ground(Vector3 spot)
    {
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            spot + Vector3.Up * GroundSearch, spot + Vector3.Down * GroundSearch, GroundMask);
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
        {
            return hit["position"].AsVector3();
        }
        return spot;
    }
}
