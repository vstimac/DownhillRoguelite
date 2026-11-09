using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// FCAT overlay for frame-capture analysis (`-- --fcat`): a coloured bar down the left edge whose
/// colour steps through the FCAT sequence once per rendered frame. Captured video then shows each
/// game frame as its own colour band, so dropped, runt and torn frames can be measured with FCAT /
/// FrameView-style tools. Drawn on top of everything; never takes the mouse. Off unless asked for.
/// </summary>
public partial class FcatOverlay : CanvasLayer
{
    [Export] public string Arg { get; set; } = "--fcat";
    /// <summary>Bar width in pixels.</summary>
    [Export] public float Width { get; set; } = 24.0f;
    /// <summary>The FCAT colour sequence (16 colours, repeating).</summary>
    [Export] public Color[] Sequence { get; set; } =
    {
        new(1.0f, 1.0f, 1.0f), new(0.0f, 1.0f, 0.0f), new(0.0f, 0.0f, 1.0f), new(1.0f, 0.0f, 0.0f),
        new(0.0f, 0.5f, 0.5f), new(0.0f, 0.0f, 0.5f), new(0.0f, 0.5f, 0.0f), new(0.0f, 1.0f, 1.0f),
        new(0.5f, 0.0f, 0.0f), new(0.75f, 0.75f, 0.75f), new(0.5f, 0.0f, 0.5f), new(0.5f, 0.5f, 0.0f),
        new(0.5f, 0.5f, 0.5f), new(1.0f, 0.0f, 1.0f), new(1.0f, 1.0f, 0.0f), new(1.0f, 0.65f, 0.0f),
    };

    public bool Active { get; private set; }
    private ColorRect? _bar;

    public override void _Ready()
    {
        Layer = 128;
        ProcessMode = ProcessModeEnum.Always;
        Active = System.Array.IndexOf(OS.GetCmdlineUserArgs(), Arg) >= 0;
        if (!Active || Sequence.Length == 0)
        {
            SetProcess(false);
            return;
        }
        _bar = new ColorRect
        {
            Name = "FcatBar",
            AnchorTop = 0.0f,
            AnchorBottom = 1.0f,
            OffsetRight = Width,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_bar);
    }

    public override void _Process(double delta)
    {
        if (_bar == null)
        {
            return;
        }
        // One step per drawn frame, so every frame on screen has its own colour.
        _bar.Color = Sequence[(int)((long)Engine.GetFramesDrawn() % Sequence.Length)];
    }
}
