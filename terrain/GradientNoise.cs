namespace DownhillRoguelite.Terrain;

/// <summary>
/// 2D gradient (Perlin) noise in plain C#: deterministic for a seed and safe to read from any thread.
/// Output is roughly -1 to 1; it is 0 at integer lattice points.
/// </summary>
public sealed class GradientNoise
{
    private readonly int[] _permutation = new int[512];

    public GradientNoise(ulong seed)
    {
        int[] p = new int[256];
        for (int i = 0; i < 256; i++)
        {
            p[i] = i;
        }
        SplitMix64 random = new(seed);
        for (int i = 255; i > 0; i--)
        {
            int j = random.NextInt(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (int i = 0; i < 512; i++)
        {
            _permutation[i] = p[i & 255];
        }
    }

    public float Sample(float x, float y)
    {
        int xi = FastFloor(x);
        int yi = FastFloor(y);
        float xf = x - xi;
        float yf = y - yi;
        int px = xi & 255;
        int py = yi & 255;

        float u = Fade(xf);
        float v = Fade(yf);
        int aa = _permutation[_permutation[px] + py];
        int ab = _permutation[_permutation[px] + py + 1];
        int ba = _permutation[_permutation[px + 1] + py];
        int bb = _permutation[_permutation[px + 1] + py + 1];

        float bottom = Lerp(Gradient(aa, xf, yf), Gradient(ba, xf - 1.0f, yf), u);
        float top = Lerp(Gradient(ab, xf, yf - 1.0f), Gradient(bb, xf - 1.0f, yf - 1.0f), u);
        return Lerp(bottom, top, v);
    }

    /// <summary>Fractal sum of <paramref name="octaves"/> layers, each twice the frequency and half the strength; about -1 to 1.</summary>
    public float Fractal(float x, float y, int octaves)
    {
        float sum = 0.0f;
        float amplitude = 1.0f;
        float frequency = 1.0f;
        float total = 0.0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Sample(x * frequency, y * frequency) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
            frequency *= 2.0f;
        }
        if (total <= 0.0f)
        {
            return 0.0f;
        }
        return sum / total;
    }

    private static float Gradient(int hash, float x, float y)
    {
        switch (hash & 7)
        {
            case 0:
                return x + y;
            case 1:
                return -x + y;
            case 2:
                return x - y;
            case 3:
                return -x - y;
            case 4:
                return x;
            case 5:
                return -x;
            case 6:
                return y;
            default:
                return -y;
        }
    }

    private static int FastFloor(float value)
    {
        int truncated = (int)value;
        if (value < truncated)
        {
            return truncated - 1;
        }
        return truncated;
    }

    private static float Fade(float t)
    {
        return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }
}
