using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// A small voice adapter for one NPC (not an audio framework): says manifest lines by id. Each line
/// raises VoiceLineRequested (npc, line, subtitle, expected file), shows its subtitle
/// (GameEvents.PublishSpeech, from where the NPC stands) and plays its voice file from the NPC on the
/// Voice bus when the file exists; without the file the subtitle stays up for a reading time instead,
/// so a missing .ogg never stops a conversation. One line at a time: a new line or a skip ends the
/// one playing (VoiceLineCompleted, skipped). The music is ducked while it speaks.
/// The file is only ever the manifest's audio_path (or the same name as .wav/.mp3); no folder is scanned.
/// </summary>
public partial class NpcVoice : Node3D
{
    [Export] public string NpcId { get; set; } = "";
    [Export] public string SpeakerName { get; set; } = "";
    [Export] public StringName Bus { get; set; } = "Voice";
    [Export] public float VolumeDb { get; set; } = 2.0f;
    [Export] public float MaxDistance { get; set; } = 40.0f;
    /// <summary>Reading time for a line without a voice file: words per second, and at least this many seconds.</summary>
    [Export] public float WordsPerSecond { get; set; } = 2.6f;
    [Export] public float MinSeconds { get; set; } = 1.8f;
    /// <summary>A short pause after each line, in s.</summary>
    [Export] public float Tail { get; set; } = 0.35f;
    [Export] public StringName DuckBus { get; set; } = "Music";
    [Export] public float DuckDb { get; set; } = -8.0f;
    /// <summary>Print every line said (id, voiced or not) to the output.</summary>
    [Export] public bool LogLines { get; set; } = true;

    public VoiceManifest? Manifest { get; set; }
    /// <summary>The recorded takes (may be null: then only the manifest's audio_path is tried).</summary>
    public VoicePack? Pack { get; set; }
    /// <summary>Which take to prefer: "forest" or "outdoor" (the restsite's surroundings).</summary>
    public string Variant { get; set; } = VoicePackLine.Outdoor;
    public bool Speaking => _current.Length > 0;
    public string CurrentLine => _current;
    public int LineSequence { get; private set; }
    public float LineElapsed { get; private set; }
    private string _sharedLine = "";
    public void ApplySharedLine(long author, int sequence, string line, float elapsed)
    {
        string stamp = author + ":" + sequence + ":" + line;
        if (_sharedLine == stamp) { return; }
        _sharedLine = stamp;
        if (line.Length == 0) { Skip(); return; }
        if (Say(line))
        {
            _left = Mathf.Max(0.01f, _left - elapsed);
            LineElapsed = elapsed;
            if (_player?.Playing == true) { _player.Seek(elapsed); }
        }
    }
    /// <summary>The last line that ended, and whether it was skipped (tests).</summary>
    public string LastFinished { get; private set; } = "";

    private AudioStreamPlayer3D? _player;
    private string _current = "";
    private float _left;
    private bool _ducked;
    private float _duckBefore;

    public override void _Ready()
    {
        _player = new AudioStreamPlayer3D
        {
            Name = "VoicePlayer",
            Bus = Bus,
            VolumeDb = VolumeDb,
            MaxDistance = MaxDistance,
        };
        AddChild(_player);
        GameEvents.VoiceSkipRequested += Skip;
    }

    public override void _ExitTree()
    {
        GameEvents.VoiceSkipRequested -= Skip;
        Unduck();
    }

