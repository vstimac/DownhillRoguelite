using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// 3D models built in code for the items that have no art-pack scene or carrier piece: the wool
/// hat, leather gloves, oilskin coat, fur cape, paper, pencil, cord and leather patch. Every item
/// therefore has a real model in the world and a photographed inventory icon (ItemIcons), never
/// a flat placeholder. Sizes are in metres; ItemVisual fits them into the item's cell block.
/// The worn versions on the carrier (CarrierWornGear) use the same shapes and materials.
/// </summary>
public static class ItemModels
{
    public static readonly Color WoolRed = new(0.55f, 0.13f, 0.1f);
    public static readonly Color WoolCream = new(0.88f, 0.84f, 0.74f);
    public static readonly Color Leather = new(0.36f, 0.22f, 0.12f);
    public static readonly Color Oilskin = new(0.42f, 0.36f, 0.16f);
    public static readonly Color Fur = new(0.42f, 0.32f, 0.22f);
    public static readonly Color FurLight = new(0.66f, 0.56f, 0.44f);
    public static readonly Color HardHatYellow = new(0.95f, 0.72f, 0.1f);
    public static readonly Color HiVis = new(0.85f, 0.95f, 0.15f);
    public static readonly Color Reflective = new(0.78f, 0.8f, 0.82f);
    private static readonly Dictionary<Color, StandardMaterial3D> Materials = new();

    /// <summary>The item's built model, or a tied bundle in its own colour for anything not listed.</summary>
    public static Node3D Build(ItemDefinition item)
    {
        Node3D root = new() { Name = "Model" };
        switch (item.ItemId)
        {
            case "wool_hat":
                AddHat(root, Vector3.Zero, 0.1f);
                break;
            case "leather_gloves":
                AddGlove(root, new Vector3(-0.07f, 0.0f, 0.0f), 1.0f);
                AddGlove(root, new Vector3(0.07f, 0.0f, 0.02f), -1.0f);
                break;
            case "oilskin_coat":
                AddCoat(root);
                break;
            case "fur_cape":
                AddCape(root, Vector3.Zero, 0.5f, 0.85f);
                break;
            case "paper":
                AddPaper(root);
                break;
            case "pencil":
                AddPencil(root);
                break;
            case "cord":
                AddCord(root);
                break;
            case "spider_silk":
                AddSilk(root);
                break;
            case "leather_patch":
                AddPatch(root);
                break;
            case "jade_urn":
                AddUrn(root);
                break;
            default:
                AddBundle(root, item.DebugColor);
                break;
        }
        return root;
    }

