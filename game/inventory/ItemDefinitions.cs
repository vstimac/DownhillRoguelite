using System.Collections;
using System.Collections.Generic;

namespace DownhillRoguelite.Inventory;

/// <summary>A live definition-only view of stored copies, without allocating on Count or indexing.</summary>
internal sealed class ItemDefinitions : IReadOnlyList<ItemDefinition>
{
    private readonly IReadOnlyList<StoredItem> _items;

    public ItemDefinitions(IReadOnlyList<StoredItem> items)
    {
        _items = items;
    }

    public int Count => _items.Count;
    public ItemDefinition this[int index] => _items[index].Definition;

    public IEnumerator<ItemDefinition> GetEnumerator()
    {
        foreach (StoredItem item in _items)
        {
            yield return item.Definition;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
