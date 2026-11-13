using System;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// The player's options (main menu or pause menu → Options), kept in user://options.cfg across runs
/// and builds. Systems read the values and listen to Changed; nothing here touches a system directly
/// (ui/OptionsApplier applies the window, audio and interface ones).
/// </summary>
public static class GameOptions
{
    public const string DefaultPath = "user://options.cfg";
    private const string Section = "options";

    /// <summary>Raised after any option changes (and once after loading).</summary>
    public static event Action? Changed;

    static GameOptions()
    {
        // Whoever asks first gets the saved options: a system reading Quality in its _Ready before
        // anyone had loaded them used to see the defaults (High, so the fog came back on Low).
        // Headless runs (the tests, CI) keep the defaults, as RunState does.
        if (DisplayServer.GetName() != "headless")
        {
            Load();
        }
    }

    /// <summary>Single player: the world stops while the inventory page or the pack is open.</summary>
    public static bool PauseWhileOpen { get; private set; } = true;
    /// <summary>The flashing list of low stats in the top-left corner.</summary>
    public static bool LowStatAlerts { get; private set; } = true;
    public static StatStyle Stats { get; private set; } = StatStyle.Dials;
    /// <summary>The mouse wheel steps through the hotbar (otherwise only the number keys do).</summary>
    public static bool WheelSelectsHotbar { get; private set; } = true;
    /// <summary>Camera field of view, in degrees (60–120).</summary>
    public static float FieldOfView { get; private set; } = 75.0f;
    /// <summary>The route and debug lines drawn in the world (DebugDraw3D); on by default only in development builds.</summary>
    public static bool PathLines { get; private set; } = OS.IsDebugBuild();
    public static ColourFilter Filter { get; private set; } = ColourFilter.None;
    /// <summary>Size of the whole interface and its text, for 2K/4K screens (0.75–1.5).</summary>
    public static float UiScale { get; private set; } = 1.0f;

    /// <summary>Volume of each audio bus, 0–1 (linear; 0 mutes it).</summary>
    public static float MasterVolume { get; private set; } = 1.0f;
    public static float MusicVolume { get; private set; } = 0.8f;
    public static float VoiceVolume { get; private set; } = 1.0f;
    /// <summary>Weather, wind and fire (the "Ambience" bus).</summary>
    public static float AmbienceVolume { get; private set; } = 1.0f;

    public static DisplayMode Display { get; private set; } = DisplayMode.Windowed;
    /// <summary>Window size when windowed; (0, 0) keeps the project's default size.</summary>
    public static Vector2I WindowSize { get; private set; } = Vector2I.Zero;
    public static bool VSync { get; private set; } = true;
    /// <summary>The low fog that flows over the ground (Low quality turns it off regardless).</summary>
    public static bool GroundFog { get; private set; } = true;
    /// <summary>Graphics preset: Low, Medium or High (OptionsApplier and the terrain read it).</summary>
    public static GraphicsQuality Quality { get; private set; } = GraphicsQuality.High;
    /// <summary>Frame-rate cap; 0 = none.</summary>
    public static int MaxFps { get; private set; }

    /// <summary>Multiplier on the camera's right-stick look speed (0.25–3).</summary>
    public static float LookSensitivity { get; private set; } = 1.0f;
    /// <summary>Linear multiplier on the camera's mouse look (0.1–5; 1 is the game's base speed).</summary>
    public static float MouseSensitivity { get; private set; } = 1.0f;
    /// <summary>Mouse look is eased over a few hundredths of a second (off: every count turns at once).</summary>
    public static bool MouseSmoothing { get; private set; }
    /// <summary>In-game mouse acceleration, 0 (off) to 1: faster flicks turn further per count.</summary>
    public static float MouseAcceleration { get; private set; }
    /// <summary>Raw mouse counts, bypassing the desktop's pointer speed and acceleration (off: look follows them, Windows only).</summary>
    public static bool RawInput { get; private set; } = true;
    /// <summary>Moving the mouse up looks down.</summary>
    public static bool InvertLook { get; private set; }
    /// <summary>Spoken lines are captioned on screen.</summary>
    public static bool Subtitles { get; private set; } = true;
    /// <summary>The title screen asks GitHub for a newer release and offers to update (core/update/UpdateCheck).</summary>
    public static bool CheckForUpdates { get; private set; } = true;
    /// <summary>Health as small hearts beside the carrier when it changes or runs low (ui/hud/SideHearts).</summary>
    public static bool SideHearts { get; private set; } = true;
    /// <summary>Language of dialogue text (subtitles and conversation answers only); voices are never changed.</summary>
    public static DialogueLanguage DialogueLanguage { get; private set; } = DialogueText.SystemDefault();

