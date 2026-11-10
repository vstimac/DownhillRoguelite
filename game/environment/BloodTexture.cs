using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// The splat a drop or a pool of blood leaves on the ground: a dark round stain with ragged edges and
/// a few satellite drops, drawn once in code (no art file) and shared by every decal.
/// </summary>
public static class BloodTexture
{
    private static ImageTexture? _splat;

    /// <summary>The shared splat (white, so the decal's modulate gives it its colour; alpha is the stain).</summary>
    public static ImageTexture Splat(int size = 128, int seed = 7)
    {
        if (_splat != null)
        {
            return _splat;
        }
        Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        RandomNumberGenerator random = new() { Seed = (ulong)seed };
        float centre = size * 0.5f;
        // The edge's raggedness: a few waves of different frequency around the stain.
        float[] wave = { random.RandfRange(0.0f, Mathf.Tau), random.RandfRange(0.0f, Mathf.Tau), random.RandfRange(0.0f, Mathf.Tau) };
        Vector2[] drops = new Vector2[9];
        float[] dropSize = new float[drops.Length];
        for (int i = 0; i < drops.Length; i++)
        {
            float angle = random.RandfRange(0.0f, Mathf.Tau);
            float distance = random.RandfRange(0.42f, 0.48f) * size;
            drops[i] = new Vector2(centre, centre) + Vector2.FromAngle(angle) * distance * random.RandfRange(0.75f, 1.0f);
            dropSize[i] = random.RandfRange(0.015f, 0.04f) * size;
        }
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new(x + 0.5f, y + 0.5f);
                Vector2 off = p - new Vector2(centre, centre);
                float angle = Mathf.Atan2(off.Y, off.X);
                float edge = size * (0.3f + 0.035f * Mathf.Sin(angle * 5.0f + wave[0]) + 0.025f * Mathf.Sin(angle * 9.0f + wave[1])
                    + 0.015f * Mathf.Sin(angle * 17.0f + wave[2]));
                float alpha = Mathf.Clamp((edge - off.Length()) / (size * 0.03f), 0.0f, 1.0f);
                for (int i = 0; i < drops.Length; i++)
                {
                    float drop = Mathf.Clamp((dropSize[i] - p.DistanceTo(drops[i])) / 1.5f, 0.0f, 1.0f);
                    alpha = Mathf.Max(alpha, drop);
                }
                // Darker and thicker in the middle, thinner towards the rim.
                float body = 0.75f + 0.25f * Mathf.Clamp(1.0f - off.Length() / Mathf.Max(edge, 1.0f), 0.0f, 1.0f);
                image.SetPixel(x, y, new Color(body, body, body, alpha * 0.92f));
            }
        }
        image.GenerateMipmaps();
        _splat = ImageTexture.CreateFromImage(image);
        return _splat;
    }
}
