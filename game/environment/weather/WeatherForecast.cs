using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// Works out the weather for a seed, depth and time, with no state: time is cut into fronts of
/// FrontHours; each front's kind, strength and wind direction come from the seed, the circle and
/// the front's number, picked by the circle's WeatherProfile. The end of each front blends into
/// the next, the end of each circle blends into the next circle's weather, and night makes fog,
/// rain, snow and wind worse (and heat milder). The same seed always gives the same weather.
/// </summary>
public sealed class WeatherForecast
{
    private static readonly WeatherKind[] Kinds =
    {
        WeatherKind.Clear, WeatherKind.Fog, WeatherKind.Rain, WeatherKind.Snow, WeatherKind.Wind, WeatherKind.Heat,
    };

    public WeatherTable Table { get; }
    public ulong Seed { get; }

    public WeatherForecast(WeatherTable table, ulong seed)
    {
        Table = table;
        Seed = seed;
    }

    /// <summary>The front number at a time.</summary>
    public long FrontIndex(double totalHours)
    {
        return (long)System.Math.Floor(totalHours / Table.FrontHours);
    }

    /// <summary>A circle's front number <paramref name="index"/>.</summary>
    public WeatherFront FrontAt(int circle, long index)
    {
        ulong salt = SplitMix64.Mix(Seed ^ 0x57EA7E5ul, (ulong)(circle + 1));
        SplitMix64 random = new(SplitMix64.Mix(salt, (ulong)index));
        WeatherProfile profile = Table.ProfileFor(circle);

        float total = 0.0f;
        foreach (WeatherKind kind in Kinds)
        {
            total += Mathf.Max(0.0f, profile.Weight(kind));
        }
        float pick = random.NextFloat() * total;
        WeatherKind chosen = WeatherKind.Clear;
        foreach (WeatherKind kind in Kinds)
        {
            float weight = Mathf.Max(0.0f, profile.Weight(kind));
            if (weight <= 0.0f)
            {
                continue;
            }
            chosen = kind;
            if (pick < weight)
            {
                break;
            }
            pick -= weight;
        }
        float intensity = random.Range(profile.Intensity.X, profile.Intensity.Y);
        float angle = random.Range(0.0f, Mathf.Tau);
        return new WeatherFront(chosen, intensity, angle);
    }

    /// <summary>
    /// The weather at a depth and time, without gusts. <paramref name="darkness"/> is 0 by day and
    /// 1 at full night. A forced front (debug) replaces every front.
    /// </summary>
    public WeatherSnapshot Sample(float depth, double totalHours, float darkness, CircleTable circles, WeatherFront? forced = null)
    {
        int circle = circles.CircleAt(depth);
        WeatherSnapshot here = SampleCircle(circle, totalHours, darkness, forced);
        float toNext = (circle + 1) * circles.DepthPerCircle - depth;
        if (Table.CircleBlendDepth <= 0.0f || toNext >= Table.CircleBlendDepth)
        {
            return here;
        }
        float weight = Mathf.SmoothStep(0.0f, 1.0f, 1.0f - toNext / Table.CircleBlendDepth);
        return Blend(here, SampleCircle(circle + 1, totalHours, darkness, forced), weight);
    }

    /// <summary>One circle's weather at a time, without gusts.</summary>
    public WeatherSnapshot SampleCircle(int circle, double totalHours, float darkness, WeatherFront? forced = null)
    {
        WeatherProfile profile = Table.ProfileFor(circle);
        long index = FrontIndex(totalHours);
        WeatherFront front = forced ?? FrontAt(circle, index);
        WeatherSnapshot weather = FromFront(front, profile, darkness);

        float intoFront = (float)(totalHours - index * (double)Table.FrontHours);
        float blendStart = Table.FrontHours - Table.BlendHours;
        if (forced == null && Table.BlendHours > 0.0f && intoFront > blendStart)
        {
            float weight = Mathf.SmoothStep(0.0f, 1.0f, (intoFront - blendStart) / Table.BlendHours);
            WeatherSnapshot next = FromFront(FrontAt(circle, index + 1), profile, darkness);
            weather = Blend(weather, next, weight);
        }
        return weather;
    }

