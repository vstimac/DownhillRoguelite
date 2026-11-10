using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// Where the companion is and how their live replies sound. The companion walks a step to the
/// carrier's left (relative to the view), following smoothly; that spot is published every frame
/// (GameEvents.CompanionPosition) so the narrator's recorded lines play from there and subtitles
/// are written on that side. The live conversation's replies (M11) are read aloud with the
/// system's text-to-speech voice (no voice model ships yet, so this one isn't panned in 3D) and
/// subtitled from the companion's side. Music ducks while they speak.
/// </summary>
public partial class CompanionVoice : Node3D
{
    /// <summary>The companion's spot relative to the carrier and the view: x right, y up, z forward, in m.</summary>
    [Export] public Vector3 Offset { get; set; } = new(-1.2f, 1.5f, 0.3f);
    /// <summary>How quickly the companion keeps up (1/s).</summary>
    [Export] public float Follow { get; set; } = 6.0f;
    [Export] public bool SpeakReplies { get; set; } = true;
    /// <summary>Picks the system voice whose name contains this (e.g. "David", "Zira"); empty takes the first English one.</summary>
    [Export] public string VoiceHint { get; set; } = "";
    [Export(PropertyHint.Range, "0,100")] public int Volume { get; set; } = 85;
    [Export] public float Rate { get; set; } = 1.0f;
    [Export] public float Pitch { get; set; } = 0.95f;
    /// <summary>Speech length estimate for subtitles and ducking: seconds per word, plus a pause.</summary>
    [Export] public float SecondsPerWord { get; set; } = 0.38f;
    [Export] public float ExtraSeconds { get; set; } = 1.2f;
    [Export] public string SpeakerName { get; set; } = "Companion";

    public string VoiceId { get; private set; } = "";
    /// <summary>A second system voice for travellers, so they don't sound like the companion (empty: the same one).</summary>
    public string OtherVoiceId { get; private set; } = "";
    public string LastSpoken { get; private set; } = "";
    /// <summary>
    /// Where speech-dispatcher's library may live on Linux. Godot's Linux voice backend needs it;
    /// without it asking for voices fails ("synth is null"), so we check first and stay text-only.
    /// </summary>
    [Export] public string[] SpeechDispatcherPaths { get; set; } =
    {
        "/usr/lib/libspeechd.so.2",
        "/usr/lib64/libspeechd.so.2",
        "/usr/lib/x86_64-linux-gnu/libspeechd.so.2",
        "/lib/x86_64-linux-gnu/libspeechd.so.2",
        "/usr/local/lib/libspeechd.so.2",
    };
    /// <summary>Command-line switch (after --) that turns the system voice off: subtitles only.</summary>
    [Export] public string NoVoiceArg { get; set; } = "--no-tts";

    private bool _placed;
    private bool _voicesChecked;
    private float _speakingFor;

    public override void _Ready()
    {
        TopLevel = true;
        GameEvents.CompanionReplied += OnReply;
        // Voices are looked up at the first reply, not here: on Linux the speech backend starts on
        // its own thread and isn't ready yet at start-up (and the first reply takes seconds anyway).
    }

    /// <summary>
    /// Finds the voices once, if the system can speak at all; otherwise the companion and travellers
    /// are subtitled only. Safe to call often.
    /// </summary>
    private void EnsureVoices()
    {
        if (_voicesChecked)
        {
            return;
        }
        _voicesChecked = true;
        if (!SpeakReplies || !TextToSpeechAvailable())
        {
            GD.Print("Companion voice: no system text-to-speech, replies are subtitled only.");
            return;
        }
        VoiceId = PickVoice();
        foreach (string id in DisplayServer.TtsGetVoicesForLanguage("en"))
        {
            if (id != VoiceId)
            {
                OtherVoiceId = id;
                break;
            }
        }
        if (VoiceId.Length == 0)
        {
            GD.Print("Companion voice: no English system voice found, replies are subtitled only.");
        }
    }

