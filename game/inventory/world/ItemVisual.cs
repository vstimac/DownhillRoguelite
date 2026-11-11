using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// How an item looks, in the world and as its inventory icon, from the first thing it has: its own
/// model (an art-pack scene), a named piece of another model (the carrier's hard hat, vest, trousers or
/// boots: ModelSource and ModelMesh), or else a model built in code (ItemModels). Every item has a
/// 3D model; the ink outline drawing is kept only for the journal's sketches.
/// </summary>
public static class ItemVisual
{
    private const int OutlineSize = 128;
    private static readonly Color OutlineInk = new(0.17f, 0.12f, 0.08f, 1.0f);
    private static readonly Color OutlinePaper = new(0.93f, 0.88f, 0.76f, 0.55f);
    private static readonly Dictionary<string, Mesh?> Pieces = new();
    private static readonly Dictionary<string, Texture2D> Outlines = new();

    /// <summary>True if the item has a 3D model: always, since ItemModels builds the rest.</summary>
    public static bool HasModel(ItemDefinition item)
    {
        return true;
    }

    /// <summary>The item's model, unfitted (its natural size).</summary>
    public static Node3D? BuildModel(ItemDefinition item)
    {
        if (item.Model != null)
        {
            Node3D holder = new();
            holder.AddChild(item.Model.Instantiate<Node3D>());
            return holder;
        }
        Mesh? mesh = PieceMesh(item);
        if (mesh == null)
        {
            return ItemModels.Build(item);
        }
        Node3D piece = new();
        MeshInstance3D instance = new() { Mesh = mesh };
        if (item.ModelLieFlat)
        {
            // Laid down on its back, the way clothes lie on the ground.
            instance.Rotation = new Vector3(-Mathf.Pi * 0.5f, 0.0f, 0.0f);
        }
        piece.AddChild(instance);
        return piece;
    }

    /// <summary>A piece of another model by name (cached; the source scene is loaded once).</summary>
    private static Mesh? PieceMesh(ItemDefinition item)
    {
        if (item.ModelSource.Length == 0 || item.ModelMesh.Length == 0)
        {
            return null;
        }
        string key = item.ModelSource + "|" + item.ModelMesh;
        if (Pieces.TryGetValue(key, out Mesh? cached))
        {
            return cached;
        }
        Mesh? mesh = null;
        if (ResourceLoader.Exists(item.ModelSource) && ResourceLoader.Load(item.ModelSource) is PackedScene scene)
        {
            Node source = scene.Instantiate();
            if (source.FindChild(item.ModelMesh, true, false) is MeshInstance3D found)
            {
                mesh = found.Mesh;
            }
            source.Free();
        }
        Pieces[key] = mesh;
        return mesh;
    }

    /// <summary>A standing ink outline of the item, as tall as its block, turning to face the camera.</summary>
    public static Node3D BuildPlaceholder(ItemDefinition item, Vector3 size)
    {
        float height = Mathf.Max(Mathf.Max(size.X, size.Y), 0.3f);
        return new Sprite3D
        {
            Texture = Outline(item),
            PixelSize = height / OutlineSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            Position = Vector3.Up * height * 0.5f,
            Shaded = false,
            DoubleSided = true,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
        };
    }

