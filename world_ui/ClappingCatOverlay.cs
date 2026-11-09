using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.WorldUi;

/// <summary>
/// When the carrier dies (GameEvents.RunEnded) a cat claps for him: a short green-screen video,
/// keyed out, standing at the side of the screen over the run's summary. Esc skips it; it hides at
/// the end of the clip or when the next run begins. Never takes the mouse.
/// </summary>
public partial class ClappingCatOverlay : CanvasLayer
{
    [Export(PropertyHint.File, "*.ogv")] public string VideoPath { get; set; } = "res://art/video/clapping_cat.ogv";
    [Export(PropertyHint.File, "*.gdshader")] public string ShaderPath { get; set; } = "res://world_ui/green_screen.gdshader";
    /// <summary>Seconds after the death before the cat appears, so the fall is seen first.</summary>
    [Export] public float DelaySeconds { get; set; } = 1.2f;
    /// <summary>The clip's height as a share of the screen's.</summary>
    [Export] public float HeightShare { get; set; } = 0.9f;
    /// <summary>Where the clip stands: 0 = left edge, 1 = right edge (x), 0 = top, 1 = bottom (y).</summary>
    [Export] public Vector2 Placement { get; set; } = new(0.97f, 1.0f);
    [Export] public Color KeyColour { get; set; } = new(0.0f, 1.0f, 0.0f);
    [Export] public float KeyThreshold { get; set; } = 0.32f;
    [Export] public float KeySoftness { get; set; } = 0.13f;
    [Export] public float VolumeDb { get; set; } = -4.0f;
    /// <summary>Width over height used until the video reports its own size (the clip is upright).</summary>
    [Export] public float FallbackAspect { get; set; } = 0.5625f;

    public bool Playing => _player != null && _player.Visible;

    private VideoStreamPlayer? _player;
    private float _wait = -1.0f;

    public override void _Ready()
    {
        Layer = 20;
        ProcessMode = ProcessModeEnum.Always;
        if (ResourceLoader.Load(VideoPath) is not VideoStream stream)
        {
            GD.PushWarning("ClappingCatOverlay: no video at " + VideoPath);
            return;
        }
        ShaderMaterial key = new() { Shader = GD.Load<Shader>(ShaderPath) };
        key.SetShaderParameter("key_color", KeyColour);
        key.SetShaderParameter("threshold", KeyThreshold);
        key.SetShaderParameter("softness", KeySoftness);
        _player = new VideoStreamPlayer
        {
            Name = "ClappingCat",
            Stream = stream,
            Expand = true,
            Autoplay = false,
            Loop = false,
            Visible = false,
            VolumeDb = VolumeDb,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = key,
        };
        AddChild(_player);
        _player.Finished += Stop;
        GetViewport().SizeChanged += Place;
        GameEvents.RunEnded += OnRunEnded;
        GameEvents.RespawnRequested += Stop;
    }

    public override void _ExitTree()
    {
        GameEvents.RunEnded -= OnRunEnded;
        GameEvents.RespawnRequested -= Stop;
    }

    public override void _Process(double delta)
    {
        if (_wait < 0.0f)
        {
            return;
        }
        _wait -= (float)delta;
        if (_wait < 0.0f)
        {
            Play();
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (Playing && inputEvent.IsActionPressed("ui_cancel"))
        {
            Stop();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Plays the clip now (also for tests and renders).</summary>
    public void Play()
    {
        _wait = -1.0f;
        if (_player == null)
        {
            return;
        }
        Place();
        _player.Visible = true;
        _player.Play();
        Place();
    }

    public void Stop()
    {
        _wait = -1.0f;
        if (_player == null)
        {
            return;
        }
        _player.Stop();
        _player.Visible = false;
    }

    private void OnRunEnded(RunSummary summary)
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }
        _wait = Mathf.Max(DelaySeconds, 0.0f);
    }

    /// <summary>Sizes the clip to the screen, keeping its aspect, and stands it at <see cref="Placement"/>.</summary>
    private void Place()
    {
        if (_player?.Stream == null)
        {
            return;
        }
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        Vector2 source = _player.GetVideoTexture()?.GetSize() ?? Vector2.Zero;
        float aspect = FallbackAspect;
        if (source.X > 0.0f && source.Y > 0.0f)
        {
            aspect = source.X / source.Y;
        }
        float height = screen.Y * HeightShare;
        Vector2 size = new(height * aspect, height);
        _player.Size = size;
        _player.Position = (screen - size) * Placement;
    }
}
