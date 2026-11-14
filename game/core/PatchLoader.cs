using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// Small updates instead of whole downloads: in an exported game, every .pck in the "patches"
/// folder next to the executable is loaded over the game's own data, in name order, before the
/// first scene loads (this is the first autoload). A patch holds only the files that changed since
/// the full release (scripts/make_update.py builds it with Godot's --export-patch); the code itself
/// comes as a replaced DLL in the data folder. Does nothing in the editor.
/// </summary>
public partial class PatchLoader : Node
{
    public const string FolderName = "patches";

    /// <summary>The patch files loaded this session, in order.</summary>
    public static IReadOnlyList<string> Loaded => _loaded;

    private static readonly List<string> _loaded = new();

    public override void _EnterTree()
    {
        if (!OS.HasFeature("template"))
        {
            return;
        }
        string folder = OS.GetExecutablePath().GetBaseDir().PathJoin(FolderName);
        if (!DirAccess.DirExistsAbsolute(folder))
        {
            return;
        }
        List<string> files = new(DirAccess.GetFilesAt(folder));
        files.Sort(System.StringComparer.Ordinal);
        foreach (string file in files)
        {
            if (file.GetExtension().ToLowerInvariant() != "pck")
            {
                continue;
            }
            string path = folder.PathJoin(file);
            if (ProjectSettings.LoadResourcePack(path, true))
            {
                _loaded.Add(file);
                GD.Print("PatchLoader: loaded " + file);
            }
            else
            {
                GD.PushWarning("PatchLoader: could not load " + path);
            }
        }
        StartCoopIfTheBaseCannot();
    }

    /// <summary>
    /// A full release from before co-op has no NetSession autoload in its project settings, and a patch
    /// can't add one: autoloads start before patches mount. The code is new (an update replaces the DLL),
    /// so the session starts here instead, as /root/NetSession (the path co-op messages are sent to), once
    /// the root has finished taking its autoloads. A full release that has the autoload is left alone.
    /// </summary>
    private void StartCoopIfTheBaseCannot()
    {
        if (ProjectSettings.HasSetting("autoload/NetSession") || Network.NetSession.Instance != null)
        {
            return;
        }
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new Network.NetSession { Name = "NetSession" });
        GD.Print("PatchLoader: started co-op (this folder's full release is older than co-op)");
    }
}
