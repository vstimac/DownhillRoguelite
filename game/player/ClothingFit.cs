using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The hand-made fits of every worn piece on every character (player/clothing_fit.tres), written by
/// the clothing fitting scene and applied by ClothingFitter as each piece is put on.
/// </summary>
[Tool]
[GlobalClass]
public partial class ClothingFit : Resource
{
    public const string DefaultPath = "res://player/clothing_fit.tres";

    [Export] public Godot.Collections.Array<ClothingFitEntry> Entries { get; set; } = new();

    private static ClothingFit? _loaded;

    /// <summary>The saved fits (kept loaded; empty when there is no file yet).</summary>
    public static ClothingFit Load()
    {
        if (_loaded != null)
        {
            return _loaded;
        }
        if (ResourceLoader.Exists(DefaultPath))
        {
            _loaded = ResourceLoader.Load(DefaultPath, "", ResourceLoader.CacheMode.Replace) as ClothingFit;
        }
        _loaded ??= new ClothingFit();
        return _loaded;
    }

    /// <summary>Forgets the loaded fits so the next Load reads the file again.</summary>
    public static void Reload()
    {
        _loaded = null;
    }

    /// <summary>Uses these fits instead of the file's until Reload (tests).</summary>
    public static void Use(ClothingFit fit)
    {
        _loaded = fit;
    }

    public ClothingFitEntry? Find(string characterId, string piece)
    {
        foreach (ClothingFitEntry entry in Entries)
        {
            if (entry != null && entry.CharacterId == characterId && entry.Piece == piece)
            {
                return entry;
            }
        }
        return null;
    }

    /// <summary>True if any piece of this character has a fit (lets unfitted characters skip the work).</summary>
    public bool Has(string characterId)
    {
        foreach (ClothingFitEntry entry in Entries)
        {
            if (entry != null && entry.CharacterId == characterId)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Stores a fit (an identity one removes the entry instead).</summary>
    public void Set(string characterId, string piece, Vector3 offset, Vector3 rotationDegrees, Vector3 scale, float tolerance)
    {
        ClothingFitEntry? entry = Find(characterId, piece);
        bool identity = offset.Length() < tolerance && rotationDegrees.Length() < tolerance && (scale - Vector3.One).Length() < tolerance;
        if (identity)
        {
            if (entry != null)
            {
                Entries.Remove(entry);
            }
            return;
        }
        if (entry == null)
        {
            entry = new ClothingFitEntry { CharacterId = characterId, Piece = piece };
            Entries.Add(entry);
        }
        entry.Offset = offset;
        entry.RotationDegrees = rotationDegrees;
        entry.Scale = scale;
    }
}
