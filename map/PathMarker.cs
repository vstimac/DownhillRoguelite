using DownhillRoguelite.Core;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Map;

/// <summary>
/// A small signpost a little way down each path of a stage with a choice of paths, telling whoever
/// walks it which path this is, how long it runs before it rejoins, how far it drops and what others
/// found on it. Facts only, from the route itself (StationManager measures them).
/// </summary>
public partial class PathMarker : Node3D
{
    [Export(PropertyHint.File, "*.glb")] public string PostModel { get; set; } = "res://art/items/kenney_survival_kit/GLB format/signpost-single.glb";
    [Export] public float PostHeight { get; set; } = 1.5f;
    [Export] public int FontSize { get; set; } = 30;
    [Export] public float PixelSize { get; set; } = 0.005f;
    [Export] public float LineSpacing { get; set; } = 0.2f;
    [Export] public float LabelRange { get; set; } = 26.0f;
    [Export] public Color SafeColor { get; set; } = new(0.85f, 0.9f, 0.8f);
    [Export] public Color HazardousColor { get; set; } = new(1.0f, 0.62f, 0.45f);
    [Export] public Color SupplyColor { get; set; } = new(0.7f, 0.9f, 1.0f);
    [Export] public Color NoteColor { get; set; } = new(0.88f, 0.84f, 0.72f);

    public RouteKind Kind { get; private set; }
    /// <summary>All the lines, top to bottom (tests).</summary>
    public string Text { get; private set; } = "";

    private readonly Label3D[] _lines = new Label3D[3];

    public override void _Ready()
    {
        if (ResourceLoader.Exists(PostModel) && ResourceLoader.Load(PostModel) is PackedScene scene)
        {
            Node3D holder = new() { Name = "Post" };
            holder.AddChild(scene.Instantiate<Node3D>());
            Aabb bounds = ModelFit.Bounds(holder);
            float scale = PostHeight / Mathf.Max(bounds.Size.Y, 0.01f);
            holder.Transform = ModelFit.FitOnGround(bounds, bounds.Size * scale, true);
            AddChild(holder);
        }
        for (int i = 0; i < _lines.Length; i++)
        {
            _lines[i] = new Label3D
            {
                FontSize = FontSize,
                PixelSize = PixelSize,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                OutlineSize = 8,
                Position = new Vector3(0.0f, PostHeight + 0.35f + (_lines.Length - 1 - i) * LineSpacing, 0.0f),
            };
            AddChild(WorldLabel.Readable(_lines[i], LabelRange));
        }
        _lines[0].FontSize = FontSize + 6;
    }

    /// <summary>Writes the marker: the path's kind, its length and drop to where it rejoins, and a note.</summary>
    public void Show(RouteKind kind, float lengthMetres, float dropMetres, bool rejoins)
    {
        Kind = kind;
        string where = "to the next station";
        if (rejoins)
        {
            where = "until it rejoins";
        }
        string[] text =
        {
            Heading(kind),
            WaypointCairn.Distance(lengthMetres) + " " + where + " · " + Mathf.RoundToInt(dropMetres) + " m down",
            Note(kind),
        };
        Text = "";
        for (int i = 0; i < _lines.Length; i++)
        {
            if (_lines[i] == null)
            {
                continue;
            }
            _lines[i].Text = text[i];
            _lines[i].Modulate = NoteColor;
        }
        Text = string.Join("\n", text);
        if (_lines[0] != null)
        {
            _lines[0].Modulate = ColorFor(kind);
        }
    }

    public static string Heading(RouteKind kind)
    {
        return RouteSign.KindName(kind) + " path";
    }

    /// <summary>What carriers before you scratched under the path's name.</summary>
    public static string Note(RouteKind kind)
    {
        if (kind == RouteKind.Hazardous)
        {
            return "Shorter, narrow, loose underfoot. Caches for the brave.";
        }
        if (kind == RouteKind.SupplyRich)
        {
            return "The long way round. Food and water left by others.";
        }
        return "Cairns all the way. Gentle ground.";
    }

    private Color ColorFor(RouteKind kind)
    {
        if (kind == RouteKind.Hazardous)
        {
            return HazardousColor;
        }
        if (kind == RouteKind.SupplyRich)
        {
            return SupplyColor;
        }
        return SafeColor;
    }
}
