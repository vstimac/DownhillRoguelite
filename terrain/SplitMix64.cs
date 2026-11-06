namespace DownhillRoguelite.Terrain;

/// <summary>
/// Small deterministic random generator (SplitMix64). Same seed, same sequence on every machine and
/// .NET version, and safe to use on a background thread (each user owns its instance).
/// </summary>
public sealed class SplitMix64
{
    private ulong _state;

    public SplitMix64(ulong seed)
    {
        _state = seed;
    }

    public ulong Next()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>0 (inclusive) to <paramref name="max"/> (exclusive).</summary>
    public int NextInt(int max)
    {
        return (int)(Next() % (ulong)max);
    }

    /// <summary>0 (inclusive) to 1 (exclusive).</summary>
    public float NextFloat()
    {
        return (Next() >> 40) * (1.0f / (1 << 24));
    }

    public float Range(float min, float max)
    {
        return min + (max - min) * NextFloat();
    }

    /// <summary>Combines a seed with a salt into a new, well-mixed seed.</summary>
    public static ulong Mix(ulong seed, ulong salt)
    {
        return new SplitMix64(seed ^ (salt * 0xD6E8FEB86659FD93UL)).Next();
    }
}
