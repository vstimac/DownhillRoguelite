using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The Jump Over vault, a timing event. Walking toward a low boulder or fallen trunk (0.25 to 1.4 m
/// high, with room to land behind it), a cue appears (GameEvents.CurrentVault) and tightens as the
/// carrier closes in; Space while it's 2 to 3.2 m ahead vaults it. The vault is the Mixamo "Jump Over"
/// dive-and-roll: the animation starts at the moment that puts the top of the dive right over the
/// obstacle, its forward travel moves the body (HipsPin keeps the hips over it) gliding along the
/// ground past the obstacle, its height stays in the pose. Space too early or too late clips it: a
/// trip into the ragdoll and a knock to the legs. Without the character model, the carrier hops
/// over in a plain arc instead.
/// </summary>
public partial class Vault : Node3D
{
    private static readonly StringName ActionJump = "player_jump";

    [Export] public PlayerController? Body { get; set; }
    [Export] public PlayerRagdoll? Ragdoll { get; set; }
    [Export] public CarrierHealth? Health { get; set; }
    [Export] public CarrierModel? Look { get; set; }
    [Export(PropertyHint.File, "*.fbx,*.glb")] public string AnimationScene { get; set; } = "res://art/characters/mixamo/jump_over.fbx";
    [Export] public string AnimationName { get; set; } = "mixamo_com";
    [Export] public string HipsTrack { get; set; } = "Skeleton3D:mixamorig1_Hips";
    /// <summary>How far into the animation's travel the top of the dive comes, in m.</summary>
    [Export] public float PeakTravel { get; set; } = 4.1f;
    /// <summary>Distances to the obstacle where Space vaults it, and the sweet spot, in m.</summary>
    [Export] public Vector2 Window { get; set; } = new(2.0f, 3.2f);
    [Export] public float Sweet { get; set; } = 2.6f;
    /// <summary>The cue shows from this far away, in m.</summary>
    [Export] public float ShowFrom { get; set; } = 5.5f;
    /// <summary>Vaultable obstacles stand this high above the carrier's feet, in m.</summary>
    [Export] public Vector2 ObstacleHeight { get; set; } = new(0.25f, 1.4f);
    [Export] public float ProbeHeight { get; set; } = 0.45f;
    /// <summary>The probe follows the ground in steps this long, in m.</summary>
    [Export] public float ProbeStep { get; set; } = 0.8f;
    /// <summary>Above this height, the way over and the landing must be clear, in m.</summary>
    [Export] public float ClearHeight { get; set; } = 2.1f;
    [Export] public float MinSpeed { get; set; } = 0.8f;
    [Export] public float TripDamage { get; set; } = 10.0f;
    [Export] public float TripSpeed { get; set; } = 3.5f;
    /// <summary>Without the character model: the plain hop's length and how high it clears the top, in s and m.</summary>
    [Export] public float HopTime { get; set; } = 0.9f;
    [Export] public float HopClearance { get; set; } = 0.5f;
    [Export(PropertyHint.Layers3DPhysics)] public uint ObstacleMask { get; set; } = 1;

    public bool Vaulting { get; private set; }
    public int Vaults { get; private set; }
    public int Trips { get; private set; }

    private Vector3 _forward = Vector3.Forward;
    private float _speed;
    private LocomotionState _state;
    private StaticBody3D? _obstacle;
    private float _obstacleTop;
    private float _obstacleDistance;
    private AnimationPlayer? _player;
    private Animation? _animation;
    private int _hipsTrack = -1;
    private HipsPin? _pin;
    private float _lastTravel;
    private Vector3 _direction;
    private float _hopTime;
    private Vector3 _hopFrom;
    private Vector3 _hopTo;
    private float _hopHeight;

    public override void _Ready()
    {
        GameEvents.LocomotionUpdated += OnLocomotion;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
        GameEvents.PublishVaultCue(VaultCue.None);
        if (Vaulting)
        {
            GameEvents.PublishVaulting(false);
        }
    }

    private void OnLocomotion(LocomotionSnapshot snapshot)
    {
        Vector3 forward = snapshot.Forward;
        forward.Y = 0.0f;
        if (!forward.IsZeroApprox())
        {
            _forward = forward.Normalized();
        }
        _speed = new Vector2(snapshot.Velocity.X, snapshot.Velocity.Z).Length();
        _state = snapshot.State;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Body == null)
        {
            return;
        }
        if (Vaulting)
        {
            Step((float)delta);
            return;
        }
        VaultCue cue = LookForObstacle();
        GameEvents.PublishVaultCue(cue);
        if (cue.Active && Input.IsActionJustPressed(ActionJump))
        {
            Attempt(cue);
        }
    }

    /// <summary>A vaultable obstacle ahead on the carrier's way, or no cue.</summary>
    private VaultCue LookForObstacle()
    {
        if (Body == null || GameEvents.IsDowned || GameEvents.IsPacking || GameEvents.IsScreenOpen || _speed < MinSpeed
            || _state == LocomotionState.Airborne || _state == LocomotionState.Fallen)
        {
            _obstacle = null;
            return VaultCue.None;
        }
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        Godot.Collections.Array<Rid> self = new() { Body.GetRid() };
        Vector3 feet = Body.GlobalPosition;
        // Probe at knee height in short steps that hug the ground, so the slope itself never gets in the way.
        Godot.Collections.Dictionary hit = new();
        Vector3 from = feet + Vector3.Up * ProbeHeight;
        int steps = Mathf.Max(1, Mathf.CeilToInt(ShowFrom / ProbeStep));
        for (int i = 1; i <= steps && hit.Count == 0; i++)
        {
            Vector3 to = GroundBelow(space, feet + _forward * (i * ProbeStep), self) + Vector3.Up * ProbeHeight;
            Godot.Collections.Dictionary found = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, ObstacleMask, self));
            if (found.Count > 0 && found["collider"].AsGodotObject() is not Terrain.TerrainChunk)
            {
                hit = found;
            }
            from = to;
        }
        if (hit.Count == 0 || hit["collider"].AsGodotObject() is not StaticBody3D body || !body.IsInGroup(Terrain.TerrainChunk.VaultableGroup))
        {
            _obstacle = null;
            return VaultCue.None;
        }
        Vector3 point = (Vector3)hit["position"];
        // Its top: straight down onto it from above, a little past the face that was hit.
        Vector3 over = point + _forward * 0.25f;
        Godot.Collections.Dictionary top = space.IntersectRay(PhysicsRayQueryParameters3D.Create(over + Vector3.Up * 4.0f, over + Vector3.Down * 2.0f, ObstacleMask, self));
        float topY = point.Y;
        if (top.Count > 0 && top["collider"].AsGodotObject() == body)
        {
            topY = ((Vector3)top["position"]).Y;
        }
        // Its height above the ground it stands on (just before its face), not above the carrier's feet.
        Godot.Collections.Array<Rid> skip = new() { Body.GetRid(), body.GetRid() };
        Vector3 standing = GroundBelow(space, point - _forward * 0.15f, skip);
        float height = topY - standing.Y;
        // Room over it and beyond: nothing in the way at head height along the slope, for the length of the dive.
        Vector3 beyond = GroundBelow(space, point + _forward * 4.0f, skip);
        Godot.Collections.Dictionary blocked = space.IntersectRay(PhysicsRayQueryParameters3D.Create(
            feet + Vector3.Up * ClearHeight, beyond + Vector3.Up * ClearHeight, ObstacleMask, self));
        if (height < ObstacleHeight.X || height > ObstacleHeight.Y || blocked.Count > 0)
        {
            _obstacle = null;
            return VaultCue.None;
        }
        _obstacle = body;
        _obstacleTop = topY;
        _obstacleDistance = new Vector2(point.X - feet.X, point.Z - feet.Z).Length() + 0.3f;
        return new VaultCue(true, _obstacleDistance, Window.X, Window.Y, Sweet);
    }

    /// <summary>Space with a cue up: in the window it vaults, outside it the carrier trips.</summary>
    public void Attempt(VaultCue cue)
    {
        if (cue.InWindow)
        {
            Begin(cue.Distance);
        }
        else
        {
            Trip();
        }
    }

    /// <summary>Starts the vault over the obstacle that is this far ahead.</summary>
    public void Begin(float distance)
    {
        if (Body == null || Vaulting)
        {
            return;
        }
        _direction = _forward;
        Vaulting = true;
        Vaults++;
        GameEvents.PublishVaultCue(VaultCue.None);
        GameEvents.PublishVaulting(true);
        GameEvents.PublishVaulted(true);
        if (Prepare())
        {
            // Start where the dive's top lands right over the obstacle.
            double start = TimeAtTravel(PeakTravel - distance);
            _lastTravel = TravelAt(start);
            _player!.Play("vault/" + AnimationName);
            _player.Seek(start, true);
            if (Look?.Follower != null)
            {
                Look.Follower.Active = false;
            }
            _pin!.Active = true;
            return;
        }
        // No character model: a plain hop over it.
        _hopTime = 0.0f;
        _hopFrom = Body.GlobalPosition;
        _hopTo = _hopFrom + _direction * (distance + 2.2f);
        _hopHeight = Mathf.Max(0.6f, _obstacleTop - _hopFrom.Y + HopClearance);
    }

    /// <summary>Finds the character's animation player and gives it the vault (once).</summary>
    private bool Prepare()
    {
        if (_player != null && _animation != null && _pin != null)
        {
            return true;
        }
        if (Look?.Model == null || !ResourceLoader.Exists(AnimationScene))
        {
            return false;
        }
        // The vault is imported as an animation library (no second copy of the character); a scene works too.
        Animation? animation = null;
        Resource? source = ResourceLoader.Load(AnimationScene);
        if (source is AnimationLibrary sourceLibrary && sourceLibrary.HasAnimation(AnimationName))
        {
            animation = (Animation)sourceLibrary.GetAnimation(AnimationName).Duplicate();
        }
        else if (source is PackedScene scene)
        {
            Node instance = scene.Instantiate();
            foreach (Node node in instance.FindChildren("*", "AnimationPlayer", true, false))
            {
                if (node is AnimationPlayer sourcePlayer && sourcePlayer.HasAnimation(AnimationName))
                {
                    animation = (Animation)sourcePlayer.GetAnimation(AnimationName).Duplicate();
                }
                break;
            }
            instance.Free();
        }
        if (animation == null)
        {
            return false;
        }
        AnimationPlayer? player = null;
        foreach (Node node in Look.Model.FindChildren("*", "AnimationPlayer", true, false))
        {
            player = node as AnimationPlayer;
            break;
        }
        Skeleton3D? skeleton = null;
        foreach (Node node in Look.Model.FindChildren("*", "Skeleton3D", true, false))
        {
            skeleton = node as Skeleton3D;
            break;
        }
        if (player == null || skeleton == null)
        {
            return false;
        }
        AnimationLibrary library = new();
        library.AddAnimation(AnimationName, animation);
        player.AddAnimationLibrary("vault", library);
        _hipsTrack = animation.FindTrack(HipsTrack, Animation.TrackType.Position3D);
        _pin = new HipsPin { Name = "HipsPin", Active = false };
        skeleton.AddChild(_pin);
        _player = player;
        _animation = animation;
        return _hipsTrack >= 0;
    }

    /// <summary>How far the animation has travelled forward at a time, in m.</summary>
    private float TravelAt(double time)
    {
        if (_animation == null || _hipsTrack < 0)
        {
            return 0.0f;
        }
        return _animation.PositionTrackInterpolate(_hipsTrack, time).Z;
    }

    /// <summary>The time at which the animation has travelled this far (0 if not yet started).</summary>
    public double TimeAtTravel(float travel)
    {
        if (_animation == null)
        {
            return 0.0;
        }
        double step = 1.0 / 60.0;
        for (double t = 0.0; t <= _animation.Length; t += step)
        {
            if (TravelAt(t) >= travel)
            {
                return t;
            }
        }
        return 0.0;
    }

    private void Step(float delta)
    {
        if (Body == null)
        {
            return;
        }
        if (_player != null && _animation != null && _player.IsPlaying())
        {
            double now = _player.CurrentAnimationPosition;
            float travel = TravelAt(now);
            Vector3 next = Body.GlobalPosition + _direction * (travel - _lastTravel);
            _lastTravel = travel;
            Body.GlobalPosition = OnGround(next);
            if (now < _animation.Length - 0.02)
            {
                return;
            }
        }
        else if (_player == null || _animation == null)
        {
            _hopTime += delta;
            float t = Mathf.Clamp(_hopTime / HopTime, 0.0f, 1.0f);
            Vector3 along = _hopFrom.Lerp(_hopTo, t);
            Vector3 ground = OnGround(along);
            Body.GlobalPosition = ground + Vector3.Up * (4.0f * _hopHeight * t * (1.0f - t));
            if (t < 1.0f)
            {
                return;
            }
        }
        Finish();
    }

    /// <summary>The ground below a point (terrain and anything not skipped), or the point itself.</summary>
    private Vector3 GroundBelow(PhysicsDirectSpaceState3D space, Vector3 point, Godot.Collections.Array<Rid> skip)
    {
        Godot.Collections.Dictionary hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(point + Vector3.Up * 4.0f, point + Vector3.Down * 8.0f, ObstacleMask, skip));
        if (hit.Count > 0)
        {
            return (Vector3)hit["position"];
        }
        return point;
    }

    /// <summary>The ground under a point (ignoring the obstacle being vaulted), or the point itself.</summary>
    private Vector3 OnGround(Vector3 point)
    {
        if (Body == null)
        {
            return point;
        }
        Godot.Collections.Array<Rid> skip = new() { Body.GetRid() };
        if (_obstacle != null && IsInstanceValid(_obstacle))
        {
            skip.Add(_obstacle.GetRid());
        }
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(
            PhysicsRayQueryParameters3D.Create(point + Vector3.Up * 3.0f, point + Vector3.Down * 6.0f, ObstacleMask, skip));
        if (hit.Count > 0)
        {
            return (Vector3)hit["position"];
        }
        return point;
    }

    private void Finish()
    {
        if (_player != null)
        {
            _player.Stop();
        }
        if (_pin != null)
        {
            _pin.Active = false;
        }
        if (Look?.Follower != null)
        {
            Look.Follower.Active = true;
        }
        Vaulting = false;
        _obstacle = null;
        GameEvents.PublishVaulting(false);
    }

    /// <summary>A mistimed vault: the carrier clips the obstacle and goes down, knocking the legs.</summary>
    public void Trip()
    {
        Trips++;
        GameEvents.PublishVaultCue(VaultCue.None);
        GameEvents.PublishVaulted(false);
        Health?.TakeDamage(TripDamage, "tripped over a rock");
        Ragdoll?.GoDown(_forward * TripSpeed, _forward * TripSpeed * 0.6f + Vector3.Up * 1.5f);
    }
}
