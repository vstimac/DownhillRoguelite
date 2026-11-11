using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Network;

/// <summary>
/// Another player's carrier on this machine: a hidden mannequin plays the clip their animator plays
/// (by name, at their speed) and their chosen character follows it (CarrierModel in remote mode),
/// wearing what they wear. It moves smoothly between the states that arrive (a short delay, then
/// interpolation), shows their name above the head, and lies down when they die. It never
/// publishes GameEvents and never takes input: it only shows someone else.
/// </summary>
public partial class RemoteCarrier : CharacterBody3D
{
    [Export(PropertyHint.File, "*.glb")] public string DriverPath { get; set; } = "res://art/characters/quaternius_ual/UAL_Mannequin.glb";
    /// <summary>States are shown this many seconds late, so there is always one ahead to move toward.</summary>
    [Export] public float InterpolationDelay { get; set; } = 0.12f;
    /// <summary>A jump further than this between two states is a teleport (respawn): no sliding across.</summary>
    [Export] public float SnapDistance { get; set; } = 12.0f;
    /// <summary>The clip is pulled back in step when it drifts this far (seconds) from theirs.</summary>
    [Export] public float ClipDrift { get; set; } = 0.3f;
    [Export] public float ClipBlend { get; set; } = 0.2f;
    [Export] public string FallbackClip { get; set; } = "Idle";
    [Export] public string DeathClip { get; set; } = "Death01";
    [Export] public float NameHeight { get; set; } = 2.15f;

    public long PeerId { get; set; }
    public string PlayerName { get; set; } = "";
    public string Character { get; set; } = "";
    public Color Shirt { get; private set; } = Colors.White;
    public Color Pack { get; private set; } = Colors.White;
    public bool Dead { get; private set; }
    /// <summary>Their torch burns (shown in their hand, lighting the ground round them).</summary>
    public bool TorchLit => _torch?.Lit ?? false;
    public string Clip { get; private set; } = "";
    /// <summary>States received (tests).</summary>
    public int States { get; private set; }
    public CarrierModel? Look => _look;
    public Node3D? Driver => _driverModel;

    private readonly record struct Sample(double Time, Vector3 Position, float Yaw);

    private readonly List<Sample> _samples = new();
    private Node3D? _driverModel;
    private Skeleton3D? _driver;
    private AnimationPlayer? _animation;
    private CarrierModel? _look;
    private Label3D? _label;
    private CarrierTorch? _torch;
    private Transform3D _modelRest = Transform3D.Identity;
    private Transform3D _lookRest = Transform3D.Identity;
    private float _restYaw;
    private double _clock;

