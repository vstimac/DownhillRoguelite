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
    public string LastSpoken { get; private set; } = "";

    private bool _placed;
    private float _speakingFor;

    public override void _Ready()
    {
        TopLevel = true;
        GameEvents.CompanionReplied += OnReply;
        if (DisplayServer.GetName() != "headless" && SpeakReplies)
        {
            VoiceId = PickVoice();
        }
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
        if (!done || text.Length == 0 || GameEvents.Companion == CompanionState.Silent)
        {
            return;
        }
        Say(text);
    }

    /// <summary>Says a line in the companion's (system) voice and publishes its subtitle.</summary>
    public void Say(string text)
    {
        int words = text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Length;
        float duration = words * SecondsPerWord / Mathf.Max(Rate, 0.1f) + ExtraSeconds;
        LastSpoken = text;
        GameEvents.PublishSpeech(new SpeechLine(SpeakerName, text, GlobalPosition, duration));
        if (VoiceId.Length > 0)
        {
            DisplayServer.TtsSpeak(text, VoiceId, Volume, Pitch, Rate, 0, true);
            _speakingFor = duration;
            GameEvents.PublishSpeaking(true);
        }
    }
}