    /// <summary>The chosen character's id (CarrierRoster); an unknown or missing one falls back to the first.</summary>
    public static string Character { get; private set; } = "modular";
    public static Color BaseShirtColour { get; private set; } = new(0.28f, 0.35f, 0.43f);
    public static Color BasePackColour { get; private set; } = new(0.25f, 0.34f, 0.22f);

    private static bool _loaded;
    private static string _path = DefaultPath;

    /// <summary>Tests: read and save another file from now on (so the player's own options stay untouched), starting from defaults.</summary>
    public static void UseFile(string path)
    {
        _path = path;
        _loaded = false;
        ResetToDefaults();
        Load();
        Changed?.Invoke();
    }

    /// <summary>Every option back to its default (not saved until the next change).</summary>
    public static void ResetToDefaults()
    {
        PauseWhileOpen = true;
        LowStatAlerts = true;
        Stats = StatStyle.Dials;
        WheelSelectsHotbar = true;
        FieldOfView = 75.0f;
        PathLines = OS.IsDebugBuild();
        Filter = ColourFilter.None;
        UiScale = 1.0f;
        MasterVolume = 1.0f;
        MusicVolume = 0.8f;
        VoiceVolume = 1.0f;
        AmbienceVolume = 1.0f;
        Display = DisplayMode.Windowed;
        WindowSize = Vector2I.Zero;
        VSync = true;
        GroundFog = true;
        Quality = GraphicsQuality.High;
        MaxFps = 0;
        LookSensitivity = 1.0f;
        MouseSensitivity = 1.0f;
        MouseSmoothing = false;
        MouseAcceleration = 0.0f;
        RawInput = true;
        InvertLook = false;
        Subtitles = true;
        CheckForUpdates = true;
        SideHearts = true;
        DialogueLanguage = DialogueText.SystemDefault();
        Character = "modular";
        BaseShirtColour = new Color(0.28f, 0.35f, 0.43f);
        BasePackColour = new Color(0.25f, 0.34f, 0.22f);
    }

