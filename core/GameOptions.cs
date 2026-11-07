using System;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// The player's options (pause menu → Options), kept in user://options.cfg across runs and builds.
/// Systems read the values and listen to Changed; nothing here touches a system directly.
/// </summary>
public static class GameOptions
{
    private const string FilePath = "user://options.cfg";
    private const string Section = "options";

    /// <summary>Raised after any option changes (and once after loading).</summary>
    public static event Action? Changed;

    /// <summary>Single player: the world stops while the inventory page or the pack is open.</summary>
    public static bool PauseWhileOpen { get; private set; } = true;
    /// <summary>The flashing list of low stats in the top-left corner.</summary>
    public static bool LowStatAlerts { get; private set; } = true;
    public static StatStyle Stats { get; private set; } = StatStyle.Dials;
    /// <summary>The mouse wheel steps through the hotbar (otherwise only the number keys do).</summary>
    public static bool WheelSelectsHotbar { get; private set; } = true;
    /// <summary>Camera field of view, in degrees (60–120).</summary>
    public static float FieldOfView { get; private set; } = 75.0f;
    /// <summary>The dotted route lines on the ground toward the next station.</summary>
    public static bool PathLines { get; private set; } = true;
    public static ColourFilter Filter { get; private set; } = ColourFilter.None;
    /// <summary>Size of the whole interface (0.75–1.5).</summary>
    public static float UiScale { get; private set; } = 1.0f;
    /// <summary>Text size on top of the interface scale (0.8–1.4).</summary>
    public static float TextScale { get; private set; } = 1.0f;

    private static bool _loaded;

    /// <summary>Reads the saved options (once; later calls do nothing).</summary>
    public static void Load()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        ConfigFile file = new();
        if (file.Load(FilePath) != Error.Ok)
        {
            return;
        }
        PauseWhileOpen = (bool)file.GetValue(Section, "pause_while_open", PauseWhileOpen);
        LowStatAlerts = (bool)file.GetValue(Section, "low_stat_alerts", LowStatAlerts);
        Stats = (StatStyle)(int)file.GetValue(Section, "stats", (int)Stats);
        WheelSelectsHotbar = (bool)file.GetValue(Section, "wheel_hotbar", WheelSelectsHotbar);
        FieldOfView = Mathf.Clamp((float)file.GetValue(Section, "fov", FieldOfView), 60.0f, 120.0f);
        PathLines = (bool)file.GetValue(Section, "path_lines", PathLines);
        Filter = (ColourFilter)(int)file.GetValue(Section, "colour_filter", (int)Filter);
        UiScale = Mathf.Clamp((float)file.GetValue(Section, "ui_scale", UiScale), 0.75f, 1.5f);
        TextScale = Mathf.Clamp((float)file.GetValue(Section, "text_scale", TextScale), 0.8f, 1.4f);
        Changed?.Invoke();
    }

    public static void SetPauseWhileOpen(bool on) => Set(() => PauseWhileOpen = on);
    public static void SetLowStatAlerts(bool on) => Set(() => LowStatAlerts = on);
    public static void SetStats(StatStyle style) => Set(() => Stats = style);
    public static void SetWheelSelectsHotbar(bool on) => Set(() => WheelSelectsHotbar = on);
    public static void SetFieldOfView(float degrees) => Set(() => FieldOfView = Mathf.Clamp(degrees, 60.0f, 120.0f));
    public static void SetPathLines(bool on) => Set(() => PathLines = on);
    public static void SetFilter(ColourFilter filter) => Set(() => Filter = filter);
    public static void SetUiScale(float scale) => Set(() => UiScale = Mathf.Clamp(scale, 0.75f, 1.5f));
    public static void SetTextScale(float scale) => Set(() => TextScale = Mathf.Clamp(scale, 0.8f, 1.4f));

    private static void Set(Action change)
    {
        Load();
        change();
        Save();
        Changed?.Invoke();
    }

    private static void Save()
    {
        ConfigFile file = new();
        file.SetValue(Section, "pause_while_open", PauseWhileOpen);
        file.SetValue(Section, "low_stat_alerts", LowStatAlerts);
        file.SetValue(Section, "stats", (int)Stats);
        file.SetValue(Section, "wheel_hotbar", WheelSelectsHotbar);
        file.SetValue(Section, "fov", FieldOfView);
        file.SetValue(Section, "path_lines", PathLines);
        file.SetValue(Section, "colour_filter", (int)Filter);
        file.SetValue(Section, "ui_scale", UiScale);
        file.SetValue(Section, "text_scale", TextScale);
        file.Save(FilePath);
    }
}
