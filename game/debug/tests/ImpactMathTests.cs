using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Independent normal/tangential collision cases with known incoming speeds.</summary>
public partial class ImpactMathTests : Node
{
    private int _failures;

    public override void _Ready()
    {
        Check("no velocity change is no impact", Vector3.Down * 6.0f, Vector3.Down * 6.0f, Vector3.Up, 0.0f);
        Check("a head-on stop loses its incoming speed", Vector3.Down * 10.0f, Vector3.Zero, Vector3.Up, 10.0f);
        Check("tangential braking is not a blow into the ground", Vector3.Right * 12.0f, Vector3.Zero, Vector3.Up, 0.0f);
        Check("a glancing stop counts only its two-metre-per-second approach", new Vector3(12.0f, -2.0f, 0.0f), Vector3.Zero, Vector3.Up, 2.0f);
        Check("a bounce does not charge the outward speed as another impact", Vector3.Down * 10.0f, Vector3.Up * 6.0f, Vector3.Up, 10.0f);
        Check("accelerating toward a surface is not a collision", Vector3.Down * 2.0f, Vector3.Down * 8.0f, Vector3.Up, 0.0f);
        Check("partial slowing counts only lost approach speed", Vector3.Down * 8.0f, Vector3.Down * 3.0f, Vector3.Up, 5.0f);
        Check("changing sliding speed preserves the incoming normal speed", new Vector3(8.0f, -6.0f, 0.0f), Vector3.Down * 6.0f, Vector3.Up, 0.0f);
        Check("a wall impact uses the wall normal", Vector3.Right * 10.0f, Vector3.Zero, Vector3.Left, 10.0f);
        Vector3 normal = new Vector3(1.0f, 1.0f, 0.0f).Normalized();
        Vector3 tangent = new Vector3(1.0f, -1.0f, 0.0f).Normalized() * 12.0f;
        Check("rotating the contact plane preserves normal impact speed", tangent - normal * 10.0f, tangent, normal, 10.0f);
        Check("normal magnitude does not scale damage", Vector3.Down * 10.0f, Vector3.Zero, Vector3.Up * 7.0f, 10.0f);
        Check("without a contact normal there is no measured collision", Vector3.Down * 10.0f, Vector3.Zero, Vector3.Zero, 0.0f);
        GD.Print($"IMPACT MATH TESTS: {_failures} failures");
        GetTree().Quit(Mathf.Min(_failures, 1));
    }

    private void Check(string name, Vector3 before, Vector3 after, Vector3 normal, float expected)
    {
        float actual = ImpactMath.SpeedLost(before, after, normal);
        if (Mathf.Abs(actual - expected) <= 0.0001f)
        {
            GD.Print($"PASS  {name}: {actual:0.00} m/s");
        }
        else
        {
            GD.Print($"FAIL  {name}: {actual:0.00} m/s, expected {expected:0.00}");
            _failures++;
        }
    }
}
