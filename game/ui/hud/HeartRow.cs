using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// Health as a row of small inked hearts in the top-left corner: each heart holds HealthPerHeart,
/// the last one fills from the left by what is left of it, empty ones stay as dark outlines. The
/// shown health eases toward the real value; hearts just lost flash pale, a big hit gives the row a
/// jolt, and below the warning level the last heart beats. More health (boons, a sturdier carrier)
/// adds hearts, wrapping onto a second row. Presentation only: Hud feeds it from VitalsSnapshot.
/// </summary>
public partial class HeartRow : Control
{
    [Export] public float HealthPerHeart { get; set; } = 20.0f;
    [Export] public int PerRow { get; set; } = 8;
    [Export] public int Rows { get; set; } = 3;
    [Export] public float HeartSize { get; set; } = 26.0f;
    [Export] public float Gap { get; set; } = 5.0f;
    [Export] public float WarnBelow { get; set; } = 0.25f;
    /// <summary>How fast the shown health catches up with the real one (per second, exponential).</summary>
    [Export] public float EaseRate { get; set; } = 6.0f;
    [Export] public float BeatSpeed { get; set; } = 7.0f;
    [Export] public float BeatScale { get; set; } = 0.18f;
    [Export] public Color Empty { get; set; } = new(0.12f, 0.08f, 0.06f, 0.7f);

