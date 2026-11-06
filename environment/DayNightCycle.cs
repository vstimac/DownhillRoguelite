using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// Drives the sun, sky, ambient light and fog from the WorldClock. By day the sun arcs across the
/// sky; through dusk everything dims; at night only a faint moonlight is left and the fog closes in
/// to a few dozen metres, so the slope ahead can't be read. Night darkness scales with the
/// difficulty's NightHarshness (easy nights are brighter). Day values are read from the scene's
/// own light and environment at start, so each circle keeps its look.
/// </summary>
public partial class DayNightCycle : Node
{
    [Export] public DirectionalLight3D? Sun { get; set; }
    [Export] public WorldEnvironment? World { get; set; }

    [ExportGroup("Sun path")]
    /// <summary>Highest the sun climbs at midday, in degrees.</summary>
    [Export] public float MaxElevation { get; set; } = 55.0f;
    /// <summary>Sun heading at sunrise and sunset, in degrees around Y.</summary>
    [Export] public Vector2 Azimuth { get; set; } = new(100.0f, -100.0f);
    [Export] public Color DuskColor { get; set; } = new(1.0f, 0.62f, 0.4f);

    [ExportGroup("Night")]
    [Export] public Color MoonColor { get; set; } = new(0.55f, 0.65f, 0.9f);
    /// <summary>Moonlight energy on the harshest nights and on the mildest.</summary>
    [Export] public Vector2 MoonEnergy { get; set; } = new(0.03f, 0.12f);
    [Export] public float MoonElevation { get; set; } = 35.0f;
    /// <summary>Ambient light multiplier at night, harshest to mildest.</summary>
    [Export] public Vector2 NightAmbient { get; set; } = new(0.04f, 0.15f);
    /// <summary>Fog density at night, harshest to mildest.</summary>
    [Export] public Vector2 NightFogDensity { get; set; } = new(0.06f, 0.03f);
    [Export] public Color NightFogColor { get; set; } = new(0.03f, 0.035f, 0.05f);
    [Export] public Color NightSkyTop { get; set; } = new(0.01f, 0.012f, 0.025f);
    [Export] public Color NightSkyHorizon { get; set; } = new(0.04f, 0.045f, 0.06f);

    /// <summary>0 = full day look, 1 = full night look; for tests and other systems.</summary>
    public float Darkness { get; private set; }

    private WorldClock? _clock;
    private RunState? _run;
    private float _dayEnergy = 1.0f;
    private Color _dayLightColor = Colors.White;
    private float _dayAmbient = 1.0f;
    private float _dayFogDensity = 0.01f;
    private Color _dayFogColor = Colors.Gray;
    private Color _daySkyTop = Colors.SkyBlue;
    private Color _daySkyHorizon = Colors.LightGray;
    private ProceduralSkyMaterial? _sky;

    public override void _Ready()
    {
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        _run = GetNodeOrNull<RunState>("/root/RunState");
        if (Sun != null)
        {
            _dayEnergy = Sun.LightEnergy;
            _dayLightColor = Sun.LightColor;
        }
        Godot.Environment? environment = World?.Environment;
        if (environment != null)
        {
            _dayAmbient = environment.AmbientLightEnergy;
            _dayFogDensity = environment.FogDensity;
            _dayFogColor = environment.FogLightColor;
            _sky = environment.Sky?.SkyMaterial as ProceduralSkyMaterial;
            if (_sky != null)
            {
                _daySkyTop = _sky.SkyTopColor;
                _daySkyHorizon = _sky.SkyHorizonColor;
            }
        }
        Apply();
    }

    public override void _Process(double delta)
    {
        Apply();
    }

    private void Apply()
    {
        if (_clock == null)
        {
            return;
        }

        float daylight = _clock.Daylight;
        float harshness = 1.0f;
        if (_run != null)
        {
            harshness = _run.DifficultySettings.NightHarshness;
        }
        Darkness = 1.0f - daylight;
        float mild = 1.0f - harshness;

        if (Sun != null)
        {
            float dayLength = _clock.SunsetHour - _clock.SunriseHour;
            float progress = Mathf.Clamp((_clock.TimeOfDay - _clock.SunriseHour) / dayLength, 0.0f, 1.0f);
            float sunElevation = Mathf.Max(4.0f, Mathf.Sin(progress * Mathf.Pi) * MaxElevation);
            float yaw = Mathf.Lerp(Azimuth.X, Azimuth.Y, progress);
            // Low sun warms toward dusk colour; at night the same light becomes faint moonlight.
            float lowSun = 1.0f - Mathf.Clamp(sunElevation / (MaxElevation * 0.5f), 0.0f, 1.0f);
            Color sunColor = _dayLightColor.Lerp(DuskColor, lowSun * 0.7f);
            float moon = Mathf.Lerp(MoonEnergy.X, MoonEnergy.Y, mild);

            float elevation = Mathf.Lerp(sunElevation, MoonElevation, Darkness);
            Sun.Basis = new Basis(Vector3.Up, Mathf.DegToRad(yaw)) * new Basis(Vector3.Right, Mathf.DegToRad(-elevation));
            Sun.LightColor = sunColor.Lerp(MoonColor, Darkness);
            Sun.LightEnergy = Mathf.Lerp(_dayEnergy, moon, Darkness);
        }

        Godot.Environment? environment = World?.Environment;
        if (environment != null)
        {
            environment.AmbientLightEnergy = Mathf.Lerp(_dayAmbient, _dayAmbient * Mathf.Lerp(NightAmbient.X, NightAmbient.Y, mild), Darkness);
            environment.FogDensity = Mathf.Lerp(_dayFogDensity, Mathf.Lerp(NightFogDensity.X, NightFogDensity.Y, mild), Darkness);
            environment.FogLightColor = _dayFogColor.Lerp(NightFogColor, Darkness);
        }
        if (_sky != null)
        {
            _sky.SkyTopColor = _daySkyTop.Lerp(NightSkyTop, Darkness);
            _sky.SkyHorizonColor = _daySkyHorizon.Lerp(NightSkyHorizon, Darkness);
            _sky.GroundHorizonColor = _daySkyHorizon.Lerp(NightSkyHorizon, Darkness);
        }
    }
}