    /// <summary>A plain, slightly rough material in a colour (shared per colour).</summary>
    public static StandardMaterial3D Material(Color colour, float roughness = 0.9f)
    {
        if (Materials.TryGetValue(colour, out StandardMaterial3D? material))
        {
            return material;
        }
        material = new StandardMaterial3D
        {
            AlbedoColor = colour,
            Roughness = roughness,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        Materials[colour] = material;
        return material;
    }

    public static MeshInstance3D Part(Node3D parent, Mesh mesh, Color colour, Vector3 position, Vector3 rotationDegrees = default, float roughness = 0.9f)
    {
        MeshInstance3D part = new()
        {
            Mesh = mesh,
            Position = position,
            RotationDegrees = rotationDegrees,
            MaterialOverride = Material(colour, roughness),
        };
        parent.AddChild(part);
        return part;
    }

    /// <summary>A knitted beanie: a dome, a turned-up cuff and a bobble; <paramref name="radius"/> is the head's.</summary>
    public static void AddHat(Node3D parent, Vector3 at, float radius)
    {
        Part(parent, new SphereMesh { Radius = radius, Height = radius, IsHemisphere = true, RadialSegments = 20, Rings = 8 }, WoolRed, at);
        Part(parent, new TorusMesh { InnerRadius = radius * 0.92f, OuterRadius = radius * 1.12f, Rings = 20, RingSegments = 8 }, WoolCream, at + Vector3.Up * radius * 0.08f);
        Part(parent, new SphereMesh { Radius = radius * 0.22f, Height = radius * 0.44f, RadialSegments = 10, Rings = 6 }, WoolCream, at + Vector3.Up * radius * 1.02f);
    }

    /// <summary>A builder's hard hat: a yellow dome with a ridge and a brim; <paramref name="radius"/> is the head's.</summary>
    public static void AddHardHat(Node3D parent, Vector3 at, float radius)
    {
        Part(parent, new SphereMesh { Radius = radius * 1.08f, Height = radius * 1.0f, IsHemisphere = true, RadialSegments = 20, Rings = 8 }, HardHatYellow, at, default, 0.35f);
        Part(parent, new CylinderMesh { TopRadius = radius * 1.3f, BottomRadius = radius * 1.3f, Height = radius * 0.08f, RadialSegments = 20 }, HardHatYellow,
            at + Vector3.Up * radius * 0.04f, default, 0.35f);
        Part(parent, new BoxMesh { Size = new Vector3(radius * 0.25f, radius * 0.18f, radius * 1.9f) }, HardHatYellow.Darkened(0.12f),
            at + Vector3.Up * radius * 0.95f, default, 0.35f);
    }

    /// <summary>A leather glove lying palm down, fingers along -Z; <paramref name="side"/> 1 for left, -1 for right.</summary>
    public static void AddGlove(Node3D parent, Vector3 at, float side)
    {
        Part(parent, new BoxMesh { Size = new Vector3(0.085f, 0.03f, 0.1f) }, Leather, at);
        for (int i = 0; i < 4; i++)
        {
            float x = (i - 1.5f) * 0.021f;
            float length = 0.07f - Mathf.Abs(i - 1.3f) * 0.008f;
            Part(parent, new CapsuleMesh { Radius = 0.01f, Height = length, RadialSegments = 8, Rings = 2 }, Leather,
                at + new Vector3(x, 0.0f, -0.05f - length * 0.5f + 0.01f), new Vector3(90.0f, 0.0f, 0.0f));
        }
        Part(parent, new CapsuleMesh { Radius = 0.011f, Height = 0.06f, RadialSegments = 8, Rings = 2 }, Leather,
            at + new Vector3(0.05f * side, 0.0f, -0.02f), new Vector3(90.0f, 50.0f * side, 0.0f));
        Part(parent, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.04f, RadialSegments = 12 }, Leather,
            at + new Vector3(0.0f, 0.0f, 0.065f), new Vector3(90.0f, 0.0f, 0.0f));
    }