    /// <summary>
    /// Copies the local player's model and character placement (both turned half round in Player.tscn),
    /// so remote carriers stand as this one does. The whole body turns to face, as the local one does:
    /// the follower maps the driver's bones in the space they had when it started, so the two must turn together.
    /// </summary>
    public void UseModelPlacement(Transform3D modelLocal, Transform3D lookLocal)
    {
        _modelRest = modelLocal;
        _lookRest = lookLocal;
        _restYaw = modelLocal.Basis.GetEuler().Y;
    }

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 0;
        if (ResourceLoader.Load(DriverPath) is PackedScene scene && scene.Instantiate() is Node3D driver)
        {
            _driverModel = driver;
            _driverModel.Name = "Model";
            _driverModel.Transform = _modelRest;
            AddChild(_driverModel);
            foreach (Node node in _driverModel.FindChildren("*", "Skeleton3D", true, false))
            {
                _driver = node as Skeleton3D;
                break;
            }
            foreach (Node node in _driverModel.FindChildren("*", "AnimationPlayer", true, false))
            {
                _animation = node as AnimationPlayer;
                break;
            }
        }
        if (_driver != null)
        {
            // The driver is the same mannequin as the player's, so the torch finds its hand the same way (../Model/Rig/Skeleton3D).
            _torch = new CarrierTorch { Name = "Torch", Remote = true };
            AddChild(_torch);
        }
        _look = new CarrierModel { Name = "CarrierModel", Remote = true, RemoteCharacter = Character, Driver = _driver, DriverModel = _driverModel, Transform = _lookRest };
        AddChild(_look);
        _label = new Label3D
        {
            Name = "Name",
            Text = PlayerName,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 48,
            PixelSize = 0.004f,
            OutlineSize = 10,
            NoDepthTest = true,
            Position = Vector3.Up * NameHeight,
        };
        AddChild(_label);
        Play(FallbackClip, 1.0f);
    }

    /// <summary>A new state from their machine: where they stand, which way they face, what they do.</summary>
    public void Receive(Vector3 position, float yaw, string clip, float clipPosition, float clipSpeed, bool dead, int torch)
    {
        States++;
        // 1: burning in the hand, 2: put away at the belt, 0: none.
        _torch?.SetRemote(torch == 1 && !dead, torch == 2 && !dead);
        if (!position.IsFinite() || !Mathf.IsFinite(yaw))
        {
            return;
        }
        _clock = Time.GetTicksMsec() / 1000.0;
        if (_samples.Count > 0 && _samples[^1].Position.DistanceTo(position) > SnapDistance)
        {
            // A teleport (a respawn at a station): start over there rather than slide across the mountain.
            _samples.Clear();
            GlobalPosition = position;
        }
        if (_samples.Count == 0)
        {
            GlobalPosition = position;
        }
        _samples.Add(new Sample(_clock, position, yaw));
        while (_samples.Count > 20)
        {
            _samples.RemoveAt(0);
        }
        if (dead != Dead)
        {
            Dead = dead;
            if (dead)
            {
                Play(DeathClip, 1.0f);
            }
        }
        if (!Dead)
        {
            Play(clip, clipSpeed);
            if (_animation != null && _animation.IsPlaying() && clip == Clip)
            {
                double length = _animation.CurrentAnimationLength;
                double at = _animation.CurrentAnimationPosition;
                if (length > 0.0 && Mathf.Abs(at - clipPosition) > ClipDrift && clipPosition <= length)
                {
                    _animation.Seek(clipPosition, true);
                }
            }
        }
        if (_label != null)
        {
            string text = PlayerName;
            if (Dead)
            {
                text += " (fallen)";
            }
            _label.Text = text;
        }
    }

    /// <summary>They changed character, colours or clothes.</summary>
    public void ShowLook(string character, string[] worn, Color shirt, Color pack)
    {
        Character = character;
        Shirt = shirt;
        Pack = pack;
        _look?.ShowRemoteCharacter(character);
        _look?.ShowRemoteColours(shirt, pack);
        _look?.ShowRemoteGear(Gear(worn));
    }

    public override void _Process(double delta)
    {
        if (_samples.Count == 0)
        {
            return;
        }
        double now = Time.GetTicksMsec() / 1000.0 - InterpolationDelay;
        Sample from = _samples[0];
        Sample to = _samples[^1];
        for (int i = 0; i < _samples.Count - 1; i++)
        {
            if (_samples[i].Time <= now && _samples[i + 1].Time >= now)
            {
                from = _samples[i];
                to = _samples[i + 1];
                break;
            }
        }
        float t = 1.0f;
        double span = to.Time - from.Time;
        if (span > 0.0001)
        {
            t = Mathf.Clamp((float)((now - from.Time) / span), 0.0f, 1.0f);
        }
        Vector3 before = GlobalPosition;
        GlobalPosition = from.Position.Lerp(to.Position, t);
        float yaw = Mathf.LerpAngle(from.Yaw, to.Yaw, t);
        if (delta > 0.0)
        {
            Velocity = (GlobalPosition - before) / (float)delta;
        }
        // Their model faced `yaw`; the model sits turned _restYaw inside the body, as here.
        Rotation = new Vector3(0.0f, yaw - _restYaw, 0.0f);
    }

    private void Play(string clip, float speed)
    {
        if (_animation == null)
        {
            return;
        }
        string name = clip;
        if (name.Length == 0 || !_animation.HasAnimation(name))
        {
            name = FallbackClip;
        }
        if (!_animation.HasAnimation(name))
        {
            return;
        }
        _animation.SpeedScale = Mathf.Clamp(speed, 0.0f, 3.0f);
        if (Clip != clip || !_animation.IsPlaying())
        {
            _animation.Play(name, ClipBlend);
            Clip = clip;
        }
    }

    /// <summary>Worn gear from item ids (each in its own slot), as the clothing layers want it.</summary>
    public static GearSnapshot Gear(string[] worn)
    {
        WornPiece[] slots = new WornPiece[EquipmentSlots.Count];
        foreach (string id in worn)
        {
            ItemDefinition? item = ItemText.Find(id);
            if (item == null || item.Slot == EquipSlot.None)
            {
                continue;
            }
            int index = (int)item.Slot;
            if (index >= 0 && index < slots.Length)
            {
                slots[index] = new WornPiece(item.ItemId, item.DisplayName, item.MaxDurability, item.MaxDurability, item);
            }
        }
        return GearSnapshot.None with { Slots = slots };
    }
}
