using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The dive, an ability (Left Ctrl), and the landing roll (Space while falling).
///
/// Ctrl launches the Mixamo "Jump Over" dive from wherever the carrier stands: a short push-off, a
/// long arc forward (the animation's own travel moves the body; HipsPin keeps the hips over it), and
/// a roll out. If the ground is still under him at the end he rolls on; if he went off an edge (the
/// point: yeeting off a cliff) the dive hands him to normal falling with his momentum.
///
/// Falling fast enough to get hurt, a cue (GameEvents.CurrentVault: its distance is the height above
/// the ground) tightens as the ground comes up; Space in the window primes a roll
/// (GameEvents.RollPrimed): the controller then reports the landing as a soft one (no knockdown, no
/// damage) and the carrier rolls out with the dive's last part. A mistimed Space spends the chance.
/// Without the character model the dive is a plain arc and the roll has no animation.
/// </summary>
public partial class Dive : Node3D
{
    private static readonly StringName ActionDive = "player_dive";
    private static readonly StringName ActionJump = "player_jump";

    [Export] public PlayerController? Body { get; set; }
    [Export] public CarrierModel? Look { get; set; }
    [Export(PropertyHint.File, "*.fbx,*.glb")] public string AnimationScene { get; set; } = "res://art/characters/mixamo/jump_over.fbx";
    [Export] public string AnimationName { get; set; } = "mixamo_com";
    [Export] public string HipsTrack { get; set; } = "Skeleton3D:mixamorig1_Hips";
    [Export] public float StaminaCost { get; set; } = 15.0f;
    /// <summary>The animation's moments: where the dive starts, leaves the ground, lands, and where the roll begins (s).</summary>
    [Export] public float StartTime { get; set; } = 0.3f;
    [Export] public float TakeoffTime { get; set; } = 0.45f;
    [Export] public float LandTime { get; set; } = 1.4f;
    [Export] public float RollStart { get; set; } = 1.35f;
    /// <summary>Ground this far below at the landing moment means he went off an edge, in m.</summary>
    [Export] public float EdgeDrop { get; set; } = 0.7f;
    /// <summary>The ground ahead dropping away steeper than this counts as an edge, not a slope (degrees).</summary>
    [Export] public float MaxSlopeDegrees { get; set; } = 40.0f;
    /// <summary>Falling faster than this shows the roll cue, in m/s.</summary>
    [Export] public float RollCueSpeed { get; set; } = 5.0f;
    /// <summary>Heights above the ground where Space primes the roll, and the sweet spot, in m.</summary>
    [Export] public Vector2 RollWindow { get; set; } = new(0.15f, 1.8f);
    [Export] public float RollSweet { get; set; } = 0.7f;
    /// <summary>A rolled landing counts as this share of its real impact speed.</summary>
    [Export] public float RollSoftening { get; set; } = 0.15f;
    [Export(PropertyHint.Layers3DPhysics)] public uint GroundMask { get; set; } = 1;

    public bool Diving { get; private set; }
    public int Dives { get; private set; }
    public int Rolls { get; private set; }

    private Vector3 _forward = Vector3.Forward;
    private LocomotionState _state;
    private AnimationPlayer? _player;
    private Animation? _animation;
    private int _hipsTrack = -1;
    private HipsPin? _pin;
    private float _lastTravel;
    private Vector3 _direction;
    private float _takeoffY;
    private float _landY;
    private bool _flying;
    private bool _rollOnly;
    private double _time;
    private bool _spentChance;
    private float _gravity = 9.8f;

