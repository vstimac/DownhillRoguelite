using System.Collections.Generic;
using System.Text;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Wildlife debug: a line of text (animals alive / cap, cost per frame, circle, count per species)
/// and a short post over each animal coloured by what it is doing (green idle, yellow walking,
/// red fleeing). Hidden with F3 like the other overlays.
/// </summary>
public partial class WildlifeDebugOverlay : CanvasLayer
{
    [Export] public WildlifeSpawner? Spawner { get; set; }
    [Export] public int FontSize { get; set; } = 15;
    [Export] public Vector2 Margin { get; set; } = new(16.0f, 0.0f);
    /// <summary>Distance of the text from the bottom of the screen, in pixels.</summary>
    [Export] public float Bottom { get; set; } = 150.0f;

    private Label? _label;

    public override void _Ready()
    {
        _label = new Label();
        _label.AddThemeFontSizeOverride("font_size", FontSize);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", FontSize / 4);
        AddChild(_label);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_label == null || Spawner?.Table == null)
        {
            return;
        }
        Visible = DebugDraw3D.Enabled;
        if (!Visible)
        {
            return;
        }

        SortedDictionary<string, int> counts = new();
        foreach (WildlifeAnimal animal in Spawner.Animals)
        {
            string name = animal.Species?.DisplayName ?? "?";
            counts[name] = counts.GetValueOrDefault(name) + 1;
            Color color = Colors.LimeGreen;
            if (animal.State == WildlifeAnimal.Mode.Walk)
            {
                color = Colors.Yellow;
            }
            else if (animal.State == WildlifeAnimal.Mode.Flee)
            {
                color = Colors.Red;
            }
            Vector3 top = animal.GlobalPosition + Vector3.Up * 2.5f;
            DebugDraw3D.Line(top, top + Vector3.Up * 0.8f, color);
        }

        StringBuilder text = new();
        text.Append($"Wildlife {Spawner.Animals.Count}/{Spawner.Table.MaxAnimals}  {Spawner.AverageMilliseconds:0.00} ms  circle {Spawner.Circle + 1}  ");
        foreach (KeyValuePair<string, int> pair in counts)
        {
            text.Append($" {pair.Key} {pair.Value}");
        }
        _label.Text = text.ToString();
        _label.Position = new Vector2(Margin.X, GetViewport().GetVisibleRect().Size.Y - Bottom);
    }
}
