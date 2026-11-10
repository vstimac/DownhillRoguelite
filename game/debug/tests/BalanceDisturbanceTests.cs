using DownhillRoguelite.Balance;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Checks push attribution independently of ragdolls, using a sensitive stumble window.</summary>
public partial class BalanceDisturbanceTests : Node
{
    private int _failures;

    public override void _Ready()
    {
        BalanceController baseline = CreateBalance(0.2f);
        Check("a standing carrier catches a pack-only stumble", !ObserveFall(baseline));
        baseline.Free();

        BalanceController zero = CreateBalance(0.2f);
        zero.ApplyImpulse(Vector2.Zero);
        Check("a zero impulse cannot disable automatic steadying", !ObserveFall(zero));
        zero.Free();

        BalanceController reset = CreateBalance(0.2f);
        reset.ApplyImpulse(new Vector2(0.1f, 0.0f));
        reset.ResetBalance();
        Check("reset clears the memory of a cancelled push", !ObserveFall(reset));
        reset.Free();

        BalanceController longMemory = CreateBalance(0.2f);
        longMemory.PushMemory = 200.0f;
        Check("a fresh carrier has no phantom push even with long memory", !ObserveFall(longMemory));
        longMemory.Free();

        BalanceController direct = CreateBalance(0.0f);
        BalanceSettings settings = direct.Settings;
        float inertia = settings.BodyMass * settings.BodyComHeight * settings.BodyComHeight * settings.BodyInertiaFactor + 20.0f * 1.2f * 1.2f;
        float impulse = settings.LandingJolt * 5.0f * 20.0f * 1.2f / inertia;
        direct.ApplyImpulse(new Vector2(0.0f, impulse));
        Check("a real knock can defeat standing steadying", ObserveFall(direct));
        direct.Free();

        BalanceController landing = CreateBalance(0.0f);
        GameEvents.PublishLanded(5.0f);
        Check("the same impulse from a landing is remembered as a push", ObserveFall(landing));
        landing.Free();

        BalanceController wind = CreateBalance(0.0f);
        GameEvents.PublishWeather(WeatherSnapshot.Calm with { Kind = WeatherKind.Wind, Wind = Vector3.Right * 12.0f });
        Check("wind can topple a standing carrier", ObserveFall(wind));
        wind.Free();

        BalanceController gust = CreateBalance(0.2f);
        GameEvents.PublishWeather(WeatherSnapshot.Calm with { Kind = WeatherKind.Wind, Wind = Vector3.Right * 12.0f });
        gust._PhysicsProcess(1.0 / 60.0);
        GameEvents.PublishWeather(WeatherSnapshot.Calm);
        Check("a brief gust remains remembered after the wind stops", ObserveFall(gust));
        gust.Free();

        GameEvents.PublishWeather(WeatherSnapshot.Calm);
        GameEvents.PublishLoad(LoadSnapshot.Empty);
        GD.Print($"BALANCE DISTURBANCE TESTS: {_failures} failures");
        GetTree().Quit(Mathf.Min(_failures, 1));
    }

    private BalanceController CreateBalance(float sidewaysOffset)
    {
        GameEvents.PublishWeather(WeatherSnapshot.Calm);
        GameEvents.PublishLoad(new LoadSnapshot("Controlled load", 20.0f, new Vector3(sidewaysOffset, 1.2f, 0.0f),
            20.0f * (1.2f * 1.2f + sidewaysOffset * sidewaysOffset), 20.0f * 1.2f * 1.2f,
            System.Array.Empty<float>(), System.Array.Empty<Vector3>(), System.Array.Empty<Vector3>()));
        BalanceController balance = new()
        {
            ProcessMode = ProcessModeEnum.Disabled,
            InputEnabled = false,
            Settings = new BalanceSettings
            {
                WobbleThreshold = 0.025f,
                StumbleThreshold = 0.05f,
                RecoverThreshold = 0.02f,
                FallThreshold = 10.0f,
                StumbleWindow = 0.1f,
                FallDuration = 100.0f,
            },
        };
        AddChild(balance);
        GameEvents.PublishLocomotion(new LocomotionSnapshot(Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Up,
            0.0f, Vector3.Zero, Vector3.Zero, LocomotionState.Idle, "Rock", 1.0f, Vector3.Forward));
        return balance;
    }

    private static bool ObserveFall(BalanceController balance)
    {
        for (int frame = 0; frame < 120; frame++)
        {
            balance._PhysicsProcess(1.0 / 60.0);
            if (balance.State == BalanceState.Fallen)
            {
                return true;
            }
        }
        return false;
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
}
