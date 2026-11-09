using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// Applies the options that belong to the whole window: the interface size (the window's content
/// scale, so menus and HUD grow together for 2K/4K screens), the in-world path lines and overlays,
/// the colour-blind filter layer, the volume of each audio bus, and the window itself (mode, size,
/// vsync, frame cap). Other options are read where they act (camera, hotbar, dials, subtitles).
/// The start screen holds the sound down while it is up through <see cref="Hush"/>.
/// </summary>
public partial class OptionsApplier : Node
{
    private static readonly string[] Buses = { "Master", "Music", "Voice", "Ambience" };

    [ExportGroup("Quality presets (Low, Medium, High)")]
    /// <summary>3D render resolution as a share of the window (upscaled with FSR below 1).</summary>
    [Export] public Vector3 RenderScale { get; set; } = new(0.67f, 0.85f, 1.0f);
    /// <summary>Sun shadow map size, in pixels.</summary>
    [Export] public Vector3I ShadowSize { get; set; } = new(1024, 2048, 4096);
    /// <summary>How far the sun's shadows reach, in m.</summary>
    [Export] public Vector3 ShadowDistance { get; set; } = new(40.0f, 55.0f, 70.0f);
    /// <summary>Shadow edge softness (RenderingServer.ShadowQuality: 0 hard … 5 ultra).</summary>
    [Export] public Vector3I ShadowSoftness { get; set; } = new(0, 1, 2);
    /// <summary>Mesh detail threshold (higher drops detail sooner).</summary>
    [Export] public Vector3 LodThreshold { get; set; } = new(4.0f, 2.0f, 1.0f);
    /// <summary>Ambient occlusion (SSAO) on Low, Medium and High.</summary>
    [Export] public Vector3I AmbientOcclusion { get; set; } = new(0, 1, 1);

    /// <summary>
    /// 0–1 on top of the master volume: the start screen keeps the world silent (0) and fades it in
    /// after Start. Changing a volume in Options never overrides it.
    /// </summary>
    public static float Hush { get; private set; } = 1.0f;