    /// <summary>An oilskin coat hung up: a long body, two sleeves, a collar and a row of buttons.</summary>
    private static void AddCoat(Node3D parent)
    {
        MeshInstance3D body = Part(parent, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.27f, Height = 0.85f, RadialSegments = 16 }, Oilskin,
            new Vector3(0.0f, 0.425f, 0.0f), default, 0.45f);
        body.Scale = new Vector3(1.0f, 1.0f, 0.6f);
        for (int s = -1; s <= 1; s += 2)
        {
            Part(parent, new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.07f, Height = 0.6f, RadialSegments = 10 }, Oilskin,
                new Vector3(0.27f * s, 0.55f, 0.0f), new Vector3(0.0f, 0.0f, 18.0f * s), 0.45f);
        }
        Part(parent, new TorusMesh { InnerRadius = 0.1f, OuterRadius = 0.15f, Rings = 16, RingSegments = 6 }, Oilskin.Darkened(0.25f),
            new Vector3(0.0f, 0.86f, 0.0f), default, 0.45f);
        for (int i = 0; i < 4; i++)
        {
            Part(parent, new CylinderMesh { TopRadius = 0.014f, BottomRadius = 0.014f, Height = 0.01f, RadialSegments = 8 }, new Color(0.15f, 0.12f, 0.08f),
                new Vector3(0.0f, 0.72f - i * 0.17f, 0.165f), new Vector3(90.0f, 0.0f, 0.0f));
        }
    }

    /// <summary>A fur cape: an open curved sheet (behind, -Z) widening to its hem, with a pale fur collar on top.</summary>
    public static void AddCape(Node3D parent, Vector3 top, float width, float length)
    {
        MeshInstance3D sheet = new()
        {
            Mesh = Shell(width * 0.35f, width * 0.62f, length, Mathf.DegToRad(200.0f), Mathf.DegToRad(340.0f), 14),
            Position = top + Vector3.Down * length * 0.5f,
            MaterialOverride = Material(Fur),
        };
        parent.AddChild(sheet);
        MeshInstance3D collar = Part(parent, new TorusMesh { InnerRadius = width * 0.26f, OuterRadius = width * 0.4f, Rings = 16, RingSegments = 8 }, FurLight, top);
        collar.Scale = new Vector3(1.0f, 1.2f, 0.75f);
    }

    /// <summary>Two sheets of paper, the top one with a few pencilled lines.</summary>
    private static void AddPaper(Node3D parent)
    {
        Part(parent, new BoxMesh { Size = new Vector3(0.21f, 0.002f, 0.297f) }, new Color(0.9f, 0.88f, 0.8f), Vector3.Zero, new Vector3(0.0f, 8.0f, 0.0f));
        Part(parent, new BoxMesh { Size = new Vector3(0.21f, 0.002f, 0.297f) }, new Color(0.95f, 0.93f, 0.86f), Vector3.Up * 0.003f, new Vector3(0.0f, -4.0f, 0.0f));
        for (int i = 0; i < 5; i++)
        {
            Part(parent, new BoxMesh { Size = new Vector3(0.15f - (i % 2) * 0.04f, 0.001f, 0.003f) }, new Color(0.35f, 0.33f, 0.32f),
                new Vector3(-(i % 2) * 0.02f, 0.0045f, -0.09f + i * 0.04f), new Vector3(0.0f, -4.0f, 0.0f));
        }
    }

    /// <summary>A yellow hexagonal pencil lying along X: sharpened wood and lead at one end, a ferrule and eraser at the other.</summary>
    private static void AddPencil(Node3D parent)
    {
        Vector3 along = new(0.0f, 0.0f, 90.0f);
        Part(parent, new CylinderMesh { TopRadius = 0.0045f, BottomRadius = 0.0045f, Height = 0.15f, RadialSegments = 6 }, new Color(0.9f, 0.7f, 0.1f), Vector3.Zero, along, 0.5f);
        Part(parent, new CylinderMesh { TopRadius = 0.0015f, BottomRadius = 0.0045f, Height = 0.02f, RadialSegments = 6 }, new Color(0.85f, 0.7f, 0.5f), new Vector3(-0.085f, 0.0f, 0.0f), along);
        Part(parent, new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.0015f, Height = 0.006f, RadialSegments = 6 }, new Color(0.15f, 0.15f, 0.16f), new Vector3(-0.098f, 0.0f, 0.0f), along);
        Part(parent, new CylinderMesh { TopRadius = 0.0048f, BottomRadius = 0.0048f, Height = 0.012f, RadialSegments = 10 }, new Color(0.7f, 0.7f, 0.68f), new Vector3(0.081f, 0.0f, 0.0f), along, 0.3f);
        Part(parent, new CylinderMesh { TopRadius = 0.0045f, BottomRadius = 0.0045f, Height = 0.01f, RadialSegments = 10 }, new Color(0.85f, 0.45f, 0.45f), new Vector3(0.092f, 0.0f, 0.0f), along);
    }

    /// <summary>A coil of cord: stacked loops with a loose end.</summary>
    private static void AddCord(Node3D parent)
    {
        Color cord = new(0.72f, 0.62f, 0.42f);
        for (int i = 0; i < 4; i++)
        {
            Part(parent, new TorusMesh { InnerRadius = 0.07f, OuterRadius = 0.085f, Rings = 20, RingSegments = 6 }, cord,
                new Vector3(0.004f * i, 0.008f + i * 0.013f, 0.003f * (i % 2)), new Vector3(3.0f * (i % 2), 0.0f, 2.0f * i));
        }
        Part(parent, new CylinderMesh { TopRadius = 0.0075f, BottomRadius = 0.0075f, Height = 0.12f, RadialSegments = 6 }, cord,
            new Vector3(0.12f, 0.01f, 0.03f), new Vector3(0.0f, 30.0f, 85.0f));
    }

    /// <summary>A pale, loose hank of spider silk: a few thin loops wound together.</summary>
    private static void AddSilk(Node3D parent)
    {
        Color silk = new(0.93f, 0.93f, 0.96f);
        for (int i = 0; i < 5; i++)
        {
            Part(parent, new TorusMesh { InnerRadius = 0.05f, OuterRadius = 0.056f, Rings = 18, RingSegments = 5 }, silk,
                new Vector3(0.003f * i, 0.006f + i * 0.004f, 0.004f * (i % 2)), new Vector3(6.0f * (i % 3), 25.0f * i, 4.0f * (i % 2)));
        }
    }

    /// <summary>A rounded leather patch with stitch holes round its edge.</summary>
    private static void AddPatch(Node3D parent)
    {
        MeshInstance3D patch = Part(parent, new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 0.006f, RadialSegments = 16 }, Leather, Vector3.Up * 0.003f);
        patch.Scale = new Vector3(1.3f, 1.0f, 1.0f);
        for (int i = 0; i < 14; i++)
        {
            float a = Mathf.Tau * i / 14.0f;
            Part(parent, new BoxMesh { Size = new Vector3(0.008f, 0.002f, 0.003f) }, new Color(0.85f, 0.8f, 0.65f),
                new Vector3(Mathf.Cos(a) * 0.07f, 0.0068f, Mathf.Sin(a) * 0.052f), new Vector3(0.0f, -Mathf.RadToDeg(a) + 90.0f, 0.0f));
        }
    }

    /// <summary>A small jade urn: round belly, neck, lip and a side handle.</summary>
    private static void AddUrn(Node3D parent)
    {
        Color jade = new(0.22f, 0.52f, 0.36f);
        MeshInstance3D belly = Part(parent, new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 18, Rings = 10 }, jade, Vector3.Up * 0.09f, default, 0.25f);
        belly.Scale = new Vector3(1.0f, 0.9f, 1.0f);
        Part(parent, new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.055f, Height = 0.06f, RadialSegments = 16 }, jade, Vector3.Up * 0.18f, default, 0.25f);
        Part(parent, new TorusMesh { InnerRadius = 0.04f, OuterRadius = 0.058f, Rings = 16, RingSegments = 8 }, jade.Darkened(0.15f), Vector3.Up * 0.21f, default, 0.25f);
        Part(parent, new TorusMesh { InnerRadius = 0.025f, OuterRadius = 0.035f, Rings = 12, RingSegments = 6 }, jade.Darkened(0.15f),
            new Vector3(0.085f, 0.15f, 0.0f), new Vector3(90.0f, 0.0f, 0.0f), 0.25f);
    }

    /// <summary>A canvas-wrapped bundle tied with two straps, for any item without a model of its own.</summary>
    private static void AddBundle(Node3D parent, Color colour)
    {
        Part(parent, new BoxMesh { Size = new Vector3(0.3f, 0.2f, 0.24f) }, colour, Vector3.Up * 0.1f);
        for (int s = -1; s <= 1; s += 2)
        {
            Part(parent, new BoxMesh { Size = new Vector3(0.025f, 0.21f, 0.25f) }, new Color(0.3f, 0.2f, 0.12f), new Vector3(0.08f * s, 0.1f, 0.0f));
        }
    }

    /// <summary>An open, double-sided curved sheet: part of a cone around Y from <paramref name="from"/> to <paramref name="to"/> radians (0 = +X, pi/2 = +Z).</summary>
    public static ArrayMesh Shell(float topRadius, float bottomRadius, float height, float from, float to, int segments)
    {
        SurfaceTool tool = new();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.Lerp(from, to, i / (float)segments);
            float a1 = Mathf.Lerp(from, to, (i + 1) / (float)segments);
            Vector3 t0 = new(Mathf.Cos(a0) * topRadius, height * 0.5f, Mathf.Sin(a0) * topRadius);
            Vector3 t1 = new(Mathf.Cos(a1) * topRadius, height * 0.5f, Mathf.Sin(a1) * topRadius);
            Vector3 b0 = new(Mathf.Cos(a0) * bottomRadius, -height * 0.5f, Mathf.Sin(a0) * bottomRadius);
            Vector3 b1 = new(Mathf.Cos(a1) * bottomRadius, -height * 0.5f, Mathf.Sin(a1) * bottomRadius);
            tool.AddVertex(t0);
            tool.AddVertex(b1);
            tool.AddVertex(b0);
            tool.AddVertex(t0);
            tool.AddVertex(t1);
            tool.AddVertex(b1);
        }
        tool.GenerateNormals();
        return tool.Commit();
    }
}
