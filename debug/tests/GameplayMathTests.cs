using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Analytical regression cases missed by the broad, scripted gameplay scenarios.</summary>
public partial class GameplayMathTests : Node3D
{
    private int _failures;

    public override void _Ready()
    {
        TestMass();
        TestGrid();
        TestStamina();
        TestTravel();
        TestLongFrames();
        TestSpringIntegration();
        TestSleepModifiers();
        GD.Print($"GAMEPLAY MATH TESTS: {_failures} failures");
        GetTree().Quit(Mathf.Min(_failures, 1));
    }

    private void TestMass()
    {
        BackpackDefinition pack = new() { EmptyMass = 0.0f };
        BackpackGrid grid = new(pack.GridSize);
        LoadSnapshot empty = LoadCalculator.Calculate(pack, grid);
        Check("massless empty pack has a finite centre of mass", empty.CenterOfMass.IsFinite() && empty.Mass == 0.0f);
        ItemDefinition cargo = new() { MassKg = 2.0f };
        grid.Place(cargo, Vector3I.Zero, GridOrientations.Identity);
        LoadSnapshot loaded = LoadCalculator.Calculate(pack, grid);
        Vector3 expected = LoadCalculator.GridToCarrier(pack, Vector3.One * pack.CellSize * 0.5f);
        Check("packed mass and centre of mass are conserved", Near(loaded.Mass, 2.0f) && loaded.CenterOfMass.IsEqualApprox(expected));
        pack.Dispose();
        cargo.Dispose();
    }

    private void TestGrid()
    {
        BackpackGrid grid = new(new Vector3I(4, 4, 4));
        Check("zero-volume boxes cannot occupy the pack", !grid.CanPlace(new Vector3I(0, 1, 1), Vector3I.One));
        Check("negative-volume boxes cannot occupy the pack", !grid.CanPlace(new Vector3I(-1, 1, 1), new Vector3I(2, 1, 1)));
    }

    private void TestStamina()
    {
        CarrierStamina stamina = new() { ProcessMode = ProcessModeEnum.Disabled };
        AddChild(stamina);
        stamina.MaxMultiplier = 1.5f;
        stamina.Refill();
        Check("refill includes stamina boons", Near(stamina.Current, 150.0f));
        stamina.MaxMultiplier = 0.6f;
        stamina.Refill();
        Check("refill respects hunger's reduced maximum", Near(stamina.Current, 60.0f));

        CarrierHealth health = new() { Stamina = stamina, ProcessMode = ProcessModeEnum.Disabled };
        AddChild(health);
        VitalsSnapshot vitals = default;
        void CaptureVitals(VitalsSnapshot snapshot) => vitals = snapshot;
        GameEvents.VitalsUpdated += CaptureVitals;
        health._PhysicsProcess(0.0);
        GameEvents.VitalsUpdated -= CaptureVitals;
        Check("vitals report the effective stamina maximum", Near(vitals.MaxStamina, 60.0f));

        stamina.MaxMultiplier = 1.0f;
        stamina.SetCurrent(0.0f);
        stamina._PhysicsProcess(stamina.RecoveryDelay - 0.05f);
        stamina._PhysicsProcess(0.1);
        Check("only the part of a frame after recovery delay restores stamina", Near(stamina.Current, stamina.EffectiveRecovery * 0.05f));
        stamina.MaxMultiplier = 0.1f;
        stamina.SetCurrent(0.0f);
        stamina._PhysicsProcess(3.0);
        Check("a low maximum cannot trap the carrier in exhaustion", Near(stamina.Current, 10.0f) && !stamina.Exhausted);
        stamina.QueueFree();
        health.QueueFree();
    }

