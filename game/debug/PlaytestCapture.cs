using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Debug-only, game-viewport recording. Keeps a bounded rolling sequence at a rendered-frame interval.
/// Off by default: each capture reads the frame back from the GPU and encodes a JPEG, which halves the
/// frame rate. F9 turns it on and off during play; `-- --capture` (or an explicit capture argument) starts with it on.
/// </summary>
public partial class PlaytestCapture : Node
{
    [Export] public bool Enabled { get; set; }
    [Export] public int EveryFrames { get; set; } = 5;
    [Export] public int MaxImages { get; set; } = 600;
    [Export] public float Quality { get; set; } = 0.88f;
    [Export] public float ChangeThreshold { get; set; } = 0.06f;
    [Export] public string OutputDirectory { get; set; } = "res://../../math-audit-artifacts/live-capture";
    public string SessionDirectory { get; private set; } = "";
    private long _frame;
    private long _sequence;
    private bool _connected;
    private byte[]? _previous;
    private readonly List<object> _index = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (!OS.IsDebugBuild() || DisplayServer.GetName() == "headless") { SetProcessInput(false); return; }
        bool explicitCapture = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--capture") { Enabled = true; explicitCapture = true; }
            if (arg == "--capture-off") { Enabled = false; }
            if (arg.StartsWith("--capture-directory=", StringComparison.Ordinal))
            { OutputDirectory = arg["--capture-directory=".Length..]; explicitCapture = true; Enabled = true; }
            if (arg.StartsWith("--capture-every=", StringComparison.Ordinal) && int.TryParse(arg["--capture-every=".Length..], out int interval))
            { EveryFrames = Math.Max(interval, 1); explicitCapture = true; Enabled = true; }
            if (arg.StartsWith("--capture-max=", StringComparison.Ordinal) && int.TryParse(arg["--capture-max=".Length..], out int limit))
            { MaxImages = Math.Clamp(limit, 1, 10000); }
        }
        if (Array.IndexOf(OS.GetCmdlineArgs(), "--write-movie") >= 0 && !explicitCapture) { Enabled = false; }
        EveryFrames = Math.Max(1, EveryFrames);
        MaxImages = Math.Clamp(MaxImages, 1, 10000);
        RenderingServer.FramePostDraw += Capture;
        _connected = true;
        if (Enabled) { GD.Print("Playtest capture: every " + EveryFrames + " rendered frames; F9 pauses/resumes."); }
    }

    public override void _ExitTree() { if (_connected) { RenderingServer.FramePostDraw -= Capture; } }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F9)
        {
            Enabled = !Enabled;
            GD.Print("Playtest capture enabled: " + Enabled + "; " + SessionDirectory);
            GetViewport().SetInputAsHandled();
        }
    }

    private void Capture()
    {
        _frame++;
        if (!Enabled || _frame % EveryFrames != 0) { return; }
        try
        {
            if (SessionDirectory.Length == 0)
            {
                string output = ProjectSettings.GlobalizePath(OutputDirectory);
                SessionDirectory = Path.Combine(output, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + OS.GetProcessId());
                Directory.CreateDirectory(SessionDirectory);
                GD.Print("Playtest capture folder: " + SessionDirectory);
            }
            using Image snapshot = GetViewport().GetTexture().GetImage();
            string file = "frame_" + (_sequence % MaxImages).ToString("D4") + ".jpg";
            Error error = snapshot.SaveJpg(Path.Combine(SessionDirectory, file), Mathf.Clamp(Quality, 0.1f, 1.0f));
            if (error != Error.Ok) { throw new IOException("Image save: " + error); }
            snapshot.Resize(32, 18, Image.Interpolation.Bilinear);
            snapshot.Convert(Image.Format.Rgb8);
            byte[] small = snapshot.GetData();
            double change = 0;
            if (_previous != null && _previous.Length == small.Length)
            {
                for (int i = 0; i < small.Length; i++) { change += Math.Abs(small[i] - _previous[i]); }
                change /= small.Length * 255.0;
            }
            _previous = small;
            Vector3 position = GameEvents.CarrierPosition;
            RunState? run = GetNodeOrNull<RunState>("/root/RunState");
            _index.Add(new { Sequence = _sequence++, RenderedFrame = _frame, File = file,
                Seconds = Time.GetTicksMsec() / 1000.0, PixelChange = change, SignificantChange = change >= ChangeThreshold,
                Character = run?.Character.Id ?? GameOptions.Character, SelectedCharacter = GameOptions.Character,
                AbilityActive = run?.Character.Active, AbilitySeconds = run?.Character.ActiveLeft, CooldownSeconds = run?.Character.CooldownLeft,
                Paused = GetTree().Paused, Position = new[] { position.X, position.Y, position.Z } });
            if (_index.Count > MaxImages) { _index.RemoveAt(0); }
            string manifest = JsonSerializer.Serialize(new { EveryFrames, MaxImages, Frames = _index }, new JsonSerializerOptions { WriteIndented = true });
            string temporary = Path.Combine(SessionDirectory, "index.tmp");
            File.WriteAllText(temporary, manifest);
            File.Move(temporary, Path.Combine(SessionDirectory, "index.json"), true);
        }
        catch (Exception exception)
        {
            Enabled = false;
            GD.PushWarning("Playtest capture stopped: " + exception.Message);
        }
    }
}
