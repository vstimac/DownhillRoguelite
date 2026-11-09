using System.Collections.Generic;
using DownhillRoguelite.Narrative;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Preview every line of the restsite president (debug/TestPresident.tscn): the restsite set on a flat
/// ground with the president at his podium, and a list of all manifest lines. Up/Down picks a line,
/// Enter (or Space) says it, V switches the take (forest/outdoor), R says a random idle line. The
/// panel shows the line's id, its recording (or "subtitles only"), event, emotion and words.
/// `-- --lab-line=intro_004` starts on that line and says it (for renders).
/// </summary>
public partial class PresidentLab : Node3D
{
    [Export(PropertyHint.File, "*.csv")] public string ManifestPath { get; set; } = "res://narrative/president/data/npc_restsite_president.csv";
    [Export(PropertyHint.File, "*.json")] public string VoicePackPath { get; set; } = "res://narrative/president/data/npc_restsite_president_voice.json";
    [Export(PropertyHint.File, "*.csv")] public string TextPath { get; set; } = "res://narrative/president/data/player_text.csv";

    private VoiceManifest _manifest = new();
    private VoicePack _pack = new();
    private TextTable _text = new();
    private readonly List<VoiceLine> _lines = new();
    private PresidentNpc? _npc;
    private Label? _panel;
    private int _index;
    private string _variant = VoicePackLine.Outdoor;

    public override void _Ready()
    {
        _manifest = VoiceManifest.Load(ManifestPath);
        _pack = VoicePack.Load(VoicePackPath);
        _text = TextTable.Load(TextPath);
        _lines.AddRange(_manifest.Lines);
        StaticBody3D ground = new() { Name = "Ground" };
        ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(60.0f, 1.0f, 60.0f) }, Position = Vector3.Down * 0.5f });
        ground.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(60.0f, 1.0f, 60.0f) }, Position = Vector3.Down * 0.5f, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.42f, 0.3f) } });
        AddChild(ground);
        RestsiteSet set = new();
        set.Build(1, _text);
        AddChild(set);
        set.SetVictories(7);
        _npc = new PresidentNpc();
        _npc.Setup(_manifest, _pack, _variant, _text, new PresidentDialogue(new PresidentLabHost(_text), _manifest));
        set.AddChild(_npc);
        _npc.Position = set.PresidentOffset;
        CanvasLayer layer = new();
        AddChild(layer);
        _panel = new Label { Position = new Vector2(16, 16), Size = new Vector2(900, 200), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _panel.AddThemeColorOverride("font_color", Colors.White);
        _panel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _panel.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(_panel);
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("--lab-line=", System.StringComparison.Ordinal))
            {
                int at = _lines.FindIndex(line => line.Id == argument["--lab-line=".Length..]);
                if (at >= 0)
                {
                    _index = at;
                    CallDeferred(MethodName.SayCurrent);
                }
            }
        }
        Refresh();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || _lines.Count == 0)
        {
            return;
        }
        switch (key.Keycode)
        {
            case Key.Up:
                _index = (_index + _lines.Count - 1) % _lines.Count;
                break;
            case Key.Down:
                _index = (_index + 1) % _lines.Count;
                break;
            case Key.Enter:
            case Key.Space:
                SayCurrent();
                break;
            case Key.V:
                if (_variant == VoicePackLine.Outdoor)
                {
                    _variant = VoicePackLine.Forest;
                }
                else
                {
                    _variant = VoicePackLine.Outdoor;
                }
                if (_npc?.Voice != null)
                {
                    _npc.Voice.Variant = _variant;
                }
                break;
            case Key.R:
                string idle = _npc?.Dialogue?.IdleLine() ?? "";
                _index = Mathf.Max(0, _lines.FindIndex(line => line.Id == idle));
                SayCurrent();
                break;
        }
        Refresh();
    }

    private void SayCurrent()
    {
        _npc?.Voice?.Say(_lines[_index].Id);
        Refresh();
    }

    private void Refresh()
    {
        if (_panel == null || _lines.Count == 0 || _npc?.Voice == null)
        {
            return;
        }
        VoiceLine line = _lines[_index];
        AudioStream? stream = _npc.Voice.Resolve(line, out string source);
        string voice = "subtitles only";
        if (stream != null)
        {
            voice = source;
        }
        _panel.Text = $"[{_index + 1}/{_lines.Count}] {line.Id}   ({line.Event}, {line.Emotion}, priority {line.Priority})\n"
            + $"take: {_variant}   voice: {voice}\n{line.Text}\n\n"
            + "Up/Down line · Enter say · V forest/outdoor · R idle line";
    }
}
