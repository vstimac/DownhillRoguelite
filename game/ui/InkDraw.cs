using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>The hand-drawn icons the UI draws itself (no image files).</summary>
public enum InkIcon
{
    Heart,
    Stomach,
    Drop,
    Boot,
    Snowflake,
    Backpack,
    Hat,
    Shirt,
    Trousers,
    Glove,
    Cape,
    Shoe,
    Figure,
}

/// <summary>
/// Hand-inked drawing for the journal UI: lines and shapes whose outlines wobble a little (seeded,
/// so they don't shimmer), filled in muted paper-friendly colours with a dark ink outline, and a
/// set of icons for the survival dials and equipment slots built from those strokes.
/// </summary>
public static class InkDraw
{
    /// <summary>Points around a closed outline, nudged by a seeded wobble so it looks drawn by hand.</summary>
    public static Vector2[] Wobble(IReadOnlyList<Vector2> points, float amount, int seed)
    {
        Vector2[] result = new Vector2[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            float a = Mathf.Sin(i * 12.9898f + seed * 78.233f) * 43758.5453f;
            float b = Mathf.Sin(i * 39.346f + seed * 11.135f) * 24634.6345f;
            result[i] = points[i] + new Vector2(a - Mathf.Floor(a) - 0.5f, b - Mathf.Floor(b) - 0.5f) * 2.0f * amount;
        }
        return result;
    }

    /// <summary>A filled shape with an ink outline.</summary>
    public static void Shape(CanvasItem canvas, IReadOnlyList<Vector2> outline, Color fill, float width = 2.5f, int seed = 1)
    {
        Vector2[] points = Wobble(outline, width * 0.35f, seed);
        if (fill.A > 0.0f)
        {
            // A wobbled outline can cross itself; then fill its hull instead (the ink line stays as drawn).
            if (Geometry2D.TriangulatePolygon(points).Length > 0)
            {
                canvas.DrawColoredPolygon(points, fill);
            }
            else
            {
                Vector2[] hull = Geometry2D.ConvexHull(points);
                if (hull.Length > 3)
                {
                    canvas.DrawColoredPolygon(hull[..^1], fill);
                }
            }
        }
        Vector2[] closed = new Vector2[points.Length + 1];
        points.CopyTo(closed, 0);
        closed[^1] = points[0];
        canvas.DrawPolyline(closed, UiStyle.Ink, width, true);
    }

    public static Vector2[] Circle(Vector2 centre, float radius, int segments = 28)
    {
        Vector2[] points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.Tau / segments;
            points[i] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        return points;
    }

