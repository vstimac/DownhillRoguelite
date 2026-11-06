using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Map;

/// <summary>
/// A stone cairn on the route. The one just ahead of the carrier shows the estimate to the next
/// station in the world: distance, how long including rests, the stamina it costs against what's
/// left, and the arrival time against nightfall, coloured by how safe it looks.
/// </summary>
public partial class WaypointCairn : Node3D
{
    [Export] public Color StoneColor { get; set; } = new(0.48f, 0.47f, 0.45f);
    [Export] public Color SafeColor { get; set; } = new(0.6f, 1.0f, 0.6f);
    [Export] public Color TightColor { get; set; } = new(1.0f, 0.82f, 0.35f);
    [Export] public Color LateColor { get; set; } = new(1.0f, 0.45f, 0.4f);
    [Export] public int LabelFontSize { get; set; } = 48;
    [Export] public float LabelPixelSize { get; set; } = 0.006f;
    [Export] public float LabelHeight { get; set; } = 1.9f;

    public string LabelText => _label?.Text ?? "";
    public bool ShowingEstimate => _label != null && _label.Visible;

    private Label3D? _label;

    public override void _Ready()
    {
        StandardMaterial3D stone = new() { AlbedoColor = StoneColor, Roughness = 1.0f };
        float y = 0.0f;
        float[] sizes = { 0.7f, 0.55f, 0.4f, 0.28f };
        for (int i = 0; i < sizes.Length; i++)
        {
            float size = sizes[i];
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(size, size * 0.6f, size) },
                Position = new Vector3(0.0f, y + size * 0.3f, 0.0f),
                RotationDegrees = new Vector3(0.0f, i * 27.0f, 0.0f),
                MaterialOverride = stone,
            });
            y += size * 0.6f;
        }
        _label = new Label3D
        {
            FontSize = LabelFontSize,
            PixelSize = LabelPixelSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            OutlineSize = 10,
            Position = Vector3.Up * LabelHeight,
            Visible = false,
        };
        AddChild(_label);
    }

    public void ShowEstimate(RouteEstimate estimate, WorldClock? clock)
    {
        if (_label == null)
        {
            return;
        }
        if (!estimate.Valid)
        {
            _label.Visible = false;
            return;
        }
        _label.Text = Describe(estimate, clock);
        if (!estimate.BeforeNightfall)
        {
            _label.Modulate = LateColor;
        }
        else if (estimate.Rests > 0)
        {
            _label.Modulate = TightColor;
        }
        else
        {
            _label.Modulate = SafeColor;
        }
        _label.Visible = true;
    }

    public void HideEstimate()
    {
        if (_label != null)
        {
            _label.Visible = false;
        }
    }

    /// <summary>The estimate as the cairn writes it (in-game hours, as the carrier's clock shows them).</summary>
    public static string Describe(RouteEstimate estimate, WorldClock? clock)
    {
        float hoursPerSecond = 0.0f;
        float sunset = 20.0f;
        if (clock != null)
        {
            hoursPerSecond = clock.HoursPerSecond;
            sunset = clock.SunsetHour;
        }
        string distance = $"{estimate.DistanceMeters:0} m";
        if (estimate.DistanceMeters >= 1000.0f)
        {
            distance = $"{estimate.DistanceMeters / 1000.0f:0.0} km";
        }
        string rests = "no rests";
        if (estimate.Rests == 1)
        {
            rests = "1 rest";
        }
        else if (estimate.Rests > 1)
        {
            rests = $"{estimate.Rests} rests";
        }
        string arrival = $"arrive {Clock(estimate.ArrivalHour)}";
        if (clock != null && estimate.ArrivalDay > clock.Day)
        {
            arrival = $"arrive {Clock(estimate.ArrivalHour)} (day {estimate.ArrivalDay})";
        }
        string dark = $"dark at {Clock(sunset)}";
        if (!estimate.BeforeNightfall)
        {
            dark = "after dark";
        }
        return $"Station {estimate.Station} · {distance}\n" +
            $"~{Duration(estimate.TotalSeconds * hoursPerSecond)} · {rests}\n" +
            $"stamina {estimate.StaminaNeeded:0}\n" +
            $"{arrival} · {dark}";
    }

    private static string Clock(float hour)
    {
        float wrapped = Mathf.PosMod(hour, 24.0f);
        int h = Mathf.FloorToInt(wrapped);
        int m = Mathf.FloorToInt((wrapped - h) * 60.0f);
        return $"{h:00}:{m:00}";
    }

    private static string Duration(float hours)
    {
        int minutes = Mathf.RoundToInt(hours * 60.0f);
        if (minutes < 60)
        {
            return $"{minutes} min";
        }
        return $"{minutes / 60} h {minutes % 60:00} min";
    }
}