    /// <summary>What one front does under a profile, with night applied.</summary>
    public WeatherSnapshot FromFront(WeatherFront front, WeatherProfile profile, float darkness)
    {
        float fog = 0.0f;
        float rain = 0.0f;
        float snow = 0.0f;
        float heat = 0.0f;
        float wind = 0.0f;
        float strength = front.Intensity;
        if (front.Kind == WeatherKind.Fog)
        {
            fog = strength;
        }
        else if (front.Kind == WeatherKind.Rain)
        {
            rain = strength;
            fog = strength * Table.PrecipitationMist;
            wind = strength * Table.StormWind;
        }
        else if (front.Kind == WeatherKind.Snow)
        {
            snow = strength;
            fog = strength * Table.PrecipitationMist;
            wind = strength * Table.StormWind;
        }
        else if (front.Kind == WeatherKind.Wind)
        {
            wind = strength;
        }
        else if (front.Kind == WeatherKind.Heat)
        {
            heat = strength;
        }

        float night = 1.0f + profile.NightWorsening * Mathf.Clamp(darkness, 0.0f, 1.0f);
        fog = Mathf.Min(1.0f, fog * night);
        rain = Mathf.Min(1.0f, rain * night);
        snow = Mathf.Min(1.0f, snow * night);
        wind = Mathf.Min(1.0f, wind * night);
        heat *= Mathf.Lerp(1.0f, Table.NightHeat, Mathf.Clamp(darkness, 0.0f, 1.0f));

        float speed = profile.Breeze + profile.WindSpeed * wind;
        Vector3 direction = new(Mathf.Sin(front.WindAngle), 0.0f, Mathf.Cos(front.WindAngle));
        WeatherKind kind = front.Kind;
        if (strength < Table.ClearBelow)
        {
            kind = WeatherKind.Clear;
        }
        return new WeatherSnapshot(kind, fog, rain, snow, heat, direction * speed, GripFactor(rain, snow));
    }

    public float GripFactor(float rain, float snow)
    {
        return Mathf.Max(Table.MinGripFactor, 1.0f - Table.RainGripLoss * rain - Table.SnowGripLoss * snow);
    }

    /// <summary>Gusts on top of the wind at a time (real seconds): stronger wind and a swinging direction.</summary>
    public WeatherSnapshot WithGusts(WeatherSnapshot weather, float seconds)
    {
        float period = Mathf.Max(Table.GustPeriod, 0.1f);
        float phase = (Seed % 1000ul) * 0.01f;
        float wave = 0.6f * Mathf.Sin(seconds * Mathf.Tau / period + phase)
            + 0.4f * Mathf.Sin(seconds * Mathf.Tau / (period * 0.37f) + phase * 2.3f);
        float gust = Mathf.Max(0.0f, wave);
        gust *= gust;
        float swing = Mathf.DegToRad(Table.GustSwing) * Mathf.Sin(seconds * Mathf.Tau / (period * 1.7f) + phase * 0.7f);
        Vector3 wind = weather.Wind.Rotated(Vector3.Up, swing) * (1.0f + Table.GustStrength * gust);
        return weather with { Wind = wind };
    }

    /// <summary>Linear mix of two weathers; the dominant kind switches halfway.</summary>
    public static WeatherSnapshot Blend(WeatherSnapshot from, WeatherSnapshot to, float weight)
    {
        WeatherKind kind = from.Kind;
        if (weight >= 0.5f)
        {
            kind = to.Kind;
        }
        return new WeatherSnapshot(
            kind,
            Mathf.Lerp(from.Fog, to.Fog, weight),
            Mathf.Lerp(from.Rain, to.Rain, weight),
            Mathf.Lerp(from.Snow, to.Snow, weight),
            Mathf.Lerp(from.Heat, to.Heat, weight),
            from.Wind.Lerp(to.Wind, weight),
            Mathf.Lerp(from.GripFactor, to.GripFactor, weight));
    }
}
