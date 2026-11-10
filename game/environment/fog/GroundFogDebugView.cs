using DownhillRoguelite.Debug;
using Godot;

namespace DownhillRoguelite.Environment.Fog;

/// <summary>
/// The fog's debug view (/fog debug density|flow|ground): a map of the whole patch in the lower
/// left corner (north up, the carrier as a white dot) showing its density (blue thin to red thick),
/// its flow (arrows) or what the sampler found (green ground shaded by height, red blocked, yellow
/// not found yet, grey not looked for), and in the world, crosses on the sampled ground around the
/// carrier. The fog itself is tinted the same way by the shader.
/// </summary>
public partial class GroundFogDebugView : CanvasLayer
{
    /// <summary>Size of the map on screen (px), cells per square drawn, and how far around the carrier crosses are drawn (m).</summary>
    [Export] public float MapSize { get; set; } = 256.0f;
    [Export] public int Block { get; set; } = 4;
    [Export] public float CrossRadius { get; set; } = 6.0f;

    public GroundFog? Fog { get; set; }

    private Control? _map;

    public override void _Ready()
    {
        Layer = 90;
        _map = new Control { Name = "Map", MouseFilter = Control.MouseFilterEnum.Ignore };
        _map.Draw += DrawMap;
        AddChild(_map);
    }

    public override void _Process(double delta)
    {
        if (_map == null || Fog == null)
        {
            return;
        }
        bool on = Fog.DebugView != GroundFog.View.Off;
        _map.Visible = on;
        if (!on)
        {
            return;
        }
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        _map.Position = new Vector2(12.0f, screen.Y - MapSize - 12.0f);
        _map.Size = new Vector2(MapSize, MapSize);
        _map.QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Fog?.Simulation == null || Fog.DebugView != GroundFog.View.Ground)
        {
            return;
        }
        GroundFogSimulation sim = Fog.Simulation;
        Vector3 feet = Fog.Feet;
        int reach = Mathf.CeilToInt(CrossRadius / sim.Cell);
        if (!sim.CellAt(feet.X, feet.Z, out int cx, out int cz))
        {
            return;
        }
        for (int z = cz - reach; z <= cz + reach; z++)
        {
            for (int x = cx - reach; x <= cx + reach; x++)
            {
                if (x < 0 || z < 0 || x >= sim.Size || z >= sim.Size)
                {
                    continue;
                }
                int i = sim.Index(x, z);
                Vector2 at = sim.CellCentre(x, z);
                if (sim.Ground[i] != GroundFogSimulation.Known)
                {
                    DebugDraw3D.Cross(new Vector3(at.X, feet.Y, at.Y), new Color(1.0f, 0.85f, 0.1f), 0.08f);
                    continue;
                }
                Color colour = new(0.2f, 1.0f, 0.3f);
                if (sim.Solid[i])
                {
                    colour = new Color(1.0f, 0.2f, 0.15f);
                }
                Vector3 ground = new(at.X, sim.Height[i], at.Y);
                DebugDraw3D.Cross(ground, colour, 0.1f);
                if (sim.Solid[i])
                {
                    DebugDraw3D.Line(ground, ground + Vector3.Up * sim.Settings.ObstacleHeight, colour);
                }
            }
        }
    }

    private void DrawMap()
    {
        if (_map == null || Fog?.Simulation == null)
        {
            return;
        }
        GroundFogSimulation sim = Fog.Simulation;
        int block = Mathf.Max(1, Block);
        int blocks = sim.Size / block;
        float square = MapSize / blocks;
        _map.DrawRect(new Rect2(Vector2.Zero, new Vector2(MapSize, MapSize)), new Color(0, 0, 0, 0.55f));
        float low = float.MaxValue;
        float high = float.MinValue;
        for (int i = 0; i < sim.Height.Length; i++)
        {
            if (sim.Ground[i] == GroundFogSimulation.Known)
            {
                low = Mathf.Min(low, sim.Height[i]);
                high = Mathf.Max(high, sim.Height[i]);
            }
        }
        for (int bz = 0; bz < blocks; bz++)
        {
            for (int bx = 0; bx < blocks; bx++)
            {
                int x = bx * block + block / 2;
                int z = bz * block + block / 2;
                int i = sim.Index(x, z);
                Rect2 rect = new(new Vector2(bx * square, bz * square), new Vector2(square, square));
                Vector2 middle = rect.GetCenter();
                switch (Fog.DebugView)
                {
                    case GroundFog.View.Density:
                    {
                        float d = Mathf.Clamp(sim.Density[i] / sim.Settings.MaxDensity, 0.0f, 1.0f);
                        _map.DrawRect(rect, new Color(0.1f, 0.3f, 1.0f).Lerp(new Color(1.0f, 0.15f, 0.05f), d));
                        break;
                    }
                    case GroundFog.View.Flow:
                    {
                        Vector2 flow = new(sim.FlowX[i], sim.FlowZ[i]);
                        float speed = flow.Length();
                        if (speed > 0.02f)
                        {
                            Vector2 tip = middle + flow / Mathf.Max(sim.Settings.MaxFlow, 0.1f) * square * 1.4f;
                            Color colour = new Color(0.3f, 0.9f, 1.0f).Lerp(Colors.White, Mathf.Clamp(speed / sim.Settings.MaxFlow, 0.0f, 1.0f));
                            _map.DrawLine(middle, tip, colour, 1.0f);
                            _map.DrawCircle(tip, 1.2f, colour);
                        }
                        break;
                    }
                    default:
                    {
                        Color colour = new(0.4f, 0.4f, 0.4f);
                        if (sim.Ground[i] == GroundFogSimulation.Known)
                        {
                            float share = 0.5f;
                            if (high > low)
                            {
                                share = (sim.Height[i] - low) / (high - low);
                            }
                            colour = new Color(0.05f, 0.35f, 0.1f).Lerp(new Color(0.6f, 1.0f, 0.6f), share);
                            if (sim.Solid[i])
                            {
                                colour = new Color(1.0f, 0.2f, 0.15f);
                            }
                        }
                        else if (sim.Ground[i] == GroundFogSimulation.Missed)
                        {
                            colour = new Color(1.0f, 0.85f, 0.1f);
                        }
                        _map.DrawRect(rect, colour);
                        break;
                    }
                }
            }
        }
        Vector3 feet = Fog.Feet;
        Vector2 dot = new((feet.X / sim.Cell - sim.OriginX) / sim.Size * MapSize, (feet.Z / sim.Cell - sim.OriginZ) / sim.Size * MapSize);
        _map.DrawCircle(dot, 3.0f, Colors.White);
        _map.DrawString(ThemeDB.FallbackFont, new Vector2(4, 14), "fog: " + Fog.DebugView.ToString().ToLowerInvariant(), HorizontalAlignment.Left, -1, 12, Colors.White);
    }
}
