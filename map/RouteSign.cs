using DownhillRoguelite.Core;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Map;

/// <summary>
/// A wooden lookout board at the top of a stage, beside the path just below the station: one line
/// per path the stage offers, its kind, which way it goes, how far and how long (rests included),
/// the stamina it costs and when it arrives, coloured by kind; a path that arrives after dark is
/// marked. Filled from GameEvents.RouteOptionsUpdated by the station manager.
/// </summary>
public partial class RouteSign : Node3D
{
    [Export] public Color WoodColor { get; set; } = new(0.4f, 0.28f, 0.18f);
    [Export] public Color SafeColor { get; set; } = new(0.7f, 1.0f, 0.7f);
    [Export] public Color HazardousColor { get; set; } = new(1.0f, 0.55f, 0.4f);
    [Export] public Color SupplyColor { get; set; } = new(1.0f, 0.85f, 0.45f);
    [Export] public Color LateColor { get; set; } = new(0.75f, 0.75f, 0.8f);
    [Export] public int FontSize { get; set; } = 32;
    [Export] public float PixelSize { get; set; } = 0.004f;
    [Export] public float LineSpacing { get; set; } = 0.32f;
    [Export] public float BoardHeight { get; set; } = 2.2f;

    public int Stage { get; set; }
    public int LineCount { get; private set; }
    public string Text { get; private set; } = "";

    private readonly Label3D[] _lines = new Label3D[4];

    public override void _Ready()
    {
        StandardMaterial3D wood = new() { AlbedoColor = WoodColor, Roughness = 1.0f };
        foreach (float x in new[] { -0.9f, 0.9f })
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.1f, BoardHeight + 0.4f, 0.1f) },
                Position = new Vector3(x, (BoardHeight + 0.4f) * 0.5f, 0.0f),
                MaterialOverride = wood,
            });
        }
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(2.2f, 1.2f, 0.06f) },
            Position = new Vector3(0.0f, BoardHeight, 0.0f),
            MaterialOverride = wood,
        });
        for (int i = 0; i < _lines.Length; i++)
        {
            _lines[i] = new Label3D
            {
                FontSize = FontSize,
                PixelSize = PixelSize,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                OutlineSize = 8,
                Position = new Vector3(0.0f, BoardHeight + 1.4f - i * LineSpacing, 0.0f),
                Visible = false,
            };
            AddChild(WorldLabel.Readable(_lines[i]));
        }
        _lines[0].Text = "Paths ahead";
        _lines[0].Visible = true;
    }

    /// <summary>Shows one line per path; <paramref name="directions"/> says which way each goes ("left", "ahead", "right").</summary>
    public void ShowOptions(RouteEstimate[] options, string[] directions, WorldClock? clock)
    {
        LineCount = 0;
        string all = "";
        for (int i = 1; i < _lines.Length; i++)
        {
            int path = i - 1;
            if (path >= options.Length || !options[path].Valid)
            {
                _lines[i].Visible = false;
                continue;
            }
            RouteEstimate estimate = options[path];
            string direction = "";
            if (path < directions.Length)
            {
                direction = directions[path];
            }
            string line = Describe(estimate, direction, clock);
            _lines[i].Text = line;
            _lines[i].Modulate = ColorFor(estimate);
            _lines[i].Visible = true;
            all += line + "\n";
            LineCount++;
        }
        Text = all;
    }

    public static string KindName(RouteKind kind)
    {
        if (kind == RouteKind.Hazardous)
        {
            return "Hazardous";
        }
        if (kind == RouteKind.SupplyRich)
        {
            return "Supply-rich";
        }
        return "Safe";
    }

    public static string Describe(RouteEstimate estimate, string direction, WorldClock? clock)
    {
        float hoursPerSecond = 0.0f;
        if (clock != null)
        {
            hoursPerSecond = clock.HoursPerSecond;
        }
        string dark = " · before dark";
        if (!estimate.BeforeNightfall)
        {
            dark = " · after dark";
        }
        return $"{KindName(estimate.Kind)} ({direction}) · {WaypointCairn.Distance(estimate.DistanceMeters)} · " +
            $"~{TimeText.Real(estimate.TotalSeconds)}{dark}";
    }

    private Color ColorFor(RouteEstimate estimate)
    {
        if (!estimate.BeforeNightfall)
        {
            return LateColor;
        }
        if (estimate.Kind == RouteKind.Hazardous)
        {
            return HazardousColor;
        }
        if (estimate.Kind == RouteKind.SupplyRich)
        {
            return SupplyColor;
        }
        return SafeColor;
    }
}