    private static DisplayMode? _appliedMode;
    private static Vector2I _appliedSize = new(-1, -1);

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GameOptions.Load();
        AddChild(new ColourFilterLayer { Name = "ColourFilter" });
        GameOptions.Changed += Apply;
        GetTree().Root.ContentScaleFactor = GameOptions.UiScale;
        ApplyVolumes();
        // After the scene is in, so its sun and environment are there to set.
        Callable.From(ApplyQuality).CallDeferred();
        // Render captures and headless runs keep the window they were given on the command line.
        if (OwnsWindow())
        {
            ApplyWindow();
        }
    }

    public override void _ExitTree()
    {
        GameOptions.Changed -= Apply;
    }

    /// <summary>Sets the hush (0 silent – 1 full) and applies it at once.</summary>
    public static void SetHush(float amount)
    {
        Hush = Mathf.Clamp(amount, 0.0f, 1.0f);
        ApplyVolumes();
    }

    /// <summary>On a change in Options (at start the lines follow DebugDraw3D's own start-up and --promo).</summary>
    private void Apply()
    {
        Window root = GetTree().Root;
        root.ContentScaleFactor = GameOptions.UiScale;
        Debug.DebugDraw3D.SetEnabled(GameOptions.PathLines);
        ApplyVolumes();
        ApplyQuality();
        if (OwnsWindow())
        {
            ApplyWindow();
        }
    }

    /// <summary>
    /// The graphics preset: render scale (FSR below full), the sun's shadow size, reach and softness,
    /// mesh detail, and ambient occlusion. The terrain reads the preset for its plant view distances
    /// (newly built ground), so Low also thins the far forest.
    /// </summary>
    private void ApplyQuality()
    {
        if (!IsInsideTree())
        {
            return;
        }
        int q = (int)GameOptions.Quality;
        Viewport root = GetTree().Root;
        float scale = RenderScale[q];
        root.Scaling3DScale = scale;
        if (scale < 0.999f)
        {
            root.Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr;
        }
        else
        {
            root.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
        }
        root.MeshLodThreshold = LodThreshold[q];
        RenderingServer.DirectionalShadowAtlasSetSize(ShadowSize[q], true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality((RenderingServer.ShadowQuality)ShadowSoftness[q]);
        foreach (Node node in GetTree().Root.FindChildren("*", "DirectionalLight3D", true, false))
        {
            if (node is DirectionalLight3D sun && sun.ShadowEnabled)
            {
                sun.DirectionalShadowMaxDistance = ShadowDistance[q];
            }
        }
        Godot.Environment? environment = root.World3D?.Environment;
        if (environment == null)
        {
            foreach (Node node in GetTree().Root.FindChildren("*", "WorldEnvironment", true, false))
            {
                environment = (node as WorldEnvironment)?.Environment;
                break;
            }
        }
        if (environment != null)
        {
            environment.SsaoEnabled = AmbientOcclusion[q] > 0;
        }
    }

    /// <summary>The volume a bus should play at, 0–1 (the master includes the hush).</summary>
    public static float BusVolume(string bus)
    {
        if (bus == "Master")
        {
            return GameOptions.MasterVolume * Hush;
        }
        if (bus == "Music")
        {
            return GameOptions.MusicVolume;
        }
        if (bus == "Voice")
        {
            return GameOptions.VoiceVolume;
        }
        if (bus == "Ambience")
        {
            return GameOptions.AmbienceVolume;
        }
        return 1.0f;
    }

    private static void ApplyVolumes()
    {
        foreach (string bus in Buses)
        {
            int index = AudioServer.GetBusIndex(bus);
            if (index < 0)
            {
                continue;
            }
            float volume = BusVolume(bus);
            AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(volume, 0.0001f)));
            AudioServer.SetBusMute(index, volume <= 0.001f);
        }
    }

    private static bool OwnsWindow()
    {
        if (DisplayServer.GetName() == "headless")
        {
            return false;
        }
        string[] args = OS.GetCmdlineArgs();
        return System.Array.IndexOf(args, "--write-movie") < 0 && System.Array.IndexOf(args, "--resolution") < 0;
    }

    /// <summary>Window mode and size change only when they differ from what was applied last, so other options don't jolt the window.</summary>
    private static void ApplyWindow()
    {
        DisplayServer.WindowSetVsyncMode(VSyncMode());
        Engine.MaxFps = GameOptions.MaxFps;
        if (_appliedMode == GameOptions.Display && _appliedSize == GameOptions.WindowSize)
        {
            return;
        }
        bool first = _appliedMode == null;
        _appliedMode = GameOptions.Display;
        _appliedSize = GameOptions.WindowSize;
        if (GameOptions.Display == DisplayMode.Borderless)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            return;
        }
        if (GameOptions.Display == DisplayMode.Fullscreen)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
            return;
        }
        // Windowed: at start-up a default size leaves the window as the project opened it.
        if (first && GameOptions.WindowSize == Vector2I.Zero)
        {
            return;
        }
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        }
        if (GameOptions.WindowSize != Vector2I.Zero)
        {
            int screen = DisplayServer.WindowGetCurrentScreen();
            Vector2I usable = DisplayServer.ScreenGetUsableRect(screen).Size;
            Vector2I size = new(Mathf.Min(GameOptions.WindowSize.X, usable.X), Mathf.Min(GameOptions.WindowSize.Y, usable.Y));
            DisplayServer.WindowSetSize(size);
            Rect2I area = DisplayServer.ScreenGetUsableRect(screen);
            DisplayServer.WindowSetPosition(area.Position + (area.Size - size) / 2);
        }
    }

    private static DisplayServer.VSyncMode VSyncMode()
    {
        if (GameOptions.VSync)
        {
            return DisplayServer.VSyncMode.Enabled;
        }
        return DisplayServer.VSyncMode.Disabled;
    }
}
