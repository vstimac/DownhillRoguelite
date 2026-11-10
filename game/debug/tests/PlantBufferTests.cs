using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DownhillRoguelite.Terrain;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>Plan order/fields survive block boundaries without growing large backing arrays.</summary>
public partial class PlantBufferTests : Node
{
    private const string Model = "res://test/plant.glb";
    private int _failed;

    public override void _Ready()
    {
        try { Run(); }
        catch (Exception error) { Check("plant buffer checks complete", false, error.ToString()); }
        GD.Print("PLANT BUFFER TESTS: " + _failed + " failures");
        GetTree().Quit(Math.Min(_failed, 1));
    }

    private void Run()
    {
        PlantBuffer plants = new();
        using (IEnumerator<PlantData> empty = plants.GetEnumerator())
        {
            Check("an empty plan has no entries or match", plants.Count == 0 && !empty.MoveNext()
                && plants.Find(_ => true).Equals(default(PlantData)));
        }
        const int count = 1537;
        for (int i = 0; i < count; i++) { plants.Add(MakePlant(i)); }
        Check("appending through multiple block boundaries preserves count", plants.Count == count);
        bool boundaries = true;
        foreach (int index in new[] { 0, 510, 511, 512, 513, 1023, 1024, 1535, 1536 })
        { boundaries &= plants[index].Equals(MakePlant(index)); }
        Check("boundary entries retain every plant field", boundaries);
        bool indexed = true;
        for (int i = 0; i < count; i++) { indexed &= plants[i].Equals(MakePlant(i)); }
        Check("indexed reads retain the complete generated order", indexed);
        int visited = 0;
        bool ordered = true;
        foreach (PlantData plant in plants) { ordered &= plant.Equals(MakePlant(visited)); visited++; }
        Check("generic enumeration returns the plan exactly once in order", ordered && visited == count);
        visited = 0;
        ordered = true;
        foreach (object boxed in (IEnumerable)plants)
        {
            ordered &= boxed is PlantData plant && plant.Equals(MakePlant(visited));
            visited++;
        }
        Check("non-generic enumeration retains the same record values", ordered && visited == count);
        Check("Find returns the first match across a block boundary", plants.Find(plant => plant.Kind == PlantKind.Tree
            && plant.Position.X >= 511).Equals(MakePlant(512)));
        Check("Find returns default for no match and rejects a null predicate",
            plants.Find(plant => plant.Position.X < 0).Equals(default(PlantData))
            && Throws<ArgumentNullException>(() => plants.Find(null!)));
        Check("index bounds reject negative, end and extreme values",
            Throws<ArgumentOutOfRangeException>(() => _ = plants[-1])
            && Throws<ArgumentOutOfRangeException>(() => _ = plants[plants.Count])
            && Throws<ArgumentOutOfRangeException>(() => _ = plants[int.MaxValue]));
        using (IEnumerator<PlantData> changing = plants.GetEnumerator())
        {
            bool started = changing.MoveNext();
            plants.Add(MakePlant(count));
            Check("enumeration detects modification instead of silently changing the plan",
                started && Throws<InvalidOperationException>(() => changing.MoveNext()));
        }
        MeasureAllocation(1025, out _); // Warm block growth and JIT before measurement.
        const int measuredCount = 10000;
        long allocated = MeasureAllocation(measuredCount, out PlantBuffer measured);
        long logical = (long)measuredCount * Unsafe.SizeOf<PlantData>();
        long ceiling = logical * 2 + 16 * 1024;
        Check("large plans avoid the allocation cost of repeated whole-plan growth",
            allocated < ceiling && measured.Count == measuredCount && measured[measuredCount - 1].Equals(MakePlant(measuredCount - 1)),
            allocated + " allocated bytes, " + logical + " logical bytes, limit " + ceiling);
        Check("each plant block stays below the large-object allocation boundary",
            (long)PlantBuffer.BlockSize * Unsafe.SizeOf<PlantData>() < 85000,
            PlantBuffer.BlockSize * Unsafe.SizeOf<PlantData>() + " data bytes per block");
    }

    private static PlantData MakePlant(int index) => new((PlantKind)(index % 4), Model,
        new Vector3(index, -index, index * 0.25f), index * 0.01f, 1 + index * 0.001f,
        new Vector2(index * 0.0001f, -index * 0.002f), (index & 1) == 0, index * 0.00001f);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureAllocation(int count, out PlantBuffer plants)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        plants = new PlantBuffer();
        for (int i = 0; i < count; i++) { plants.Add(MakePlant(i)); }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return true; }
        return false;
    }

    private void Check(string title, bool passed, string detail = "")
    {
        if (passed) { GD.Print("PASS  " + title + ": " + detail); }
        else { _failed++; GD.Print("FAIL  " + title + ": " + detail); }
    }
}