    public float Value { get; private set; }
    public float Max { get; private set; } = 100.0f;
    /// <summary>The health the hearts show right now (easing toward Value).</summary>
    public float Shown { get; private set; } = -1.0f;
    public bool Warning => Max > 0.0f && Value / Max < WarnBelow;
    /// <summary>How many hearts the current maximum makes (capped by PerRow × Rows).</summary>
    public int HeartCount => Mathf.Clamp(Mathf.CeilToInt(Max / Mathf.Max(HealthPerHeart, 0.001f) - 0.001f), 1, PerRow * Rows);
    /// <summary>Hearts that are at least partly filled right now.</summary>
    public int FilledHearts => Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(Shown, 0.0f) / Mathf.Max(HealthPerHeart, 0.001f) - 0.001f), 0, HeartCount);

    /// <summary>Rows the hearts take right now (the row's Size follows it, so the Hud can place things under it).</summary>
    public int RowsUsed => (HeartCount + Mathf.Max(PerRow, 1) - 1) / Mathf.Max(PerRow, 1);

    private float _time;
    private float _flash;
    private float _flashFrom;
    private float _jolt;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Resize();
    }

    private void Resize()
    {
        Vector2 size = new(PerRow * (HeartSize + Gap), RowsUsed * (HeartSize + Gap));
        if (!Size.IsEqualApprox(size))
        {
            CustomMinimumSize = size;
            Size = size;
        }
    }

    /// <summary>Feeds the real value; changes animate.</summary>
    public void SetValue(float value, float max)
    {
        float before = Value;
        Value = value;
        Max = Mathf.Max(max, 0.001f);
        Resize();
        if (Shown < 0.0f)
        {
            Shown = value;
            return;
        }
        float change = (value - before) / Max;
        if (change < -0.02f)
        {
            if (_flash <= 0.0f)
            {
                _flashFrom = before;
            }
            else
            {
                _flashFrom = Mathf.Max(_flashFrom, before);
            }
            _flash = 1.0f;
            _jolt = Mathf.Min(1.0f, -change * 6.0f);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        if (Shown >= 0.0f)
        {
            Shown = Mathf.Lerp(Shown, Value, 1.0f - Mathf.Exp(-EaseRate * dt));
        }
        _flash = Mathf.MoveToward(_flash, 0.0f, dt * 2.0f);
        _jolt = Mathf.MoveToward(_jolt, 0.0f, dt * 3.0f);
        QueueRedraw();
    }

    /// <summary>The heart's outline in units of half its size (y down): plump, with a shallow notch and a blunt tip.</summary>
    private static readonly Vector2[] Shape =
    {
        new(0.0f, -0.45f), new(0.22f, -0.78f), new(0.5f, -0.9f), new(0.8f, -0.8f), new(0.98f, -0.5f), new(0.95f, -0.12f), new(0.72f, 0.28f), new(0.38f, 0.65f),
        new(0.0f, 0.95f), new(-0.38f, 0.65f), new(-0.72f, 0.28f), new(-0.95f, -0.12f), new(-0.98f, -0.5f), new(-0.8f, -0.8f), new(-0.5f, -0.9f), new(-0.22f, -0.78f),
    };

    /// <summary>The heart outline around <paramref name="c"/>, <paramref name="size"/> px across.</summary>
    public static Vector2[] Outline(Vector2 c, float size)
    {
        Vector2[] points = new Vector2[Shape.Length];
        for (int i = 0; i < Shape.Length; i++)
        {
            points[i] = c + Shape[i] * (size * 0.5f);
        }
        return points;
    }

    public override void _Draw()
    {
        int count = HeartCount;
        float per = Mathf.Max(HealthPerHeart, 0.001f);
        float shake = Mathf.Sin(_time * 45.0f) * 3.0f * _jolt;
        int last = FilledHearts - 1;
        for (int i = 0; i < count; i++)
        {
            int row = i / Mathf.Max(PerRow, 1);
            int col = i % Mathf.Max(PerRow, 1);
            Vector2 c = new(col * (HeartSize + Gap) + HeartSize * 0.5f + 2.0f + shake, row * (HeartSize + Gap) + HeartSize * 0.5f + 2.0f);
            float size = HeartSize;
            if (Warning && i == last)
            {
                size *= 1.0f + BeatScale * Mathf.Max(0.0f, Mathf.Sin(_time * BeatSpeed));
            }
            Vector2[] heart = InkDraw.Wobble(Outline(c, size), 0.4f, 40 + i);
            float fill = Mathf.Clamp((Shown - i * per) / per, 0.0f, 1.0f);
            DrawColoredPolygon(heart, Empty);
            if (fill > 0.001f)
            {
                Color red = UiStyle.Health;
                if (Warning)
                {
                    red = red.Lerp(UiStyle.Warning, 0.5f + 0.5f * Mathf.Sin(_time * BeatSpeed));
                }
                if (fill >= 0.999f)
                {
                    DrawColoredPolygon(heart, red);
                }
                else
                {
                    // Partly left: fill from the left edge of the heart.
                    float left = c.X - size * 0.55f;
                    Rect2 box = new(new Vector2(left, c.Y - size), new Vector2(size * 1.1f * fill, size * 2.0f));
                    Vector2[] rect = { box.Position, new(box.End.X, box.Position.Y), box.End, new(box.Position.X, box.End.Y) };
                    foreach (Vector2[] part in Geometry2D.IntersectPolygons(heart, rect))
                    {
                        if (part.Length >= 3 && Geometry2D.TriangulatePolygon(part).Length > 0)
                        {
                            DrawColoredPolygon(part, red);
                        }
                    }
                }
                // A small paper glint, top left.
                DrawLine(c + new Vector2(-size * 0.32f, -size * 0.18f), c + new Vector2(-size * 0.18f, -size * 0.32f), new Color(1, 1, 1, 0.4f), 2.0f, true);
            }
            // Hearts just lost flash pale.
            float lostFrom = i * per;
            if (_flash > 0.0f && lostFrom < _flashFrom && lostFrom + per > Value)
            {
                DrawColoredPolygon(heart, new Color(UiStyle.Highlight, _flash * 0.7f));
            }
            Vector2[] closed = new Vector2[heart.Length + 1];
            heart.CopyTo(closed, 0);
            closed[^1] = heart[0];
            DrawPolyline(closed, UiStyle.Ink, 2.0f, true);
        }
    }
}