    /// <summary>An arc as a thick inked band from angle <paramref name="from"/> to <paramref name="to"/> (radians, 0 = up, clockwise).</summary>
    public static void Arc(CanvasItem canvas, Vector2 centre, float radius, float from, float to, float width, Color colour)
    {
        int segments = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(to - from) / 0.12f));
        Vector2[] points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float a = Mathf.Lerp(from, to, i / (float)segments) - Mathf.Pi * 0.5f;
            points[i] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        canvas.DrawPolyline(points, colour, width, true);
    }

    /// <summary>Draws an icon inside <paramref name="box"/>.</summary>
    public static void Icon(CanvasItem canvas, InkIcon icon, Rect2 box, int seed = 3)
    {
        Vector2 c = box.GetCenter();
        float s = Mathf.Min(box.Size.X, box.Size.Y) * 0.5f;
        switch (icon)
        {
            case InkIcon.Heart:
            {
                List<Vector2> heart = new();
                for (int i = 0; i < 32; i++)
                {
                    float t = i * Mathf.Tau / 32.0f;
                    float x = 16.0f * Mathf.Pow(Mathf.Sin(t), 3.0f);
                    float y = 13.0f * Mathf.Cos(t) - 5.0f * Mathf.Cos(2 * t) - 2.0f * Mathf.Cos(3 * t) - Mathf.Cos(4 * t);
                    heart.Add(c + new Vector2(x, -y - 1.5f) * (s / 18.0f));
                }
                Shape(canvas, heart, UiStyle.Health, 2.5f, seed);
                canvas.DrawLine(c + new Vector2(-s * 0.45f, -s * 0.35f), c + new Vector2(-s * 0.25f, -s * 0.55f), new Color(1, 1, 1, 0.35f), 2.5f);
                break;
            }
            case InkIcon.Stomach:
            {
                List<Vector2> bean = new();
                for (int i = 0; i < 28; i++)
                {
                    float t = i * Mathf.Tau / 28.0f;
                    float r = s * (0.62f + 0.12f * Mathf.Sin(2 * t));
                    bean.Add(c + new Vector2(Mathf.Cos(t) * r * 1.05f, Mathf.Sin(t) * r * 0.8f + Mathf.Cos(t) * s * 0.15f));
                }
                Shape(canvas, bean, UiStyle.Hunger, 2.5f, seed);
                canvas.DrawLine(c + new Vector2(-s * 0.55f, -s * 0.55f), c + new Vector2(-s * 0.35f, -s * 0.2f), UiStyle.Ink, 2.5f);
                break;
            }
            case InkIcon.Drop:
            {
                List<Vector2> drop = new();
                for (int i = 0; i < 26; i++)
                {
                    float t = i * Mathf.Tau / 26.0f;
                    float r = s * 0.55f;
                    Vector2 p = new(Mathf.Sin(t) * r * (1.0f - 0.35f * Mathf.Cos(t)), -Mathf.Cos(t) * r * 1.25f);
                    drop.Add(c + p + new Vector2(0, s * 0.15f));
                }
                Shape(canvas, drop, UiStyle.Thirst, 2.5f, seed);
                break;
            }
            case InkIcon.Boot:
            case InkIcon.Shoe:
            {
                Vector2[] boot =
                {
                    c + new Vector2(-0.3f, -0.75f) * s, c + new Vector2(0.15f, -0.75f) * s, c + new Vector2(0.18f, 0.15f) * s,
                    c + new Vector2(0.75f, 0.3f) * s, c + new Vector2(0.8f, 0.65f) * s, c + new Vector2(-0.35f, 0.65f) * s,
                };
                Color leather = UiStyle.PaperDark;
                if (icon == InkIcon.Boot)
                {
                    leather = UiStyle.Stamina;
                }
                Shape(canvas, boot, leather, 2.5f, seed);
                canvas.DrawLine(c + new Vector2(-0.35f, 0.45f) * s, c + new Vector2(0.78f, 0.45f) * s, UiStyle.Ink, 2.0f);
                break;
            }
            case InkIcon.Snowflake:
            {
                for (int i = 0; i < 3; i++)
                {
                    float a = i * Mathf.Pi / 3.0f;
                    Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s * 0.75f;
                    canvas.DrawLine(c - d, c + d, UiStyle.Cold.Darkened(0.4f), 3.0f, true);
                }
                break;
            }
            case InkIcon.Backpack:
            {
                Vector2[] bag =
                {
                    c + new Vector2(-0.55f, -0.45f) * s, c + new Vector2(0.55f, -0.45f) * s, c + new Vector2(0.65f, 0.75f) * s, c + new Vector2(-0.65f, 0.75f) * s,
                };
                Shape(canvas, bag, new Color(0.55f, 0.38f, 0.22f), 2.5f, seed);
                Vector2[] flap = { c + new Vector2(-0.55f, -0.45f) * s, c + new Vector2(0.55f, -0.45f) * s, c + new Vector2(0.45f, 0.05f) * s, c + new Vector2(-0.45f, 0.05f) * s };
                Shape(canvas, flap, new Color(0.45f, 0.3f, 0.18f), 2.0f, seed + 1);
                Arc(canvas, c + new Vector2(0, -0.45f) * s, s * 0.28f, -1.4f, 1.4f, 2.5f, UiStyle.Ink);
                break;
            }
            case InkIcon.Hat:
            {
                Vector2[] hat = { c + new Vector2(-0.8f, 0.35f) * s, c + new Vector2(0.8f, 0.35f) * s, c + new Vector2(0.4f, 0.15f) * s, c + new Vector2(0.35f, -0.55f) * s, c + new Vector2(-0.35f, -0.55f) * s, c + new Vector2(-0.4f, 0.15f) * s };
                Shape(canvas, hat, new Color(0.35f, 0.32f, 0.28f, 0.55f), 2.0f, seed);
                break;
            }
            case InkIcon.Shirt:
            {
                Vector2[] shirt = { c + new Vector2(-0.3f, -0.7f) * s, c + new Vector2(0.3f, -0.7f) * s, c + new Vector2(0.8f, -0.35f) * s, c + new Vector2(0.6f, 0.0f) * s, c + new Vector2(0.4f, -0.15f) * s, c + new Vector2(0.45f, 0.75f) * s, c + new Vector2(-0.45f, 0.75f) * s, c + new Vector2(-0.4f, -0.15f) * s, c + new Vector2(-0.6f, 0.0f) * s, c + new Vector2(-0.8f, -0.35f) * s };
                Shape(canvas, shirt, new Color(0.35f, 0.32f, 0.28f, 0.55f), 2.0f, seed);
                break;
            }
            case InkIcon.Trousers:
            {
                Vector2[] legs = { c + new Vector2(-0.45f, -0.75f) * s, c + new Vector2(0.45f, -0.75f) * s, c + new Vector2(0.5f, 0.8f) * s, c + new Vector2(0.12f, 0.8f) * s, c + new Vector2(0.0f, -0.2f) * s, c + new Vector2(-0.12f, 0.8f) * s, c + new Vector2(-0.5f, 0.8f) * s };
                Shape(canvas, legs, new Color(0.35f, 0.32f, 0.28f, 0.55f), 2.0f, seed);
                break;
            }
            case InkIcon.Glove:
            {
                Vector2[] glove = { c + new Vector2(-0.4f, 0.8f) * s, c + new Vector2(-0.45f, -0.2f) * s, c + new Vector2(-0.3f, -0.75f) * s, c + new Vector2(0.0f, -0.8f) * s, c + new Vector2(0.3f, -0.7f) * s, c + new Vector2(0.4f, -0.1f) * s, c + new Vector2(0.75f, -0.25f) * s, c + new Vector2(0.6f, 0.2f) * s, c + new Vector2(0.4f, 0.8f) * s };
                Shape(canvas, glove, new Color(0.35f, 0.32f, 0.28f, 0.55f), 2.0f, seed);
                break;
            }
            case InkIcon.Cape:
            {
                Vector2[] cape = { c + new Vector2(-0.25f, -0.75f) * s, c + new Vector2(0.25f, -0.75f) * s, c + new Vector2(0.75f, 0.8f) * s, c + new Vector2(-0.75f, 0.8f) * s };
                Shape(canvas, cape, new Color(0.35f, 0.32f, 0.28f, 0.55f), 2.0f, seed);
                break;
            }
            case InkIcon.Figure:
            {
                Color body = new(0.72f, 0.64f, 0.5f);
                Shape(canvas, Circle(c + new Vector2(0, -0.78f) * s, s * 0.14f, 16), body, 2.0f, seed);
                Vector2[] torso = { c + new Vector2(-0.22f, -0.6f) * s, c + new Vector2(0.22f, -0.6f) * s, c + new Vector2(0.2f, 0.05f) * s, c + new Vector2(-0.2f, 0.05f) * s };
                Shape(canvas, torso, body, 2.0f, seed + 1);
                canvas.DrawPolyline(Wobble(new[] { c + new Vector2(-0.22f, -0.55f) * s, c + new Vector2(-0.38f, -0.05f) * s, c + new Vector2(-0.36f, 0.25f) * s }, 1.0f, seed), UiStyle.Ink, 3.0f, true);
                canvas.DrawPolyline(Wobble(new[] { c + new Vector2(0.22f, -0.55f) * s, c + new Vector2(0.38f, -0.05f) * s, c + new Vector2(0.36f, 0.25f) * s }, 1.0f, seed + 2), UiStyle.Ink, 3.0f, true);
                canvas.DrawPolyline(Wobble(new[] { c + new Vector2(-0.1f, 0.05f) * s, c + new Vector2(-0.15f, 0.5f) * s, c + new Vector2(-0.16f, 0.92f) * s }, 1.0f, seed + 3), UiStyle.Ink, 3.5f, true);
                canvas.DrawPolyline(Wobble(new[] { c + new Vector2(0.1f, 0.05f) * s, c + new Vector2(0.15f, 0.5f) * s, c + new Vector2(0.16f, 0.92f) * s }, 1.0f, seed + 4), UiStyle.Ink, 3.5f, true);
                break;
            }
        }
    }
}
