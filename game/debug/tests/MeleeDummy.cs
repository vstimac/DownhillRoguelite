using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>A standing target for the sword tests: it counts the blows and adds up the damage and shoves.</summary>
public partial class MeleeDummy : Node3D, IMeleeTarget
{
    [Export] public float Height { get; set; } = 1.6f;
    [Export] public float Radius { get; set; } = 0.35f;

    public int Blows { get; private set; }
    public float DamageTaken { get; private set; }
    public Vector3 LastPush { get; private set; }

    public Vector3 HitBase => GlobalPosition;
    public float HitHeight => Height;
    public float HitRadius => Radius;
    public bool CanBeHit => true;

    public override void _Ready()
    {
        AddToGroup(IMeleeTarget.Group);
    }

    public void TakeHit(float damage, Vector3 push, string source)
    {
        Blows++;
        DamageTaken += damage;
        LastPush = push;
    }
}