    /// <summary>Says a line by id; false (and nothing said) if the manifest has no such line.</summary>
    public bool Say(string lineId)
    {
        VoiceLine? found = Manifest?.Find(lineId);
        if (found is not VoiceLine line)
        {
            GD.PushWarning($"NpcVoice: {NpcId} has no line '{lineId}' in its manifest.");
            return false;
        }
        if (Speaking)
        {
            Finish(true);
        }
        string expected = ExpectedPath(line);
        GameEvents.PublishVoiceLineRequested(NpcId, line.Id, line.Text, expected);
        AudioStream? stream = Resolve(line, out string source);
        float seconds;
        if (stream != null)
        {
            seconds = (float)stream.GetLength() + Tail;
        }
        else
        {
            seconds = ReadingTime(line.Text) + Tail;
        }
        _current = line.Id;
        LineSequence++; LineElapsed = 0;
        _left = seconds;
        // The voice stays as recorded; the subtitle follows Options → Dialogue text.
        GameEvents.PublishSpeech(new SpeechLine(SpeakerName, line.Shown, GlobalPosition + Vector3.Up * 1.6f, seconds));
        if (stream != null && _player != null && IsInsideTree())
        {
            _player.Stream = stream;
            _player.Play();
            Duck();
        }
        if (LogLines)
        {
            string how = "subtitles only (expected " + expected + ")";
            if (stream != null)
            {
                how = "voiced: " + source;
            }
            GD.Print($"[voice] {NpcId}/{line.Id} ({line.Event}, {line.Emotion}) {how}");
        }
        GameEvents.PublishVoiceLineStarted(NpcId, line.Id, stream != null);
        return true;
    }

    /// <summary>Ends the line being spoken now.</summary>
    public void Skip()
    {
        if (Speaking)
        {
            Finish(true);
        }
    }

    public override void _Process(double delta)
    {
        if (!Speaking)
        {
            return;
        }
        _left -= (float)delta;
        LineElapsed += (float)delta;
        if (_left <= 0.0f)
        {
            Finish(false);
        }
    }

    /// <summary>Seconds a line takes to read when it has no voice file.</summary>
    public float ReadingTime(string text)
    {
        int words = text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Length;
        return Mathf.Max(MinSeconds, words / Mathf.Max(WordsPerSecond, 0.1f));
    }

    /// <summary>
    /// The voice for a line: the recording with its exact words in the pack (the Variant take, else
    /// the other take), else the manifest's audio_path; null means subtitles only.
    /// </summary>
    public AudioStream? Resolve(VoiceLine line, out string source)
    {
        if (Pack?.ForText(line.Text) is VoicePackLine recorded)
        {
            foreach (string path in recorded.PathsFor(Variant))
            {
                AudioStream? take = Stream(path);
                if (take != null)
                {
                    source = recorded.Id + " " + path.GetBaseDir().GetFile() + "/" + path.GetFile();
                    return take;
                }
            }
        }
        AudioStream? stream = Stream(line.AudioPath);
        source = line.AudioPath.GetFile();
        return stream;
    }

    /// <summary>Where the line's voice is looked for first (for VoiceLineRequested and the log).</summary>
    public string ExpectedPath(VoiceLine line)
    {
        if (Pack?.ForText(line.Text) is VoicePackLine recorded)
        {
            return recorded.PathsFor(Variant)[0];
        }
        return line.AudioPath;
    }

    /// <summary>A voice file if it exists: this path, or the same name as .wav or .mp3.</summary>
    public static AudioStream? Stream(string path)
    {
        if (path.Length == 0)
        {
            return null;
        }
        string stem = path.GetBaseName();
        foreach (string candidate in new[] { path, stem + ".wav", stem + ".mp3" })
        {
            if (ResourceLoader.Exists(candidate) && ResourceLoader.Load(candidate) is AudioStream stream)
            {
                return stream;
            }
        }
        return null;
    }

    private void Finish(bool skipped)
    {
        string id = _current;
        _current = "";
        _left = 0.0f;
        _player?.Stop();
        Unduck();
        LastFinished = id;
        GameEvents.PublishVoiceLineCompleted(NpcId, id, skipped);
    }

    private void Duck()
    {
        int bus = AudioServer.GetBusIndex(DuckBus);
        if (_ducked || bus < 0)
        {
            return;
        }
        _duckBefore = AudioServer.GetBusVolumeDb(bus);
        AudioServer.SetBusVolumeDb(bus, _duckBefore + DuckDb);
        _ducked = true;
    }

    private void Unduck()
    {
        int bus = AudioServer.GetBusIndex(DuckBus);
        if (!_ducked || bus < 0)
        {
            return;
        }
        AudioServer.SetBusVolumeDb(bus, _duckBefore);
        _ducked = false;
    }
}
