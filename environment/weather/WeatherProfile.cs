using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Environment;

/// <summary>
/// The weather of one circle: how likely each kind of front is, how strong fronts get and how hard
/// the wind blows. See docs/survival-loop.md (M8) and the circle table in docs/narrative.md.
/// </summary>
[GlobalClass]
public partial class WeatherProfile : Resource
{
    [Export] public string DisplayName { get; set; } = "";

    [ExportGroup("Front chances (relative weights)")]
    [Export] public float Clear { get; set; } = 1.0f;
    [Export] public float Fog { get; set; }
    [Export] public float Rain { get; set; }
    [Export] public float Snow { get; set; }
    [Export] public float Wind { get; set; }
    [Export] public float Heat { get; set; }

    [ExportGroup("Strength")]
    /// <summary>Weakest and strongest front intensity, 0 to 1.</summary>
    [Export] public Vector2 Intensity { get; set; } = new(0.4f, 1.0f);
    /// <summary>Wind speed of a full-strength wind front, in m/s.</summary>
    [Export] public float WindSpeed { get; set; } = 11.0f;
    /// <summary>Wind that is always there, whatever the front, in m/s.</summary>
    [Export] public float Breeze { get; set; } = 1.5f;
    /// <summary>How much fog, rain, snow and wind grow at full night (0.4 = 40 % stronger).</summary>
    [Export] public float NightWorsening { get; set; } = 0.4f;

    public float Weight(WeatherKind kind)
    {
        if (kind == WeatherKind.Fog)
        {
            return Fog;
        }
        if (kind == WeatherKind.Rain)
        {
            return Rain;
        }
        if (kind == WeatherKind.Snow)
        {
            return Snow;
        }
        if (kind == WeatherKind.Wind)
        {
            return Wind;
        }
        if (kind == WeatherKind.Heat)
        {
            return Heat;
        }
        return Clear;
    }
}