    /// <summary>The item's ink outline drawing (cached per item).</summary>
    public static Texture2D Outline(ItemDefinition item)
    {
        if (Outlines.TryGetValue(item.ItemId, out Texture2D? texture))
        {
            return texture;
        }
        Image image = Image.CreateEmpty(OutlineSize, OutlineSize, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 0));
        uint seed = (uint)item.ItemId.GetHashCode();
        foreach (Vector2[] line in Shape(item))
        {
            Stroke(image, line, seed);
            seed = seed * 747796405u + 2891336453u;
        }
        texture = ImageTexture.CreateFromImage(image);
        Outlines[item.ItemId] = texture;
        return texture;
    }

    /// <summary>The lines of an item's drawing, in 0..1 coordinates (y down); closed shapes repeat their first point.</summary>
    private static List<Vector2[]> Shape(ItemDefinition item)
    {
        List<Vector2[]> lines = new();
        if (item.Category == ItemCategory.Clothing)
        {
            switch (item.Slot)
            {
                case EquipSlot.Head:
                    lines.Add(Arc(new Vector2(0.5f, 0.68f), new Vector2(0.28f, 0.34f), Mathf.Pi, Mathf.Tau, 14));
                    lines.Add(new[] { new Vector2(0.1f, 0.7f), new Vector2(0.9f, 0.7f) });
                    lines.Add(new[] { new Vector2(0.26f, 0.6f), new Vector2(0.74f, 0.6f) });
                    break;
                case EquipSlot.Torso:
                    lines.Add(Closed(new Vector2(0.32f, 0.12f), new Vector2(0.42f, 0.2f), new Vector2(0.58f, 0.2f), new Vector2(0.68f, 0.12f),
                        new Vector2(0.92f, 0.3f), new Vector2(0.82f, 0.46f), new Vector2(0.72f, 0.38f), new Vector2(0.72f, 0.9f),
                        new Vector2(0.28f, 0.9f), new Vector2(0.28f, 0.38f), new Vector2(0.18f, 0.46f), new Vector2(0.08f, 0.3f)));
                    lines.Add(new[] { new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.9f) });
                    break;
                case EquipSlot.Legs:
                    lines.Add(Closed(new Vector2(0.28f, 0.08f), new Vector2(0.72f, 0.08f), new Vector2(0.8f, 0.92f), new Vector2(0.58f, 0.92f),
                        new Vector2(0.5f, 0.38f), new Vector2(0.42f, 0.92f), new Vector2(0.2f, 0.92f)));
                    lines.Add(new[] { new Vector2(0.28f, 0.16f), new Vector2(0.72f, 0.16f) });
                    break;
                case EquipSlot.Hands:
                    lines.Add(Closed(new Vector2(0.36f, 0.92f), new Vector2(0.36f, 0.52f), new Vector2(0.22f, 0.44f), new Vector2(0.17f, 0.33f),
                        new Vector2(0.27f, 0.29f), new Vector2(0.38f, 0.38f), new Vector2(0.38f, 0.16f), new Vector2(0.5f, 0.08f),
                        new Vector2(0.62f, 0.12f), new Vector2(0.67f, 0.3f), new Vector2(0.67f, 0.92f)));
                    lines.Add(new[] { new Vector2(0.36f, 0.8f), new Vector2(0.67f, 0.8f) });
                    break;
                case EquipSlot.Feet:
                    lines.Add(Closed(new Vector2(0.3f, 0.1f), new Vector2(0.56f, 0.1f), new Vector2(0.56f, 0.56f), new Vector2(0.84f, 0.66f),
                        new Vector2(0.9f, 0.84f), new Vector2(0.24f, 0.84f), new Vector2(0.27f, 0.56f)));
                    lines.Add(new[] { new Vector2(0.24f, 0.76f), new Vector2(0.9f, 0.76f) });
                    break;
                default:
                    lines.Add(Closed(new Vector2(0.4f, 0.1f), new Vector2(0.6f, 0.1f), new Vector2(0.86f, 0.9f), new Vector2(0.5f, 0.84f), new Vector2(0.14f, 0.9f)));
                    lines.Add(Arc(new Vector2(0.5f, 0.12f), new Vector2(0.16f, 0.08f), 0.0f, Mathf.Pi, 8));
                    break;
            }
            return lines;
        }
        switch (item.ItemId)
        {
            case "spider_silk":
                for (int i = 0; i < 4; i++)
                {
                    lines.Add(Arc(new Vector2(0.5f, 0.5f), new Vector2(0.36f - i * 0.02f, 0.2f + i * 0.05f), i * 0.4f, i * 0.4f + Mathf.Tau, 18));
                }
                lines.Add(new[] { new Vector2(0.84f, 0.48f), new Vector2(0.95f, 0.2f) });
                return lines;
            case "cord":
                lines.Add(Arc(new Vector2(0.5f, 0.5f), new Vector2(0.36f, 0.36f), 0.0f, Mathf.Tau, 20));
                lines.Add(Arc(new Vector2(0.5f, 0.5f), new Vector2(0.24f, 0.24f), 0.0f, Mathf.Tau, 16));
                lines.Add(Arc(new Vector2(0.5f, 0.5f), new Vector2(0.12f, 0.12f), 0.0f, Mathf.Tau, 12));
                lines.Add(new[] { new Vector2(0.86f, 0.5f), new Vector2(0.95f, 0.72f) });
                return lines;
            case "leather_patch":
                lines.Add(Closed(new Vector2(0.15f, 0.25f), new Vector2(0.85f, 0.2f), new Vector2(0.88f, 0.78f), new Vector2(0.12f, 0.8f)));
                for (int i = 0; i < 6; i++)
                {
                    float x = 0.24f + i * 0.1f;
                    lines.Add(new[] { new Vector2(x, 0.32f), new Vector2(x + 0.05f, 0.32f) });
                    lines.Add(new[] { new Vector2(x, 0.7f), new Vector2(x + 0.05f, 0.7f) });
                }
                return lines;
            case "paper":
                lines.Add(Closed(new Vector2(0.24f, 0.08f), new Vector2(0.62f, 0.08f), new Vector2(0.78f, 0.24f), new Vector2(0.78f, 0.92f), new Vector2(0.24f, 0.92f)));
                lines.Add(new[] { new Vector2(0.62f, 0.08f), new Vector2(0.62f, 0.24f), new Vector2(0.78f, 0.24f) });
                for (int i = 0; i < 4; i++)
                {
                    float y = 0.38f + i * 0.13f;
                    lines.Add(new[] { new Vector2(0.32f, y), new Vector2(0.7f, y) });
                }
                return lines;
            case "pencil":
                lines.Add(Closed(new Vector2(0.18f, 0.74f), new Vector2(0.66f, 0.26f), new Vector2(0.74f, 0.34f), new Vector2(0.26f, 0.82f)));
                lines.Add(new[] { new Vector2(0.66f, 0.26f), new Vector2(0.86f, 0.14f), new Vector2(0.74f, 0.34f) });
                lines.Add(new[] { new Vector2(0.24f, 0.68f), new Vector2(0.32f, 0.76f) });
                return lines;
        }
        // Anything else: a tied bundle.
        lines.Add(Closed(new Vector2(0.14f, 0.32f), new Vector2(0.86f, 0.32f), new Vector2(0.86f, 0.88f), new Vector2(0.14f, 0.88f)));
        lines.Add(new[] { new Vector2(0.14f, 0.32f), new Vector2(0.3f, 0.16f), new Vector2(0.98f, 0.16f), new Vector2(0.86f, 0.32f) });
        lines.Add(new[] { new Vector2(0.5f, 0.32f), new Vector2(0.5f, 0.88f) });
        lines.Add(new[] { new Vector2(0.64f, 0.16f), new Vector2(0.5f, 0.32f) });
        return lines;
    }

    private static Vector2[] Closed(params Vector2[] points)
    {
        Vector2[] closed = new Vector2[points.Length + 1];
        points.CopyTo(closed, 0);
        closed[^1] = points[0];
        return closed;
    }

    private static Vector2[] Arc(Vector2 centre, Vector2 radius, float from, float to, int steps)
    {
        Vector2[] points = new Vector2[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(from, to, i / (float)steps);
            points[i] = centre + new Vector2(Mathf.Cos(a) * radius.X, Mathf.Sin(a) * radius.Y);
        }
        return points;
    }

    /// <summary>Draws a hand-inked line: a soft round nib, a slight wobble, a faint paper wash behind it.</summary>
    private static void Stroke(Image image, Vector2[] points, uint seed)
    {
        RandomNumberGenerator random = new() { Seed = seed };
        float scale = OutlineSize - 12.0f;
        Vector2 offset = new(6.0f, 6.0f);
        for (int i = 1; i < points.Length; i++)
        {
            Vector2 a = points[i - 1] * scale + offset + new Vector2(random.RandfRange(-1.2f, 1.2f), random.RandfRange(-1.2f, 1.2f));
            Vector2 b = points[i] * scale + offset + new Vector2(random.RandfRange(-1.2f, 1.2f), random.RandfRange(-1.2f, 1.2f));
            int steps = Mathf.Max(1, Mathf.CeilToInt(a.DistanceTo(b) * 1.5f));
            for (int s = 0; s <= steps; s++)
            {
                Vector2 p = a.Lerp(b, s / (float)steps);
                Dab(image, p, 5.5f, OutlinePaper);
                Dab(image, p, 2.6f, OutlineInk);
            }
        }
    }

    /// <summary>A round dab of colour, soft at its edge, blended over what's there.</summary>
    private static void Dab(Image image, Vector2 at, float radius, Color colour)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt(at.X - radius - 1));
        int x1 = Mathf.Min(OutlineSize - 1, Mathf.CeilToInt(at.X + radius + 1));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(at.Y - radius - 1));
        int y1 = Mathf.Min(OutlineSize - 1, Mathf.CeilToInt(at.Y + radius + 1));
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float d = new Vector2(x + 0.5f, y + 0.5f).DistanceTo(at);
                float cover = Mathf.Clamp(radius - d + 0.5f, 0.0f, 1.0f) * colour.A;
                if (cover <= 0.0f)
                {
                    continue;
                }
                Color under = image.GetPixel(x, y);
                // Ink always wins over paper; paper never covers ink.
                if (colour.A < 1.0f && under.A >= 0.99f && under.R < 0.5f)
                {
                    continue;
                }
                float alpha = Mathf.Max(under.A, cover);
                Color mixed = under.Lerp(new Color(colour, 1.0f), cover);
                image.SetPixel(x, y, new Color(mixed, alpha));
            }
        }
    }
}
