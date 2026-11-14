using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// The voiced companion, first stage of M11: listens to game events, turns them into the trigger
/// names of narrative/data/metadata.csv and plays a fitting line from audio/narrator/. A line must
/// suit the current circle, be off its cooldown and under its plays per run; lines of the current
/// narrative phase (curiosity in the first circles, then familiarity, doubt, acceptance) are
/// preferred, higher priority more likely. One line at a time, with a gap between lines; a much
/// more important line may cut in. Publishes NarrationStarted/Finished (music ducks) and prints
/// the text. Live conversation (NobodyWho) comes later.
/// </summary>
public partial class NarratorDirector : Node
{
    [Export] public bool Enabled { get; set; } = true;
    [Export(PropertyHint.File, "*.csv")] public string MetadataPath { get; set; } = "res://narrative/data/metadata.csv";
    [Export(PropertyHint.Dir)] public string AudioFolder { get; set; } = "res://audio/narrator";
    [Export] public StringName Bus { get; set; } = "Voice";
    [Export] public float VolumeDb { get; set; }
    /// <summary>Off for headless tests: lines "play" for their length without sound.</summary>
    [Export] public bool PlayAudio { get; set; } = true;
    /// <summary>How strongly the voice is panned toward the companion's side (0 centred, 1 fully).</summary>
    [Export] public float VoicePanning { get; set; } = 0.7f;
    [Export] public string SpeakerName { get; set; } = "Companion";
    /// <summary>
    /// How fast the lines are spoken (1 = as recorded): below 1 he speaks slower and every word comes
    /// out clearer. With KeepPitch the voice keeps its own pitch (a pitch shift on his own bus, NarratorBus,
    /// which feeds Bus); without it a slower line is also a deeper one.
    /// </summary>
    [Export(PropertyHint.Range, "0.6,1.2")] public float SpeechRate { get; set; } = 0.86f;
    [Export] public bool KeepPitch { get; set; } = true;
    [Export] public StringName NarratorBus { get; set; } = "Narrator";

    [ExportGroup("Pacing")]
    /// <summary>Seconds of quiet after a line before the next one.</summary>
    [Export] public float MinGap { get; set; } = 30.0f;
    /// <summary>The longest an NPC's voiced line holds the narrator back (s), in case its end is never reported.</summary>
    [Export] public float NpcLineHoldMax { get; set; } = 30.0f;
    /// <summary>Seconds after the start before the first line.</summary>
    [Export] public float OpeningDelay { get; set; } = 4.0f;
    /// <summary>Seconds between ambient musings, random in this range.</summary>
    [Export] public Vector2 AmbientInterval { get; set; } = new(160.0f, 300.0f);
    /// <summary>Chance an ambient musing is about godhood instead of the circle.</summary>
    [Export] public float GodhoodChance { get; set; } = 0.2f;
    /// <summary>A line may cut in on a playing one only if its priority is this much higher.</summary>
    [Export] public int InterruptMargin { get; set; } = 2;
    /// <summary>Chance of preferring a line of the current narrative phase.</summary>
    [Export] public float PhasePreference { get; set; } = 0.7f;

    [ExportGroup("Thresholds")]
    [Export] public float IdleShortSeconds { get; set; } = 30.0f;
    [Export] public float IdleLongSeconds { get; set; } = 90.0f;
    [Export] public float IdleSpeed { get; set; } = 0.2f;
    [Export] public float DepthMinor { get; set; } = 100.0f;
    [Export] public float DepthMajor { get; set; } = 500.0f;
    [Export] public float HeavyLoad { get; set; } = 25.0f;
    [Export] public float CriticalLoad { get; set; } = 40.0f;
    /// <summary>Landing speed above which a fall counts as from height, in m/s.</summary>
    [Export] public float HighFallSpeed { get; set; } = 12.0f;
    [Export] public float BraceSeconds { get; set; } = 1.5f;

    public int LineCount => _all.Count;
    public string? CurrentLine { get; private set; }
    public bool Speaking => (_player != null && _player.Playing) || _speakingUntil >= 0.0f;

