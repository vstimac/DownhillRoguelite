using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Progression;

/// <summary>
/// Save &amp; Continue. While a run started from the start screen is going, the run is saved every
/// SaveInterval, at each station, when the game is closed and on "Save and quit": the mountain's
/// seed, where the carrier stands, the hour, health, hunger and thirst, the run's numbers,
/// character and boons, the supplies already taken, and the pack and worn gear (RunSave and
/// EquipmentPersistence). Continue on the start screen rebuilds that mountain, waits for the ground
/// under the saved spot to load (holding the carrier there), puts them on it and restores the rest.
/// Death ends the run as before and deletes the save. Contracts, objectives, relics and the map's
/// marks start afresh on Continue.
/// </summary>
public partial class RunKeeper : Node
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public SupplySpawner? Supplies { get; set; }
    /// <summary>Seconds between saves while walking.</summary>
    [Export] public float SaveInterval { get; set; } = 60.0f;
    /// <summary>Continue gives up waiting for the ground after this long and puts the carrier at the saved spot as is (s).</summary>
    [Export] public float PlaceTimeout { get; set; } = 20.0f;
    /// <summary>Continue waits at least this long once the ground is there, so the terrain around has loaded too (s).</summary>
    [Export] public float PlaceSettle { get; set; } = 0.6f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;
    [Export] public string ContinuedHint { get; set; } = "Your descent continues.";

    /// <summary>A run is being kept (started from the start screen, not a test or a render).</summary>
    public bool Active { get; private set; }
    /// <summary>Continue is putting a saved run back.</summary>
    public bool Restoring => _restore != null;
    public int Saves { get; private set; }
    public string LastProblem { get; private set; } = "";

    private RunState? _run;
    private WorldClock? _clock;
    private RunSave? _restore;
    private float _placeTime;
    private float _groundTime;
    private float _sinceSave;
    private Vector3 _position;
    private float _yaw;
    private bool _hasPosition;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        GameEvents.RunBegun += OnRunBegun;
        GameEvents.CarrierDied += OnCarrierDied;
        GameEvents.StationReached += OnStationReached;
        GameEvents.SaveRequested += SaveNow;
        GameEvents.LocomotionUpdated += OnLocomotion;
    }

    public override void _ExitTree()
    {
        GameEvents.RunBegun -= OnRunBegun;
        GameEvents.CarrierDied -= OnCarrierDied;
        GameEvents.StationReached -= OnStationReached;
        GameEvents.SaveRequested -= SaveNow;
        GameEvents.LocomotionUpdated -= OnLocomotion;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            SaveNow();
        }
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        if (Restoring)
        {
            return;
        }
        _position = snapshot.Position;
        _hasPosition = true;
        if (!snapshot.Forward.IsZeroApprox())
        {
            _yaw = Mathf.Atan2(-snapshot.Forward.X, -snapshot.Forward.Z);
        }
    }

    private void OnRunBegun(bool continuing)
    {
        Active = true;
        _sinceSave = 0.0f;
        if (!continuing)
        {
            RunSave.Delete();
            return;
        }
        RunSave? save = RunSave.Read(out string problem);
        if (save == null || _run == null || PlayerBody == null)
        {
            LastProblem = problem;
            GD.PushWarning("RunKeeper: can't continue (" + problem + "); a new run starts instead.");
            return;
        }
        GD.Print($"RunKeeper: continuing run {save.RunNumber} on seed {save.Seed} at {save.Where}");
        _restore = save;
        _placeTime = 0.0f;
        _groundTime = 0.0f;
        _run.StartNewRun(save.Seed);
    }

    private void OnCarrierDied(string cause)
    {
        // Death is permanent: the run can't be continued.
        RunSave.Delete();
    }

    private void OnStationReached(int station)
    {
        SaveNow();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_restore != null)
        {
            Place(_restore, dt);
            return;
        }
        if (!Active)
        {
            return;
        }
        _sinceSave += dt;
        if (_sinceSave >= SaveInterval)
        {
            SaveNow();
        }
    }

    /// <summary>Holds the carrier above the saved spot until the ground there has loaded, then sets them on it and restores the run.</summary>
    private void Place(RunSave save, float dt)
    {
        if (PlayerBody == null)
        {
            _restore = null;
            return;
        }
        _placeTime += dt;
        Vector3 where = save.Where;
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(where + Vector3.Up * 60.0f, where + Vector3.Down * 200.0f, GroundMask,
            new Godot.Collections.Array<Rid> { PlayerBody.GetRid() });
        Godot.Collections.Dictionary hit = PlayerBody.GetWorld3D().DirectSpaceState.IntersectRay(query);
        bool ground = hit.Count > 0;
        if (ground)
        {
            _groundTime += dt;
        }
        if ((ground && _groundTime >= PlaceSettle) || _placeTime >= PlaceTimeout)
        {
            Vector3 at = where;
            if (ground)
            {
                at = (Vector3)hit["position"];
            }
            PlayerBody.Teleport(at + Vector3.Up * 0.1f, save.Yaw);
            Restore(save);
            _restore = null;
            return;
        }
        // Held in the air above the spot while the terrain streams in around it.
        PlayerBody.Teleport(where + Vector3.Up * 1.0f, save.Yaw);
    }

    private void Restore(RunSave save)
    {
        _run?.RestoreFrom(save);
        if (save.Character.Length > 0 && GameOptions.Character != save.Character)
        {
            GameOptions.SetCharacter(save.Character);
        }
        _clock?.Restore(save.Day, save.Hour);
        PlayerBody?.Health?.Restore(save.Health);
        PlayerBody?.GetNodeOrNull<CarrierNeeds>("Needs")?.SetNeeds(save.Hunger, save.Thirst);
        CarrierInventory? inventory = PlayerBody?.GetNodeOrNull<CarrierInventory>("Inventory");
        if (inventory != null && System.IO.File.Exists(ProjectSettings.GlobalizePath(RunSave.PackPath))
            && !EquipmentPersistence.Load(inventory, RunSave.PackPath, out string problem))
        {
            LastProblem = problem;
            GD.PushWarning("RunKeeper: the saved pack couldn't be loaded: " + problem);
        }
        Supplies?.MarkTaken(save.TakenSupplies);
        _position = save.Where;
        _yaw = save.Yaw;
        _hasPosition = true;
        _sinceSave = 0.0f;
        GameEvents.PublishHint(ContinuedHint);
        GD.Print("RunKeeper: run continued");
    }

    /// <summary>Saves the run now (no-op unless a run is being kept, alive, and not being put back).</summary>
    public void SaveNow()
    {
        if (!Active || Restoring || _run == null || _run.RunOver || PlayerBody == null || !_hasPosition || PlayerBody.Health?.Dead == true)
        {
            return;
        }
        _sinceSave = 0.0f;
        RunSave save = new()
        {
            GameVersion = (string)ProjectSettings.GetSetting("application/config/version", ""),
            SavedAt = System.DateTime.UtcNow.ToString("o"),
            Position = new[] { _position.X, _position.Y, _position.Z },
            Yaw = _yaw,
            Health = PlayerBody.Health?.Current ?? 100.0f,
        };
        _run.CaptureInto(save);
        if (_clock != null)
        {
            save.Day = _clock.Day;
            save.Hour = _clock.TimeOfDay;
        }
        CarrierNeeds? needs = PlayerBody.GetNodeOrNull<CarrierNeeds>("Needs");
        if (needs != null)
        {
            save.Hunger = needs.Hunger;
            save.Thirst = needs.Thirst;
        }
        if (Supplies != null)
        {
            save.TakenSupplies = new System.Collections.Generic.List<string>(Supplies.Taken);
        }
        // The pack first: the run file is only replaced once its pack is safely written.
        CarrierInventory? inventory = PlayerBody.GetNodeOrNull<CarrierInventory>("Inventory");
        if (inventory != null && !EquipmentPersistence.Save(inventory, RunSave.PackPath, out string packProblem))
        {
            LastProblem = packProblem;
            GD.PushWarning("RunKeeper: couldn't save the pack: " + packProblem);
            return;
        }
        if (!save.Write(out string problem))
        {
            LastProblem = problem;
            GD.PushWarning("RunKeeper: couldn't save the run: " + problem);
            return;
        }
        Saves++;
    }
}
