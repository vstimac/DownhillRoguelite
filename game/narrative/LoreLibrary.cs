using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// All found texts (narrative/lore/*.tres) and the rule that chooses one for a stage. The choice is
/// deterministic: the run's seed and the stage pick it, and texts already chosen earlier in the run
/// (or sharing an exclusive group with one) are left out. Texts never read in any run are favoured.
/// </summary>
public sealed class LoreLibrary
{
    public const string Folder = "res://narrative/lore";
    private const ulong Salt = 0x4C4F5245UL;

    public IReadOnlyList<LoreEntry> Entries => _entries;

    private readonly List<LoreEntry> _entries = new();

    public LoreLibrary(IEnumerable<LoreEntry> entries)
    {
        foreach (LoreEntry entry in entries)
        {
            if (entry.Problem().Length == 0)
            {
                _entries.Add(entry);
            }
        }
    }

    /// <summary>Every valid entry in the folder (exported builds list them as .remap).</summary>
    public static LoreLibrary Load(string folder = Folder)
    {
        List<LoreEntry> entries = new();
        if (DirAccess.DirExistsAbsolute(folder))
        {
            foreach (string file in DirAccess.GetFilesAt(folder))
            {
                string name = file.Replace(".remap", "");
                if (!name.EndsWith(".tres"))
                {
                    continue;
                }
                if (ResourceLoader.Load(folder + "/" + name) is LoreEntry entry)
                {
                    entries.Add(entry);
                }
            }
        }
        entries.Sort(CompareIds);
        return new LoreLibrary(entries);
    }

    private static int CompareIds(LoreEntry a, LoreEntry b)
    {
        return string.CompareOrdinal(a.Id, b.Id);
    }

    public LoreEntry? Find(string id)
    {
        foreach (LoreEntry entry in _entries)
        {
            if (entry.Id == id)
            {
                return entry;
            }
        }
        return null;
    }

    /// <summary>
    /// The text for a stage, or null when none fits. <paramref name="used"/> holds the ids and groups
    /// already chosen this run; <paramref name="readBefore"/> tells which texts any run has read
    /// (they keep <paramref name="readWeight"/> of their chance).
    /// </summary>
    public LoreEntry? Choose(ulong seed, int stage, int circle, HashSet<string> used, System.Func<string, bool>? readBefore, float readWeight)
    {
        List<LoreEntry> fits = new();
        List<float> weights = new();
        float total = 0.0f;
        foreach (LoreEntry entry in _entries)
        {
            if (entry.EarliestStage > stage || used.Contains(entry.Id))
            {
                continue;
            }
            if (entry.Circle >= 0 && entry.Circle != circle)
            {
                continue;
            }
            if (entry.ExclusiveGroup.Length > 0 && used.Contains("group:" + entry.ExclusiveGroup))
            {
                continue;
            }
            float weight = entry.Weight;
            // A text of this very circle is the most fitting thing to find here.
            if (entry.Circle >= 0)
            {
                weight *= 2.0f;
            }
            if (readBefore != null && readBefore(entry.Id))
            {
                weight *= readWeight;
            }
            fits.Add(entry);
            weights.Add(weight);
            total += weight;
        }
        if (fits.Count == 0 || total <= 0.0f)
        {
            return null;
        }
        SplitMix64 random = new(SplitMix64.Mix(seed ^ Salt, (ulong)stage));
        float roll = random.NextFloat() * total;
        for (int i = 0; i < fits.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0.0f)
            {
                return fits[i];
            }
        }
        return fits[fits.Count - 1];
    }

    /// <summary>Marks an entry (and its group) as used this run.</summary>
    public static void MarkUsed(LoreEntry entry, HashSet<string> used)
    {
        used.Add(entry.Id);
        if (entry.ExclusiveGroup.Length > 0)
        {
            used.Add("group:" + entry.ExclusiveGroup);
        }
    }
}