    /// <summary>Reads the saved options (once; later calls do nothing).</summary>
    public static void Load()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        ConfigFile file = new();
        if (file.Load(_path) != Error.Ok)
        {
            // First launch: on integrated graphics start on Low (Options can raise it).
            if (RenderingServer.GetVideoAdapterType() == RenderingDevice.DeviceType.IntegratedGpu)
            {
                Quality = GraphicsQuality.Low;
            }
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
        Character = (string)file.GetValue(Section, "character", Character);
        BaseShirtColour = (Color)file.GetValue(Section, "base_shirt_colour", BaseShirtColour);
        BasePackColour = (Color)file.GetValue(Section, "base_pack_colour", BasePackColour);
        MasterVolume = Volume(file, "volume_master", MasterVolume);
        MusicVolume = Volume(file, "volume_music", MusicVolume);
        VoiceVolume = Volume(file, "volume_voice", VoiceVolume);
        AmbienceVolume = Volume(file, "volume_ambience", AmbienceVolume);
        Display = (DisplayMode)Mathf.Clamp((int)file.GetValue(Section, "display", (int)Display), 0, 2);
        WindowSize = new Vector2I(Mathf.Max(0, (int)file.GetValue(Section, "window_width", 0)), Mathf.Max(0, (int)file.GetValue(Section, "window_height", 0)));
        VSync = (bool)file.GetValue(Section, "vsync", VSync);
        GroundFog = (bool)file.GetValue(Section, "ground_fog", GroundFog);
        Quality = (GraphicsQuality)Mathf.Clamp((int)file.GetValue(Section, "graphics_quality", (int)Quality), 0, 2);
        MaxFps = Mathf.Max(0, (int)file.GetValue(Section, "max_fps", MaxFps));
        LookSensitivity = Mathf.Clamp((float)file.GetValue(Section, "look_sensitivity", LookSensitivity), 0.25f, 3.0f);
        // Before Beta 2.4 one look sensitivity served mouse and stick: it carries over to the mouse.
        MouseSensitivity = Mathf.Clamp((float)file.GetValue(Section, "mouse_sensitivity", LookSensitivity), 0.1f, 5.0f);
        MouseSmoothing = (bool)file.GetValue(Section, "mouse_smoothing", MouseSmoothing);
        MouseAcceleration = Mathf.Clamp((float)file.GetValue(Section, "mouse_acceleration", MouseAcceleration), 0.0f, 1.0f);
        RawInput = (bool)file.GetValue(Section, "raw_input", RawInput);
        InvertLook = (bool)file.GetValue(Section, "invert_look", InvertLook);
        Subtitles = (bool)file.GetValue(Section, "subtitles", Subtitles);
        CheckForUpdates = (bool)file.GetValue(Section, "check_updates", CheckForUpdates);
        SideHearts = (bool)file.GetValue(Section, "side_hearts", SideHearts);
        DialogueLanguage = (DialogueLanguage)Mathf.Clamp((int)file.GetValue(Section, "dialogue_language", (int)DialogueLanguage), 0, 1);
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
    public static void SetCharacter(string id) => Set(() => Character = id);
    public static void SetBaseOutfitColours(Color shirt, Color pack) => Set(() => { BaseShirtColour = shirt; BasePackColour = pack; });
    public static void SetMasterVolume(float volume) => Set(() => MasterVolume = Mathf.Clamp(volume, 0.0f, 1.0f));
    public static void SetMusicVolume(float volume) => Set(() => MusicVolume = Mathf.Clamp(volume, 0.0f, 1.0f));
    public static void SetVoiceVolume(float volume) => Set(() => VoiceVolume = Mathf.Clamp(volume, 0.0f, 1.0f));
    public static void SetAmbienceVolume(float volume) => Set(() => AmbienceVolume = Mathf.Clamp(volume, 0.0f, 1.0f));
    public static void SetDisplay(DisplayMode mode) => Set(() => Display = mode);
    public static void SetWindowSize(Vector2I size) => Set(() => WindowSize = new Vector2I(Mathf.Max(0, size.X), Mathf.Max(0, size.Y)));
    public static void SetVSync(bool on) => Set(() => VSync = on);
    public static void SetGroundFog(bool on) => Set(() => GroundFog = on);
    public static void SetQuality(GraphicsQuality quality) => Set(() => Quality = quality);
    public static void SetMaxFps(int fps) => Set(() => MaxFps = Mathf.Max(0, fps));
    public static void SetLookSensitivity(float multiplier) => Set(() => LookSensitivity = Mathf.Clamp(multiplier, 0.25f, 3.0f));
    public static void SetMouseSensitivity(float multiplier) => Set(() => MouseSensitivity = Mathf.Clamp(multiplier, 0.1f, 5.0f));
    public static void SetMouseSmoothing(bool on) => Set(() => MouseSmoothing = on);
    public static void SetMouseAcceleration(float amount) => Set(() => MouseAcceleration = Mathf.Clamp(amount, 0.0f, 1.0f));
    public static void SetRawInput(bool on) => Set(() => RawInput = on);
    public static void SetInvertLook(bool on) => Set(() => InvertLook = on);
    public static void SetSubtitles(bool on) => Set(() => Subtitles = on);
    public static void SetCheckForUpdates(bool on) => Set(() => CheckForUpdates = on);
    public static void SetSideHearts(bool on) => Set(() => SideHearts = on);
    public static void SetDialogueLanguage(DialogueLanguage language) => Set(() => DialogueLanguage = language);

    /// <summary>Every option back to its default and saved, keeping the chosen character (Options → Reset all).</summary>
    public static void RestoreDefaults()
    {
        string character = Character;
        Color shirt = BaseShirtColour, pack = BasePackColour;
        Set(() =>
        {
            ResetToDefaults();
            Character = character;
            BaseShirtColour = shirt; BasePackColour = pack;
        });
    }

    private static float Volume(ConfigFile file, string key, float fallback)
    {
        return Mathf.Clamp((float)file.GetValue(Section, key, fallback), 0.0f, 1.0f);
    }

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
        file.SetValue(Section, "character", Character);
        file.SetValue(Section, "base_shirt_colour", BaseShirtColour);
        file.SetValue(Section, "base_pack_colour", BasePackColour);
        file.SetValue(Section, "volume_master", MasterVolume);
        file.SetValue(Section, "volume_music", MusicVolume);
        file.SetValue(Section, "volume_voice", VoiceVolume);
        file.SetValue(Section, "volume_ambience", AmbienceVolume);
        file.SetValue(Section, "display", (int)Display);
        file.SetValue(Section, "window_width", WindowSize.X);
        file.SetValue(Section, "window_height", WindowSize.Y);
        file.SetValue(Section, "vsync", VSync);
        file.SetValue(Section, "ground_fog", GroundFog);
        file.SetValue(Section, "graphics_quality", (int)Quality);
        file.SetValue(Section, "max_fps", MaxFps);
        file.SetValue(Section, "look_sensitivity", LookSensitivity);
        file.SetValue(Section, "mouse_sensitivity", MouseSensitivity);
        file.SetValue(Section, "mouse_smoothing", MouseSmoothing);
        file.SetValue(Section, "mouse_acceleration", MouseAcceleration);
        file.SetValue(Section, "raw_input", RawInput);
        file.SetValue(Section, "invert_look", InvertLook);
        file.SetValue(Section, "subtitles", Subtitles);
        file.SetValue(Section, "check_updates", CheckForUpdates);
        file.SetValue(Section, "side_hearts", SideHearts);
        file.SetValue(Section, "dialogue_language", (int)DialogueLanguage);
        file.Save(_path);
    }
}