    private readonly List<NarratorLine> _all = new();
    private readonly Dictionary<string, List<NarratorLine>> _byTrigger = new();
    private readonly Dictionary<string, int> _plays = new();
    private readonly Dictionary<string, float> _lastPlayed = new();
    private readonly HashSet<string> _playedThisSave = new();
    private readonly RandomNumberGenerator _random = new();
    private AudioStreamPlayer3D? _player;
    private RunState? _run;
    private float _time;
    private float _quietSince = -1000.0f;
    private float _npcLineSince = -1000.0f;
    private bool _npcLine;
    /// <summary>
    /// Somebody else has the word: the conversation page, a dialogue with an NPC, a voiced NPC line or
    /// the companion's spoken reply. The narrator stops its line and waits out the usual gap after it.
    /// </summary>
    public bool Held => GameEvents.IsConversing || GameEvents.InDialogue || GameEvents.IsSpeaking || (_npcLine && _time - _npcLineSince < NpcLineHoldMax);
    private float _speakingUntil = -1.0f;
    private int _currentPriority;
    private float _nextAmbient;
    private bool _opened;
    private int _circle;
    private float _deepestMinor;
    private float _deepestMajor;
    private float _idle;
    private bool _idleShortDone;
    private bool _idleLongDone;
    private BalanceState _balanceState = BalanceState.Stable;
    private float _braced;
    private bool _bracedDone;
    private float _lastLanding;
    private bool _dead;
    private bool _low;
    private bool _critical;
    private bool _exhausted;
    private float _healthFraction = 1.0f;
    private float _load;
    private bool _wasInShelter;
    private int _restarts;
    private int _optionsStage = -1;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _random.Randomize();
        // Headless runs have no real audio output and never release playing streams.
        if (DisplayServer.GetName() == "headless")
        {
            PlayAudio = false;
        }
        Load();
        // The companion's voice comes from where they walk, a step to the carrier's left (CompanionVoice):
        // panned in 3D, but not fading with distance.
        _player = new AudioStreamPlayer3D
        {
            Bus = SpeechBus(),
            PitchScale = Mathf.Clamp(SpeechRate, 0.3f, 2.0f),
            VolumeDb = VolumeDb,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
            PanningStrength = VoicePanning,
            DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
        };
        _player.Finished += OnFinished;
        AddChild(_player);
        _nextAmbient = _random.RandfRange(AmbientInterval.X, AmbientInterval.Y);

