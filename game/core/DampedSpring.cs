using System;

namespace DownhillRoguelite.Core;

/// <summary>Exact integration of x'' + 2*damping*omega*x' + omega^2*(x-target) = 0.</summary>
public static class DampedSpring
{
    public static void Step(ref float value, ref float velocity, float target, float frequency, float damping, float dt)
    {
        if (dt <= 0.0f)
        {
            return;
        }
        double omega = Math.Tau * Math.Max(0.0f, frequency);
        if (omega == 0.0)
        {
            value += velocity * dt;
            return;
        }
        double ratio = Math.Max(0.0f, damping);
        double offset = value - target;
        double speed = velocity;
        double position;
        double nextSpeed;
        if (ratio < 1.0)
        {
            double decayRate = ratio * omega;
            double damped = omega * Math.Sqrt(1.0 - ratio * ratio);
            double decay = Math.Exp(-decayRate * dt);
            double cosine = Math.Cos(damped * dt);
            double sineOverFrequency = Math.Sin(damped * dt) / damped;
            position = decay * (offset * cosine + (speed + decayRate * offset) * sineOverFrequency);
            nextSpeed = decay * (speed * cosine - (decayRate * speed + omega * omega * offset) * sineOverFrequency);
        }
        else if (ratio > 1.0)
        {
            double sum = ratio + Math.Sqrt(ratio * ratio - 1.0);
            double slow = -omega / sum;
            double fast = -omega * sum;
            double slowPart = (speed - fast * offset) / (slow - fast);
            double fastPart = offset - slowPart;
            double slowDecay = slowPart * Math.Exp(slow * dt);
            double fastDecay = fastPart * Math.Exp(fast * dt);
            position = slowDecay + fastDecay;
            nextSpeed = slow * slowDecay + fast * fastDecay;
        }
        else
        {
            double decay = Math.Exp(-omega * dt);
            double change = (speed + omega * offset) * dt;
            position = (offset + change) * decay;
            nextSpeed = (speed - omega * change) * decay;
        }
        value = target + (float)position;
        velocity = (float)nextSpeed;
    }
}
