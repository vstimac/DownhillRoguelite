using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Narrative;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug scene for the newspaper seller (narrative/NewspaperSeller): her on a flat field with a stand-in
/// carrier (the blue post) that the arrow keys / WASD walk about, a pack holding two water bottles, and
/// the enemy director for her ambush. E is E on her (begging, then giving), G puts another bottle in the
/// pack, H strikes her with the sword (two enemies come), R starts again. The overlay shows her state,
/// speed, clip and the prompt the tooltip would show.
/// Run: godot --path . res://debug/NewspaperSellerLab.tscn [-- --begging] (--begging: E is pressed
/// at once and the stand-in walks a circle, for a rendered check).
/// </summary>
public partial class NewspaperSellerLab : Node3D
{
    [Export] public NewspaperSeller? Seller { get; set; }
    [Export] public Camera3D? Camera { get; set; }
    [Export] public Node3D? Carrier { get; set; }
    [Export] public CarrierInventory? Inventory { get; set; }
    [Export] public Label? Overlay { get; set; }
    [Export] public float CarrierSpeed { get; set; } = 2.4f;
    /// <summary>The camera sits this far behind and above the middle of the two of them (m).</summary>
    [Export] public Vector3 CameraOffset { get; set; } = new(5.0f, 1.6f, 2.0f);
    /// <summary>The scripted circle the stand-in walks with --begging: radius (m) and speed (rad/s).</summary>
    [Export] public float CircleRadius { get; set; } = 5.0f;
    [Export] public float CircleSpeed { get; set; } = 0.35f;
    [Export] public string WaterPath { get; set; } = "res://inventory/items/water_bottle.tres";

    private bool _scripted;
    private float _angle;
    private Vector3 _sellerStart;

    public override void _Ready()
    {
        _scripted = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--begging") >= 0;
        if (Seller != null)
        {
            _sellerStart = Seller.GlobalPosition;
        }
        CallDeferred(MethodName.Stock);
        if (_scripted)
        {
            CallDeferred(MethodName.StartBegging);
        }
    }

    private void Stock()
    {
        AddBottle();
        AddBottle();
    }

    private void StartBegging() => Seller?.Interact(this);

    private void AddBottle()
    {
        if (Inventory != null && ResourceLoader.Load(WaterPath) is ItemDefinition water)
        {
            Inventory.TryAdd(water, -1.0f, false);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || Seller == null)
        {
            return;
        }
        if (key.Keycode == Key.E)
        {
            Seller.Interact(this);
        }
        else if (key.Keycode == Key.G)
        {
            AddBottle();
        }
        else if (key.Keycode == Key.H && Carrier != null)
        {
            Vector3 away = Seller.GlobalPosition - Carrier.GlobalPosition;
            away.Y = 0.0f;
            Seller.TakeHit(20.0f, away.Normalized() * 3.0f, "sword");
        }
        else if (key.Keycode == Key.R)
        {
            GameEvents.PublishRespawnRequest();
            Seller.GlobalPosition = _sellerStart;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Carrier == null)
        {
            return;
        }
        float dt = (float)delta;
        Vector3 move = Vector3.Zero;
        if (_scripted)
        {
            _angle += CircleSpeed * dt;
            Vector3 target = new(Mathf.Sin(_angle) * CircleRadius, 0.0f, 6.0f - Mathf.Cos(_angle) * CircleRadius);
            move = (target - Carrier.GlobalPosition) / Mathf.Max(dt, 0.001f);
            Carrier.GlobalPosition = target;
        }
        else
        {
            Vector2 input = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
            move = new Vector3(input.X, 0.0f, input.Y) * CarrierSpeed;
            Carrier.GlobalPosition += move * dt;
        }
        Vector3 forward = Vector3.Forward;
        if (move.LengthSquared() > 0.01f)
        {
            forward = move.Normalized();
        }
        GameEvents.PublishLocomotion(default(LocomotionSnapshot) with { Position = Carrier.GlobalPosition, Velocity = move, Forward = forward });
    }

    public override void _Process(double delta)
    {
        if (Seller == null || Carrier == null)
        {
            return;
        }
        if (Camera != null)
        {
            Vector3 middle = (Seller.GlobalPosition + Carrier.GlobalPosition) * 0.5f;
            Camera.GlobalPosition = Camera.GlobalPosition.Lerp(middle + CameraOffset, Mathf.Clamp((float)delta * 3.0f, 0.0f, 1.0f));
            Camera.LookAt(middle + Vector3.Up * 0.9f, Vector3.Up);
        }
        if (Overlay != null)
        {
            Overlay.Text = $"{Seller.Title}: {Seller.State}   speed {Seller.Speed:0.0} m/s   clip {Seller.Clip}\n"
                + $"Prompt: {Seller.Prompt}\n{Seller.Details}\n"
                + $"Pack: {string.Join(", ", GameEvents.CarriedItems)}\n"
                + "WASD/arrows walk the stand-in   E  her   G  +water   H  strike her   R  reset";
        }
    }
}
