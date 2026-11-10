using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// An open wound on a body (a child of the IMeleeTarget node, added by BloodEffects): it drains
/// health each tick through IMeleeTarget.Bleed, drips blood on the ground along the way the body
/// goes, and closes up by itself. Each new wound adds to the flow. When the body goes down it leaves
/// one pool under it and the wound is done.
/// </summary>
public partial class Bleeding : Node
{
    public const string NodeName = "Bleeding";

    /// <summary>Health lost per second now.</summary>
    public float Rate { get; private set; }
    /// <summary>Health this wound has drained so far (tests).</summary>
    public float Drained { get; private set; }
    public int Drips { get; private set; }

    private BloodEffects? _effects;
    private IMeleeTarget? _target;
    private float _tick;
    private float _drip;
    private float _left;
    private float _fade;

    public void Setup(BloodEffects effects, IMeleeTarget target)
    {
        _effects = effects;
        _target = target;
    }

    /// <summary>Another wound: the flow grows (to the cap) and lasts again from now.</summary>
    public void Open(float rate)
    {
        if (_effects == null)
        {
            return;
        }
        Rate = Mathf.Min(Rate + rate, _effects.MaxBleedRate);
        _left = _effects.BleedSeconds;
        _fade = Rate / Mathf.Max(_effects.BleedSeconds, 0.1f);
    }

    public override void _Process(double delta)
    {
        if (_effects == null || _target == null || GetParent() is not Node3D body || !IsInstanceValid(body))
        {
            QueueFree();
            return;
        }
        float dt = (float)delta;
        _left -= dt;
        // A wound closes slowly: the flow eases off to nothing over its life.
        Rate = Mathf.Max(0.0f, Rate - _fade * dt);
        _tick += dt;
        if (_tick >= _effects.BleedTick)
        {
            float damage = Rate * _tick;
            _tick = 0.0f;
            Drained += damage;
            if (!_target.Bleed(damage))
            {
                _effects.Pool(body.GlobalPosition, _effects.DeathPoolSize);
                QueueFree();
                return;
            }
        }
        _drip -= dt;
        if (_drip <= 0.0f)
        {
            // Faster flow, more drops.
            _drip = Mathf.Max(_effects.DripSeconds * _effects.DripRate / Mathf.Max(Rate, 0.05f), _effects.MinDripSeconds);
            Drips++;
            _effects.Drip(body.GlobalPosition + Vector3.Up * 0.8f);
        }
        if (_left <= 0.0f || Rate <= 0.01f)
        {
            QueueFree();
        }
    }
}
