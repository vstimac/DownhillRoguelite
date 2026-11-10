using System;
using System.Collections;
using System.Collections.Generic;

namespace DownhillRoguelite.Terrain;

/// <summary>
/// Append-only plant plan in small managed blocks. Growing a chunk never copies a large struct
/// array or puts its entire plan on the large-object heap; readers retain stable index order.
/// </summary>
public sealed class PlantBuffer : IReadOnlyList<PlantData>
{
    internal const int BlockSize = 512;
    private readonly List<PlantData[]> _blocks = new();
    private int _count;
    private int _version;

    public int Count => _count;

    public PlantData this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count) { throw new ArgumentOutOfRangeException(nameof(index)); }
            return _blocks[index / BlockSize][index % BlockSize];
        }
    }

    public void Add(PlantData plant)
    {
        int block = _count / BlockSize;
        if (block == _blocks.Count) { _blocks.Add(new PlantData[BlockSize]); }
        _blocks[block][_count % BlockSize] = plant;
        _count++;
        _version++;
    }

    /// <summary>Returns the first match, or default when absent, as List.Find does.</summary>
    public PlantData Find(Predicate<PlantData> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        for (int i = 0; i < _count; i++)
        {
            PlantData plant = this[i];
            if (match(plant)) { return plant; }
        }
        return default;
    }

    public IEnumerator<PlantData> GetEnumerator() => Enumerate(_version).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private IEnumerable<PlantData> Enumerate(int version)
    {
        for (int i = 0; i < _count; i++)
        {
            if (version != _version) { throw new InvalidOperationException("Plant plan changed during enumeration."); }
            yield return this[i];
        }
        if (version != _version) { throw new InvalidOperationException("Plant plan changed during enumeration."); }
    }
}