    public override void _Ready()
    {
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8f);
        GameEvents.LocomotionUpdated += OnLocomotion;
        GameEvents.Rolled += OnRolled;
    }

    public override void _ExitTree()
    {
        GameEvents.LocomotionUpdated -= OnLocomotion;
        GameEvents.Rolled -= OnRolled;
        GameEvents.PublishVaultCue(VaultCue.None);
        if (Diving)
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
        _state = snapshot.State;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Body == null)
        {
            return;
        }
        if (Diving)
        {
            Step((float)delta);
            return;
        }
        bool busy = GameEvents.IsDowned || GameEvents.IsPacking || GameEvents.IsScreenOpen || GameEvents.IsConversing || GameEvents.IsMapOpen;
        if (!busy && Input.IsActionJustPressed(ActionDive) && Body.IsOnFloor())
        {
            TryDive();
            return;
        }
        UpdateRollCue(busy);
    }

    /// <summary>Starts the dive if there's the stamina for it; false otherwise.</summary>
    public bool TryDive()
    {
        if (Body == null || Diving || (Body.Stamina != null && !Body.Stamina.TrySpend(StaminaCost)))
        {
            return false;
        }
        Dives++;
        _direction = _forward;
        _takeoffY = Body.GlobalPosition.Y;
        _flying = false;
        _rollOnly = false;
        _time = StartTime;
        Begin(StartTime);
        // Where the dive comes down: the ground ahead, unless that's a cliff (then the arc keeps its height).
        Vector3 landing = Body.GlobalPosition + _direction * (TravelAt(LandTime) - TravelAt(StartTime));
        float landGround = GroundBelow(landing).Y;
        float slopeAllowance = (TravelAt(LandTime) - TravelAt(StartTime)) * Mathf.Tan(Mathf.DegToRad(MaxSlopeDegrees));
        _landY = landGround;
        if (landGround < _takeoffY - slopeAllowance)
        {
            _landY = _takeoffY;
        }
        return true;
    }

    private void Begin(double from)
    {
        Diving = true;
        GameEvents.PublishVaultCue(VaultCue.None);
        GameEvents.PublishVaulting(true);
        if (Prepare())
        {
            _lastTravel = TravelAt(from);
            _player!.Play("dive/" + AnimationName);
            _player.Seek(from, true);
            if (Look?.Follower != null)
            {
                Look.Follower.Active = false;
            }
            _pin!.Active = true;
        }
        else
        {
            _lastTravel = PlainTravel(from);
        }
        _time = from;
    }

    /// <summary>Loads the dive animation onto the character (once); false without the model or the file.</summary>
    private bool Prepare()
    {
        // The character can be changed (start screen, Options): forget what was set up on the old one.
        if (_player != null && (Look?.Model == null || !IsInstanceValid(_player) || !Look.Model.IsAncestorOf(_player)))
        {
            _player = null;
            _animation = null;
            _pin = null;
        }
        if (_player != null && _animation != null && _pin != null)
        {
            return true;
        }
        if (Look?.Model == null || !ResourceLoader.Exists(AnimationScene))
        {
            return false;
        }
        Animation? animation = null;
        Resource? source = ResourceLoader.Load(AnimationScene);
        if (source is AnimationLibrary library && library.HasAnimation(AnimationName))
        {
            animation = (Animation)library.GetAnimation(AnimationName).Duplicate();
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
        AnimationLibrary own = new();
        own.AddAnimation(AnimationName, animation);
        player.AddAnimationLibrary("dive", own);
        _hipsTrack = animation.FindTrack(HipsTrack, Animation.TrackType.Position3D);
        _pin = new HipsPin { Name = "HipsPin", Active = false };
        skeleton.AddChild(_pin);
        _player = player;
        _animation = animation;
        return _hipsTrack >= 0;
    }

    private float TravelAt(double time)
    {
        if (_animation == null || _hipsTrack < 0)
        {
            return PlainTravel(time);
        }
        return _animation.PositionTrackInterpolate(_hipsTrack, time).Z;
    }

    /// <summary>Without the animation: the same distances, travelled evenly.</summary>
    private static float PlainTravel(double time)
    {
        return (float)time * 3.65f;
    }

    private double Length()
    {
        if (_animation != null)
        {
            return _animation.Length;
        }
        return 2.25;
    }

    private void Step(float delta)
    {
        if (Body == null)
        {
            return;
        }
        if (_player != null && _animation != null && _player.IsPlaying())
        {
            _time = _player.CurrentAnimationPosition;
        }
        else
        {
            _time += delta;
        }
        float travel = TravelAt(_time);
        Vector3 next = Body.GlobalPosition + _direction * (travel - _lastTravel);
        _lastTravel = travel;
        Vector3 ground = GroundBelow(next);
        if (!_rollOnly && _time >= TakeoffTime && _time < LandTime)
        {
            // In the air: a jump's arc, back to the take-off height at the landing moment.
            _flying = true;
            float flight = LandTime - TakeoffTime;
            float since = (float)(_time - TakeoffTime);
            float rise = 0.5f * _gravity * flight;
            float y = Mathf.Lerp(_takeoffY, _landY, since / flight) + rise * since - 0.5f * _gravity * since * since;
            next.Y = Mathf.Max(y, ground.Y);
            Body.GlobalPosition = next;
            return;
        }
        if (_flying && !_rollOnly)
        {
            _flying = false;
            if (ground.Y < _landY - EdgeDrop)
            {
                // Off the edge: from here he falls like anyone, keeping the dive's speed.
                Body.GlobalPosition = new Vector3(next.X, Mathf.Max(_landY, ground.Y), next.Z);
                float speed = (TravelAt(LandTime) - TravelAt(TakeoffTime)) / (LandTime - TakeoffTime);
                Finish();
                Body.Velocity = _direction * speed + Vector3.Down * (0.5f * _gravity * (LandTime - TakeoffTime));
                return;
            }
        }
        Body.GlobalPosition = ground;
        if (_time < Length() - 0.02)
        {
            return;
        }
        Finish();
    }

    private Vector3 GroundBelow(Vector3 point)
    {
        if (Body == null)
        {
            return point;
        }
        Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(
            PhysicsRayQueryParameters3D.Create(point + Vector3.Up * 2.0f, point + Vector3.Down * 60.0f, GroundMask, new Godot.Collections.Array<Rid> { Body.GetRid() }));
        if (hit.Count > 0)
        {
            return (Vector3)hit["position"];
        }
        return point + Vector3.Down * 60.0f;
    }

    private void Finish()
    {
        _player?.Stop();
        if (_pin != null)
        {
            _pin.Active = false;
        }
        if (Look?.Follower != null)
        {
            Look.Follower.Active = true;
        }
        Diving = false;
        GameEvents.PublishVaulting(false);
    }

    /// <summary>Falling fast enough to hurt: the cue for a roll, and Space to prime it.</summary>
    private void UpdateRollCue(bool busy)
    {
        if (Body == null || busy || Body.IsOnFloor() || Body.Velocity.Y > -RollCueSpeed || GameEvents.RollPrimed || _spentChance)
        {
            if (Body != null && Body.IsOnFloor())
            {
                _spentChance = false;
            }
            GameEvents.PublishVaultCue(VaultCue.None);
            return;
        }
        float height = Body.GlobalPosition.Y - GroundBelow(Body.GlobalPosition).Y;
        VaultCue cue = new(true, height, RollWindow.X, RollWindow.Y, RollSweet);
        GameEvents.PublishVaultCue(cue);
        if (Input.IsActionJustPressed(ActionJump))
        {
            TryRoll(cue);
        }
    }

    /// <summary>Space while falling: in the window it primes a roll; outside it the chance is gone.</summary>
    public void TryRoll(VaultCue cue)
    {
        GameEvents.PublishVaultCue(VaultCue.None);
        if (cue.InWindow)
        {
            GameEvents.PublishRollPrimed(true);
        }
        else
        {
            _spentChance = true;
            GameEvents.PublishHint("Too early: brace for a hard landing");
        }
    }

    /// <summary>A rolled landing: the dive's last part, a roll along the way he was going.</summary>
    private void OnRolled()
    {
        Rolls++;
        if (Body == null || Diving)
        {
            return;
        }
        Vector3 along = Body.Velocity;
        along.Y = 0.0f;
        _direction = _forward;
        if (along.Length() > 0.5f)
        {
            _direction = along.Normalized();
        }
        _rollOnly = true;
        _flying = false;
        _takeoffY = Body.GlobalPosition.Y;
        _landY = _takeoffY;
        Begin(RollStart);
    }
}
