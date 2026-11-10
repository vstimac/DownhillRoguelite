using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>A target with health for the wound tests: it tells blows (flinches) apart from bleeding.</summary>
public partial class BleedDummy : Node3D, IMeleeTarget
{
    [Export] public float Health { get; set; } = 100.0f;

    public int Blows { get; private set; }
    public float Bled { get; private set; }

    public Vector3 HitBase => GlobalPosition;
    public float HitHeight => 1.75f;
    public float HitRadius => 0.3f;
    public bool CanBeHit => Health > 0.0f;

    public void TakeHit(float damage, Vector3 push, string source)
    {
        Blows++;
        Health = Mathf.Max(0.0f, Health - damage);
    }

    public bool Bleed(float damage)
    {
        Bled += damage;
        Health = Mathf.Max(0.0f, Health - damage);
        return Health > 0.0f;
    }
}