    private void TestTravel()
    {
        PlayerController body = new() { ProcessMode = ProcessModeEnum.Disabled };
        CarrierStamina stamina = new() { ProcessMode = ProcessModeEnum.Disabled };
        AddChild(body);
        AddChild(stamina);
        RouteEstimator estimator = new() { Body = body, Stamina = stamina, ProcessMode = ProcessModeEnum.Disabled };
        AddChild(estimator);
        MathTestRoute slope = new() { RisePerMetre = -Mathf.Tan(Mathf.DegToRad(30.0f)) };
        RouteEstimate estimate = estimator.EstimateAlong(Vector3.Zero, slope, 0, 0);
        float length = slope.Station(1).Length();
        float expected = length / body.WalkingSpeed(0.5f);
        Check("30-degree route time uses distance along the ground", Near(estimate.WalkSeconds, expected, 0.01f));

        stamina.WalkDrain = 2.0f;
        stamina.SetCurrent(10.0f);
        MathTestRoute flat = new();
        estimate = estimator.EstimateAlong(Vector3.Zero, flat, 0, 0);
        float deficit = estimate.StaminaNeeded - stamina.Current;
        int rests = Mathf.CeilToInt(deficit / stamina.ExhaustedRecoverLevel);
        expected = rests * (stamina.ExhaustedRecoverLevel / stamina.EffectiveRecovery + stamina.RecoveryDelay);
        Check("every exhaustion rest restores the full recovery threshold", estimate.Rests == rests && Near(estimate.RestSeconds, expected, 0.01f));

        stamina.WalkDrain = 0.0f;
        stamina.SetCurrent(0.0f);
        estimate = estimator.EstimateAlong(Vector3.Zero, flat, 0, 0);
        Check("starting exhausted counts the initial rest", estimate.Rests == 1 && estimate.RestSeconds > 0.0f);

        stamina.SetCurrent(5.0f);
        MathTestRoute recoveryThenClimb = new() { Length = 200.0f, FlatApproach = 100.0f, RisePerMetre = 0.2f };
        estimate = estimator.EstimateAlong(Vector3.Zero, recoveryThenClimb, 0, 0);
        Check("walking on the flat restores stamina before a climb", estimate.Rests == 0 && Near(estimate.RestSeconds, 0.0f));

        stamina.MaxMultiplier = 0.1f;
        stamina.SetCurrent(0.0f);
        estimate = estimator.EstimateAlong(Vector3.Zero, flat, 0, 0);
        expected = stamina.RecoveryDelay + stamina.EffectiveMax / stamina.EffectiveRecovery;
        Check("route rests can reach a reduced stamina maximum", estimate.Valid && Near(estimate.RestSeconds, expected));
        estimator.SampleStep = 0.0f;
        Check("invalid sample step cannot hang route estimation", !estimator.EstimateAlong(Vector3.Zero, flat, 0, 0).Valid);
        estimator.SampleStep = 5.0f;
        stamina.RecoveryMultiplier = 0.0f;
        Check("an exhausted carrier with no recovery has no finite ETA", !estimator.EstimateAlong(Vector3.Zero, flat, 0, 0).Valid);
        estimator.QueueFree();
        body.QueueFree();
        stamina.QueueFree();
    }

    private void TestLongFrames()
    {
        PlayerController body = new() { ProcessMode = ProcessModeEnum.Disabled };
        Skeleton3D skeleton = new();
        BodyDynamics dynamics = new() { Body = body };
        AddChild(body);
        AddChild(skeleton);
        skeleton.AddChild(dynamics);
        dynamics._ProcessModificationWithDelta(0.1);
        body.Rotation = new Vector3(0.0f, 1.0f, 0.0f);
        dynamics._ProcessModificationWithDelta(0.1);
        for (int frame = 0; frame < 40; frame++)
        {
            dynamics._ProcessModificationWithDelta(0.1);
        }
        Check("body springs settle even at 10 FPS", float.IsFinite(dynamics.Twist) && Mathf.Abs(dynamics.Twist) < 0.01f);
        body.QueueFree();
        skeleton.QueueFree();
    }

    private void TestSleepModifiers()
    {
        RunState run = GetNode<RunState>("/root/RunState");
        run.Modifiers.NeedsDrain = 0.5f;
        CarrierNeeds needs = new() { ProcessMode = ProcessModeEnum.Disabled };
        AddChild(needs);
        GameEvents.PublishSlept(8.0f);
        float expected = CarrierNeeds.Full - needs.HungerPerHour * needs.SleepDrainFactor * run.DifficultySettings.NeedsDrainMultiplier * run.DepthNeedsFactor * run.Modifiers.NeedsDrain * 8.0f;
        Check("needs boons also reduce food spent sleeping", Near(needs.Hunger, expected));
        run.Modifiers.Reset();
        needs.QueueFree();
    }

    private void TestSpringIntegration()
    {
        foreach (float damping in new[] { 0.0f, 0.45f, 1.0f, 2.0f })
        {
            float single = 0.7f;
            float singleSpeed = -0.3f;
            float partitioned = single;
            float partitionedSpeed = singleSpeed;
            DampedSpring.Step(ref single, ref singleSpeed, 0.1f, 2.6f, damping, 0.8f);
            for (int frame = 0; frame < 80; frame++)
            {
                DampedSpring.Step(ref partitioned, ref partitionedSpeed, 0.1f, 2.6f, damping, 0.01f);
            }
            Check($"spring integration is independent of frame partition, damping {damping}",
                Near(single, partitioned, 0.0001f) && Near(singleSpeed, partitionedSpeed, 0.0001f));
        }
        float position = 0.7f;
        float velocity = -0.3f;
        float omega = Mathf.Tau * 2.6f;
        float energy = velocity * velocity + omega * omega * position * position;
        DampedSpring.Step(ref position, ref velocity, 0.0f, 2.6f, 0.0f, 10.0f);
        float after = velocity * velocity + omega * omega * position * position;
        Check("an undamped spring conserves energy through a ten-second frame", Near(after / energy, 1.0f));
    }

    private void Check(string name, bool passed)
    {
        if (passed)
        {
            GD.Print("PASS  " + name);
        }
        else
        {
            GD.Print("FAIL  " + name);
            _failures++;
        }
    }

    private static bool Near(float actual, float expected, float tolerance = 0.001f) => Mathf.Abs(actual - expected) <= tolerance;
}