    /// <summary>Whether asking the system for voices can work here (display, project setting, Linux library, switch).</summary>
    private bool TextToSpeechAvailable()
    {
        if (DisplayServer.GetName() == "headless")
        {
            return false;
        }
        if (!ProjectSettings.GetSetting("audio/general/text_to_speech", false).AsBool())
        {
            return false;
        }
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == NoVoiceArg)
            {
                return false;
            }
        }
        string os = OS.GetName();
        if (os == "Linux" || os == "FreeBSD" || os == "NetBSD" || os == "OpenBSD" || os == "BSD")
        {
            foreach (string path in SpeechDispatcherPaths)
            {
                if (System.IO.File.Exists(path))
                {
                    return true;
                }
            }
            return false;
        }
        return true;
    }

    public override void _ExitTree()
    {
        GameEvents.CompanionReplied -= OnReply;
        if (VoiceId.Length > 0)
        {
            DisplayServer.TtsStop();
        }
        GameEvents.PublishSpeaking(false);
    }

    private string PickVoice()
    {
        string[] english = DisplayServer.TtsGetVoicesForLanguage("en");
        if (english.Length == 0)
        {
            return "";
        }
        if (VoiceHint.Length > 0)
        {
            foreach (Godot.Collections.Dictionary voice in DisplayServer.TtsGetVoices())
            {
                string name = voice["name"].AsString();
                if (name.Contains(VoiceHint, System.StringComparison.OrdinalIgnoreCase))
                {
                    return voice["id"].AsString();
                }
            }
        }
        return english[0];
    }

    public override void _Process(double delta)
    {
        Vector3 carrier = GameEvents.CarrierPosition;
        Basis yaw = Basis.Identity;
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera != null)
        {
            Vector3 forward = -camera.GlobalBasis.Z;
            forward.Y = 0.0f;
            if (!forward.IsZeroApprox())
            {
                forward = forward.Normalized();
                Vector3 right = forward.Cross(Vector3.Up);
                yaw = new Basis(right, Vector3.Up, -forward);
            }
        }
        Vector3 target = carrier + yaw * new Vector3(Offset.X, Offset.Y, -Offset.Z);
        if (!_placed)
        {
            GlobalPosition = target;
            _placed = true;
        }
        else
        {
            GlobalPosition = GlobalPosition.Lerp(target, Mathf.Clamp(Follow * (float)delta, 0.0f, 1.0f));
        }
        GameEvents.PublishCompanionPosition(GlobalPosition);
        if (_speakingFor > 0.0f)
        {
            _speakingFor -= (float)delta;
            if (_speakingFor <= 0.0f)
            {
                GameEvents.PublishSpeaking(false);
            }
        }
    }

    /// <summary>A finished reply from the live conversation: say it and subtitle it.</summary>
    private void OnReply(string text, bool done)
    {
        if (!done || text.Length == 0)
        {
            return;
        }
        SpeakerInfo speaker = GameEvents.ReplySpeaker;
        if (speaker.IsCompanion)
        {
            if (GameEvents.Companion != CompanionState.Silent)
            {
                Say(text);
            }
            return;
        }
        // A traveller: their own voice, from where they sit.
        SayAs(speaker, text);
    }

    /// <summary>Says a traveller's line from their spot, in a second system voice at their pitch.</summary>
    public void SayAs(SpeakerInfo speaker, string text)
    {
        int words = text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Length;
        float duration = words * SecondsPerWord / Mathf.Max(Rate, 0.1f) + ExtraSeconds;
        LastSpoken = text;
        GameEvents.PublishSpeech(new SpeechLine(speaker.Name, text, speaker.Position, duration));
        EnsureVoices();
        string voice = OtherVoiceId;
        if (voice.Length == 0)
        {
            voice = VoiceId;
        }
        if (voice.Length > 0)
        {
            DisplayServer.TtsSpeak(text, voice, Volume, Mathf.Clamp(speaker.Pitch, 0.0f, 2.0f), Rate, 0, true);
            _speakingFor = duration;
            GameEvents.PublishSpeaking(true);
        }
    }

    /// <summary>Says a line in the companion's (system) voice and publishes its subtitle.</summary>
    public void Say(string text)
    {
        int words = text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Length;
        float duration = words * SecondsPerWord / Mathf.Max(Rate, 0.1f) + ExtraSeconds;
        LastSpoken = text;
        GameEvents.PublishSpeech(new SpeechLine(SpeakerName, text, GlobalPosition, duration));
        EnsureVoices();
        if (VoiceId.Length > 0)
        {
            DisplayServer.TtsSpeak(text, VoiceId, Volume, Pitch, Rate, 0, true);
            // (Travellers use SayAs.)
            _speakingFor = duration;
            GameEvents.PublishSpeaking(true);
        }
    }
}
