using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// The 24 right-angle rotations of a box (every axis permutation and sign flip with
/// determinant +1). Items are packed in one of these; index 0 is the default orientation.
/// </summary>
public static class GridOrientations
{
    public const int Identity = 0;

    public static IReadOnlyList<Basis> All { get; } = Build();

    /// <summary>Quarter turn about the vertical axis.</summary>
    public static int YawQuarter { get; } = IndexOf(new Basis(Vector3.Up, Mathf.Pi * 0.5f));
    /// <summary>Quarter tip about the x axis (front to back).</summary>
    public static int TipForwardQuarter { get; } = IndexOf(new Basis(Vector3.Right, Mathf.Pi * 0.5f));
    /// <summary>Quarter tip about the z axis (side to side).</summary>
    public static int TipSideQuarter { get; } = IndexOf(new Basis(Vector3.Back, Mathf.Pi * 0.5f));

    /// <summary>Box size after rotating, always positive.</summary>
    public static Vector3I RotatedSize(Vector3I size, int orientation)
    {
        Vector3 rotated = All[orientation] * (Vector3)size;
        return new Vector3I(
            Mathf.RoundToInt(Mathf.Abs(rotated.X)),
            Mathf.RoundToInt(Mathf.Abs(rotated.Y)),
            Mathf.RoundToInt(Mathf.Abs(rotated.Z)));
    }

    /// <summary>The orientation reached by applying <paramref name="step"/> after <paramref name="current"/>.</summary>
    public static int Compose(int step, int current)
    {
        return IndexOf(All[step] * All[current]);
    }

    /// <summary>Whether the orientation keeps the item's up axis pointing up.</summary>
    public static bool IsUpright(int orientation)
    {
        return (All[orientation] * Vector3.Up).IsEqualApprox(Vector3.Up);
    }

    public static bool Allows(OrientationRule rule, int orientation)
    {
        if (rule == OrientationRule.Upright)
        {
            return IsUpright(orientation);
        }
        return true;
    }

    private static int IndexOf(Basis basis)
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i].IsEqualApprox(basis))
            {
                return i;
            }
        }
        return Identity;
    }

    private static List<Basis> Build()
    {
        Vector3[] axes = { Vector3.Right, Vector3.Up, Vector3.Back };
        int[][] permutations =
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
            new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 },
        };

        List<Basis> result = new() { Basis.Identity };
        foreach (int[] permutation in permutations)
        {
            for (int signs = 0; signs < 8; signs++)
            {
                Vector3 x = axes[permutation[0]] * Sign(signs, 0);
                Vector3 y = axes[permutation[1]] * Sign(signs, 1);
                Vector3 z = axes[permutation[2]] * Sign(signs, 2);
                Basis basis = new(x, y, z);
                if (basis.Determinant() > 0.0f && !Contains(result, basis))
                {
                    result.Add(basis);
                }
            }
        }
        return result;
    }

    private static float Sign(int signs, int bit)
    {
        if ((signs & (1 << bit)) != 0)
        {
            return -1.0f;
        }
        return 1.0f;
    }

    private static bool Contains(List<Basis> list, Basis basis)
    {
        foreach (Basis existing in list)
        {
            if (existing.IsEqualApprox(basis))
            {
                return true;
            }
        }
        return false;
    }
}