        GameEvents.DepthChanged += OnDepthChanged;
        GameEvents.StationReached += OnStationReached;
        GameEvents.ShelterChanged += OnShelterChanged;
        GameEvents.Slept += OnSlept;
        GameEvents.Landed += OnLanded;
        GameEvents.DownedChanged += OnDownedChanged;
        GameEvents.BalanceUpdated += OnBalanceUpdated;
        GameEvents.VitalsUpdated += OnVitalsUpdated;
        GameEvents.RespawnRequested += OnRespawnRequested;
        GameEvents.ItemPickedUp += OnItemPickedUp;
        GameEvents.LoadChanged += OnLoadChanged;
        GameEvents.PackingModeChanged += OnPackingModeChanged;
        GameEvents.WeatherChanged += OnWeatherChanged;
        GameEvents.LocomotionUpdated += OnLocomotionUpdated;
        GameEvents.RouteOptionsUpdated += OnRouteOptions;
        GameEvents.RouteChosen += OnRouteChosen;
        GameEvents.MapOpened += OnMapOpened;
        GameEvents.ItemLost += OnItemLost;
        GameEvents.ContractAccepted += OnContractAccepted;
        GameEvents.ContractResolved += OnContractResolved;
        GameEvents.RelicDiscovered += OnRelicDiscovered;
        GameEvents.VoiceLineStarted += OnNpcLineStarted;
        GameEvents.VoiceLineCompleted += OnNpcLineCompleted;
    }

    public override void _ExitTree()
    {
        GameEvents.DepthChanged -= OnDepthChanged;
        GameEvents.StationReached -= OnStationReached;
        GameEvents.ShelterChanged -= OnShelterChanged;
        GameEvents.Slept -= OnSlept;
        GameEvents.Landed -= OnLanded;
        GameEvents.DownedChanged -= OnDownedChanged;
        GameEvents.BalanceUpdated -= OnBalanceUpdated;
        GameEvents.VitalsUpdated -= OnVitalsUpdated;
        GameEvents.RespawnRequested -= OnRespawnRequested;
        GameEvents.ItemPickedUp -= OnItemPickedUp;
        GameEvents.LoadChanged -= OnLoadChanged;
        GameEvents.PackingModeChanged -= OnPackingModeChanged;
        GameEvents.WeatherChanged -= OnWeatherChanged;
        GameEvents.LocomotionUpdated -= OnLocomotionUpdated;
        GameEvents.RouteOptionsUpdated -= OnRouteOptions;
        GameEvents.RouteChosen -= OnRouteChosen;
        GameEvents.MapOpened -= OnMapOpened;
        GameEvents.ItemLost -= OnItemLost;
        GameEvents.ContractAccepted -= OnContractAccepted;
        GameEvents.ContractResolved -= OnContractResolved;
        GameEvents.RelicDiscovered -= OnRelicDiscovered;
        GameEvents.VoiceLineStarted -= OnNpcLineStarted;
        GameEvents.VoiceLineCompleted -= OnNpcLineCompleted;
        Silence();
    }

    /// <summary>Stops the current line at once (quitting, cutscenes, tests).</summary>
    public void Silence()
    {
        if (_player != null)
        {
            _player.Stop();
            _player.Stream = null;
        }
        _speakingUntil = -1.0f;
        CurrentLine = null;
        _currentPriority = 0;
        if (GameEvents.IsNarrating)
        {
            GameEvents.PublishNarrationFinished();
        }
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (_player != null && _player.IsInsideTree())
        {
            _player.GlobalPosition = GameEvents.CompanionPosition;
        }
        if (_speakingUntil >= 0.0f && _time >= _speakingUntil)
        {
            _speakingUntil = -1.0f;
            OnFinished();
        }
        if (Held)
        {
            if (Speaking) { Silence(); }
            _quietSince = _time;
        }
        if (!_opened && _time >= OpeningDelay)
        {
            _opened = true;
            if (!Say(CircleTrigger(_circle, "entered")))
            {
                Say("area_entered");
            }
        }
        if (_time >= _nextAmbient)
        {
            _nextAmbient = _time + _random.RandfRange(AmbientInterval.X, AmbientInterval.Y);
            if (_random.Randf() < GodhoodChance)
            {
                Say("godhood_reflection");
            }
            else
            {
                Say(CircleTrigger(_circle, "ambient"));
            }
        }
    }

    /// <summary>Plays a line for a trigger if one fits; false if nothing played.</summary>
    public bool Say(string trigger)
    {
        if (!Enabled || _player == null || !_byTrigger.TryGetValue(trigger, out List<NarratorLine>? lines))
        {
            return false;
        }
        // While the player talks with the companion or an NPC, the voiced lines hold back.
        if (Held)
        {
            return false;
        }
        List<NarratorLine> candidates = new();
        int bestPriority = 0;
        foreach (NarratorLine line in lines)
        {
            if (!Available(line))
            {
                continue;
            }
            candidates.Add(line);
            bestPriority = Mathf.Max(bestPriority, line.Priority);
        }
        if (candidates.Count == 0)
        {
            return false;
        }

        if (Speaking)
        {
            if (bestPriority < _currentPriority + InterruptMargin)
            {
                return false;
            }
        }
        else if (_time - _quietSince < MinGap)
        {
            return false;
        }

        NarratorLine chosen = Pick(candidates);
        AudioStream? stream = ResourceLoader.Load<AudioStream>($"{AudioFolder}/{chosen.Id}.wav");
        if (stream == null)
        {
            return false;
        }
        _plays[chosen.Id] = _plays.GetValueOrDefault(chosen.Id) + 1;
        _lastPlayed[chosen.Id] = _time;
        if (chosen.OncePerSave)
        {
            _playedThisSave.Add(chosen.Id);
        }
        _currentPriority = chosen.Priority;
        CurrentLine = chosen.Id;
        GameEvents.PublishSpeech(new SpeechLine(SpeakerName, chosen.Text, GameEvents.CompanionPosition, SpokenLength(stream)));
        if (PlayAudio)
        {
            _player.Stream = stream;
            _player.GlobalPosition = GameEvents.CompanionPosition;
            _player.Play();
        }
        else
        {
            _speakingUntil = _time + SpokenLength(stream);
        }
        GD.Print($"Narrator [{trigger}] {chosen.Id}: {chosen.Text}");
        GameEvents.PublishNarrationStarted(chosen.Id, chosen.Text);
        return true;
    }

    /// <summary>How long a line takes at the speech rate, in s.</summary>
    public float SpokenLength(AudioStream stream)
    {
        return (float)stream.GetLength() / Mathf.Clamp(SpeechRate, 0.3f, 2.0f);
    }

    /// <summary>
    /// The bus his voice goes through: at the recorded rate the plain Bus; slowed down with KeepPitch,
    /// his own bus (made once) whose pitch shift undoes the slower playback's deeper pitch.
    /// </summary>
    private StringName SpeechBus()
    {
        if (!KeepPitch || Mathf.IsEqualApprox(SpeechRate, 1.0f))
        {
            return Bus;
        }
        int index = AudioServer.GetBusIndex(NarratorBus);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, NarratorBus);
            AudioServer.SetBusSend(index, Bus);
            AudioServer.AddBusEffect(index, new AudioEffectPitchShift
            {
                PitchScale = 1.0f / Mathf.Clamp(SpeechRate, 0.3f, 2.0f),
                Oversampling = 8,
                FftSize = AudioEffectPitchShift.FftSizeEnum.Size2048,
            });
        }
        return NarratorBus;
    }

    private bool Available(NarratorLine line)
    {
        if (!line.FitsCircle(_circle))
        {
            return false;
        }
        if (line.OncePerSave && _playedThisSave.Contains(line.Id))
        {
            return false;
        }
        if (line.MaxPlaysPerRun > 0 && _plays.GetValueOrDefault(line.Id) >= line.MaxPlaysPerRun)
        {
            return false;
        }
        if (_lastPlayed.TryGetValue(line.Id, out float last) && _time - last < line.CooldownSeconds)
        {
            return false;
        }
        return true;
    }

    private NarratorLine Pick(List<NarratorLine> candidates)
    {
        string phase = PhaseFor(_circle);
        List<NarratorLine> pool = candidates;
        if (_random.Randf() < PhasePreference)
        {
            List<NarratorLine> inPhase = candidates.FindAll(line => line.Phase == phase);
            if (inPhase.Count > 0)
            {
                pool = inPhase;
            }
        }
        int total = 0;
        foreach (NarratorLine line in pool)
        {
            total += Mathf.Max(1, line.Priority);
        }
        int pick = _random.RandiRange(0, total - 1);
        foreach (NarratorLine line in pool)
        {
            pick -= Mathf.Max(1, line.Priority);
            if (pick < 0)
            {
                return line;
            }
        }
        return pool[pool.Count - 1];
    }

    private static string PhaseFor(int circle)
    {
        if (circle <= 1)
        {
            return "curiosity";
        }
        if (circle <= 3)
        {
            return "familiarity";
        }
        if (circle <= 7)
        {
            return "doubt";
        }
        return "acceptance";
    }

    private static string CircleTrigger(int circle, string suffix)
    {
        if (circle >= NarratorLine.Beyond)
        {
            if (suffix == "entered")
            {
                return "beyond_ninth_circle";
            }
            return "beyond_ninth_ambient";
        }
        return $"circle_{circle + 1}_{suffix}";
    }

    private void OnNpcLineStarted(string npcId, string lineId, bool voiced)
    {
        _npcLine = true;
        _npcLineSince = _time;
    }

    private void OnNpcLineCompleted(string npcId, string lineId, bool skipped) => _npcLine = false;

    private void OnFinished()
    {
        _quietSince = _time;
        _currentPriority = 0;
        CurrentLine = null;
        GameEvents.PublishNarrationFinished();
    }

    private void Load()
    {
        using FileAccess? file = FileAccess.Open(MetadataPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"Narrator: can't open {MetadataPath}");
            return;
        }
        string[] header = file.GetCsvLine();
        Dictionary<string, int> column = new();
        for (int i = 0; i < header.Length; i++)
        {
            column[header[i].Trim()] = i;
        }
        while (!file.EofReached())
        {
            string[] row = file.GetCsvLine();
            if (row.Length < header.Length)
            {
                continue;
            }
            NarratorLine line = new(
                row[column["id"]],
                row[column["category"]],
                row[column["trigger"]],
                row[column["circle_context"]],
                row[column["narrative_phase"]],
                row[column["priority"]].ToInt(),
                row[column["cooldown_seconds"]].ToFloat(),
                row[column["max_plays_per_run"]].ToInt(),
                row[column["once_per_save"]] == "true",
                row[column["dialogue"]]);
            _all.Add(line);
            if (!_byTrigger.TryGetValue(line.Trigger, out List<NarratorLine>? list))
            {
                list = new List<NarratorLine>();
                _byTrigger[line.Trigger] = list;
            }
            list.Add(line);
        }
    }

    private void OnDepthChanged(float depth)
    {
        if (_run != null)
        {
            int circle = _run.Circles.CircleAt(depth);
            if (circle != _circle)
            {
                _circle = circle;
                Say(CircleTrigger(circle, "entered"));
                return;
            }
        }
        if (depth >= _deepestMajor + DepthMajor)
        {
            _deepestMajor = Mathf.Floor(depth / DepthMajor) * DepthMajor;
            _deepestMinor = Mathf.Floor(depth / DepthMinor) * DepthMinor;
            Say("depth_milestone_major");
        }
        else if (depth >= _deepestMinor + DepthMinor)
        {
            _deepestMinor = Mathf.Floor(depth / DepthMinor) * DepthMinor;
            Say("depth_milestone_minor");
        }
    }

    private void OnStationReached(int station)
    {
        Say("camp_reached");
    }

    private void OnShelterChanged(bool inside, int station)
    {
        if (_wasInShelter && !inside)
        {
            Say("camp_leaving");
        }
        _wasInShelter = inside;
    }

    private void OnSlept(float hours)
    {
        Say("camp_resting");
    }

    private void OnLanded(float downwardSpeed)
    {
        _lastLanding = downwardSpeed;
    }

    private void OnDownedChanged(bool downed)
    {
        if (downed)
        {
            if (_lastLanding >= HighFallSpeed && Say("player_fell_from_height"))
            {
                return;
            }
            Say("player_fell");
            return;
        }
        if (_healthFraction < 0.5f && Say("player_got_up_injured"))
        {
            return;
        }
        Say("player_got_up");
    }

    private void OnBalanceUpdated(BalanceSnapshot snapshot)
    {
        if (snapshot.State != _balanceState)
        {
            if (snapshot.State == BalanceState.Stumbling)
            {
                Say("player_stumbled");
            }
            else if (_balanceState == BalanceState.Stumbling && snapshot.State != BalanceState.Fallen)
            {
                Say("stumble_recovered");
            }
            else if (snapshot.State == BalanceState.Wobbling && _balanceState == BalanceState.Stable)
            {
                Say("balance_wobbling");
            }
            _balanceState = snapshot.State;
        }

        if (snapshot.Bracing)
        {
            _braced += 1.0f / Engine.PhysicsTicksPerSecond;
            if (!_bracedDone && _braced >= BraceSeconds)
            {
                _bracedDone = true;
                Say("balance_bracing");
            }
        }
        else
        {
            _braced = 0.0f;
            _bracedDone = false;
        }
    }

    private void OnVitalsUpdated(VitalsSnapshot vitals)
    {
        _healthFraction = 1.0f;
        if (vitals.MaxHealth > 0.0f)
        {
            _healthFraction = vitals.Health / vitals.MaxHealth;
        }
        if (vitals.Dead && !_dead)
        {
            Say("player_died");
        }
        _dead = vitals.Dead;

        bool critical = _healthFraction < 0.25f && !vitals.Dead;
        bool low = _healthFraction < 0.5f && !vitals.Dead;
        if (critical && !_critical)
        {
            Say("health_critical");
        }
        else if (low && !_low)
        {
            Say("health_low");
        }
        _critical = critical;
        _low = low;

        if (vitals.Exhausted && !_exhausted && _load >= HeavyLoad)
        {
            Say("stamina_exhausted_loaded");
        }
        _exhausted = vitals.Exhausted;
    }

    private void OnRespawnRequested()
    {
        _restarts++;
        _plays.Clear();
        _circle = 0;
        _deepestMinor = 0.0f;
        _deepestMajor = 0.0f;
        if (_restarts > 1 && Say("run_restarted_repeat"))
        {
            return;
        }
        Say("run_restarted");
    }

    private void OnItemPickedUp(string itemId, string category)
    {
        if (_random.Randf() < 0.5f && Say("cargo_found"))
        {
            return;
        }
        Say("cargo_picked_up");
    }

    private void OnLoadChanged(LoadSnapshot load)
    {
        float previous = _load;
        _load = load.Mass;
        if (_load >= CriticalLoad && previous < CriticalLoad)
        {
            Say("cargo_weight_critical");
        }
        else if (_load >= HeavyLoad && previous < HeavyLoad)
        {
            Say("cargo_weight_high");
        }
    }

    private void OnPackingModeChanged(bool packing)
    {
        if (packing)
        {
            Say("packing_opened");
        }
        else
        {
            Say("packing_closed");
        }
    }

    private void OnWeatherChanged(WeatherKind kind)
    {
        if (kind == WeatherKind.Fog)
        {
            Say("weather_fog_rolled_in");
        }
        else if (kind == WeatherKind.Rain)
        {
            Say("weather_rain_started");
        }
        else if (kind == WeatherKind.Snow)
        {
            Say("weather_snow_started");
        }
        else if (kind == WeatherKind.Wind)
        {
            Say("weather_wind_rising");
        }
        else if (kind == WeatherKind.Heat)
        {
            Say("weather_heat");
        }
        else
        {
            Say("weather_clearing");
        }
    }

    private void OnRouteOptions(int stage, RouteEstimate[] options)
    {
        if (stage != _optionsStage)
        {
            _optionsStage = stage;
            Say("route_choice_presented");
        }
    }

    private void OnItemLost(string itemId)
    {
        Say("cargo_lost");
    }

    /// <summary>Taking a job: the voiced lines about picking something up to carry.</summary>
    private void OnContractAccepted(ContractNote note)
    {
        Say("cargo_picked_up");
    }

    /// <summary>A relic found: the voiced lines about something that looks almost holy.</summary>
    private void OnRelicDiscovered(RelicNote note)
    {
        Say("relic_found");
    }

    /// <summary>A delivery: the voiced lines about giving up something carried a long way.</summary>
    private void OnContractResolved(ContractNote note)
    {
        if (note.Status == ContractStatus.Delivered)
        {
            Say("trade_completed");
        }
    }

    private void OnMapOpened(bool open)
    {
        if (open)
        {
            Say("map_viewed");
        }
    }

    private void OnRouteChosen(int stage, RouteKind kind)
    {
        if (kind == RouteKind.Hazardous)
        {
            Say("route_chosen_hazardous");
        }
        else if (kind == RouteKind.Safe)
        {
            Say("route_chosen_safe");
        }
    }

    private void OnLocomotionUpdated(LocomotionSnapshot snapshot)
    {
        float speed = new Vector2(snapshot.Velocity.X, snapshot.Velocity.Z).Length();
        if (speed > IdleSpeed || GameEvents.IsPacking)
        {
            _idle = 0.0f;
            _idleShortDone = false;
            _idleLongDone = false;
            return;
        }
        _idle += 1.0f / Engine.PhysicsTicksPerSecond;
        if (!_idleLongDone && _idle >= IdleLongSeconds)
        {
            _idleLongDone = true;
            Say("idle_long");
        }
        else if (!_idleShortDone && _idle >= IdleShortSeconds)
        {
            _idleShortDone = true;
            Say("idle_short");
        }
    }
}
