using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// Drives the sun, sky, ambient light and fog from the WorldClock. By day the sun arcs across the
/// sky; through dusk everything dims; at night only a faint moonlight is left and the fog closes in
/// to a few dozen metres, so the slope ahead can't be read. Night darkness scales with the
/// difficulty's NightHarshness (easy nights are brighter). Day values are read from the scene's
/// own light and environment at start, so each circle keeps its look. Weather (from
/// GameEvents.CurrentWeather) comes on top: fog and precipitation thicken the fog and dim the sun
/// under an overcast grey; heat tints the haze. The sky is either a ProceduralSkyMaterial or the
/// descent sky shader (environment/sky/descent_sky.gdshader: stars, sun and moon disc, clouds
/// that thicken with overcast and drift with the wind).
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
    [Export] public Vector2 MoonEnergy { get; set; } = new(0.12f, 0.32f);
    [Export] public float MoonElevation { get; set; } = 35.0f;
    /// <summary>Ambient light multiplier at night, harshest to mildest.</summary>
    [Export] public Vector2 NightAmbient { get; set; } = new(0.03f, 0.1f);
    /// <summary>Fog density at night, harshest to mildest.</summary>
    [Export] public Vector2 NightFogDensity { get; set; } = new(0.06f, 0.03f);
    [Export] public Color NightFogColor { get; set; } = new(0.03f, 0.035f, 0.05f);
    [Export] public Color NightSkyTop { get; set; } = new(0.01f, 0.012f, 0.025f);
    [Export] public Color NightSkyHorizon { get; set; } = new(0.04f, 0.045f, 0.06f);

    [ExportGroup("Weather")]
    /// <summary>Fog density added by full weather fog (by day; half of it at night, which is already thick).</summary>
    [Export] public float WeatherFogDensity { get; set; } = 0.05f;
    /// <summary>Fog density added by full rain or snow.</summary>
    [Export] public float PrecipitationFogDensity { get; set; } = 0.012f;
    /// <summary>How much a full overcast dims the sun.</summary>
    [Export] public float OvercastDimming { get; set; } = 0.6f;
    /// <summary>How much a full overcast dims ambient light.</summary>
    [Export] public float OvercastAmbientDimming { get; set; } = 0.3f;
    [Export] public Color OvercastColor { get; set; } = new(0.56f, 0.57f, 0.6f);
    [Export] public Color HeatHazeColor { get; set; } = new(0.8f, 0.6f, 0.42f);
    [Export] public Color HeatSunColor { get; set; } = new(1.0f, 0.8f, 0.6f);

    [ExportGroup("Sky shader")]
    /// <summary>Cloud drift per m/s of wind, in cloud-texture units per second.</summary>
    [Export] public float CloudDrift { get; set; } = 0.004f;
    /// <summary>Cloud drift with no wind, so the sky never stands still.</summary>
    [Export] public Vector2 CloudIdleDrift { get; set; } = new(0.003f, 0.001f);

    /// <summary>0 = full day look, 1 = full night look; for tests and other systems.</summary>
    public float Darkness { get; private set; }
    /// <summary>0 = clear sky, 1 = fully overcast (from weather).</summary>
    public float Overcast { get; private set; }

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
    private ShaderMaterial? _skyShader;
    private Vector2 _cloudOffset;
    private float _twinkle;

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
            _skyShader = environment.Sky?.SkyMaterial as ShaderMaterial;
            if (_sky != null)
            {
                _daySkyTop = _sky.SkyTopColor;
                _daySkyHorizon = _sky.SkyHorizonColor;
            }
            else if (_skyShader != null)
            {
                _daySkyTop = (Color)_skyShader.GetShaderParameter("top_color");
                _daySkyHorizon = (Color)_skyShader.GetShaderParameter("horizon_color");
            }
        }
        Apply();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Vector3 wind = GameEvents.CurrentWeather.Wind;
        _cloudOffset += (new Vector2(wind.X, wind.Z) * CloudDrift + CloudIdleDrift) * dt;
        _twinkle += dt;
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
        WeatherSnapshot weather = GameEvents.CurrentWeather;
        Overcast = Mathf.Clamp(Mathf.Max(weather.Fog, Mathf.Max(weather.Rain, weather.Snow)), 0.0f, 1.0f);
        float heat = Mathf.Clamp(weather.Heat, 0.0f, 1.0f);
        float weatherFog = weather.Fog * WeatherFogDensity + (weather.Rain + weather.Snow) * PrecipitationFogDensity;

        if (Sun != null)
        {
            float dayLength = _clock.SunsetHour - _clock.SunriseHour;
            float progress = Mathf.Clamp((_clock.TimeOfDay - _clock.SunriseHour) / dayLength, 0.0f, 1.0f);
            float sunElevation = Mathf.Max(4.0f, Mathf.Sin(progress * Mathf.Pi) * MaxElevation);
            float yaw = Mathf.Lerp(Azimuth.X, Azimuth.Y, progress);
            // Low sun warms toward dusk colour; at night the same light becomes faint moonlight.
            float lowSun = 1.0f - Mathf.Clamp(sunElevation / (MaxElevation * 0.5f), 0.0f, 1.0f);
            Color sunColor = _dayLightColor.Lerp(DuskColor, lowSun * 0.7f).Lerp(HeatSunColor, heat * 0.5f);
            float dayEnergy = _dayEnergy * (1.0f - OvercastDimming * Overcast);
            float moon = Mathf.Lerp(MoonEnergy.X, MoonEnergy.Y, mild);

            float elevation = Mathf.Lerp(sunElevation, MoonElevation, Darkness);
            Sun.Basis = new Basis(Vector3.Up, Mathf.DegToRad(yaw)) * new Basis(Vector3.Right, Mathf.DegToRad(-elevation));
            Sun.LightColor = sunColor.Lerp(MoonColor, Darkness);
            Sun.LightEnergy = Mathf.Lerp(dayEnergy, moon * (1.0f - OvercastDimming * Overcast * 0.5f), Darkness);
        }

        Godot.Environment? environment = World?.Environment;
        if (environment != null)
        {
            float ambient = _dayAmbient * (1.0f - OvercastAmbientDimming * Overcast);
            environment.AmbientLightEnergy = Mathf.Lerp(ambient, ambient * Mathf.Lerp(NightAmbient.X, NightAmbient.Y, mild), Darkness);
            float dayFog = _dayFogDensity + weatherFog;
            float nightFog = Mathf.Lerp(NightFogDensity.X, NightFogDensity.Y, mild) + weatherFog * 0.5f;
            environment.FogDensity = Mathf.Lerp(dayFog, nightFog, Darkness);
            Color dayFogColor = _dayFogColor.Lerp(OvercastColor, Overcast * 0.6f).Lerp(HeatHazeColor, heat * 0.5f);
            environment.FogLightColor = dayFogColor.Lerp(NightFogColor, Darkness);
        }
        Color top = _daySkyTop.Lerp(OvercastColor.Darkened(0.2f), Overcast * 0.7f).Lerp(HeatHazeColor, heat * 0.3f);
        Color horizon = _daySkyHorizon.Lerp(OvercastColor, Overcast * 0.7f).Lerp(HeatHazeColor, heat * 0.4f);
        if (_sky != null)
        {
            _sky.SkyTopColor = top.Lerp(NightSkyTop, Darkness);
            _sky.SkyHorizonColor = horizon.Lerp(NightSkyHorizon, Darkness);
            _sky.GroundHorizonColor = horizon.Lerp(NightSkyHorizon, Darkness);
        }
        else if (_skyShader != null)
        {
            _skyShader.SetShaderParameter("top_color", top.Lerp(NightSkyTop, Darkness));
            _skyShader.SetShaderParameter("horizon_color", horizon.Lerp(NightSkyHorizon, Darkness));
            _skyShader.SetShaderParameter("darkness", Darkness);
            _skyShader.SetShaderParameter("overcast", Overcast);
            _skyShader.SetShaderParameter("cloud_offset", _cloudOffset);
            _skyShader.SetShaderParameter("twinkle_time", _twinkle);
        }
    }
}
