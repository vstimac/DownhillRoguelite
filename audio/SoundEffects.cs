using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Audio;

/// <summary>
/// One-shot sounds for game events, from the Kenney sound packs (audio/sfx): landings and falls
/// (impacts by how hard), picking up (cloth), eating and drinking, packing (belt and straps), the map
/// (book), a boon chosen and a station reached (interface chimes), the end of a run (a bell). Each
/// event picks a random variant, slightly detuned, through a small pool of players.
/// </summary>
public partial class SoundEffects : Node
{
    [Export] public StringName Bus { get; set; } = "Ambience";
    [Export] public float VolumeDb { get; set; } = -4.0f;
    /// <summary>Landings slower than this make no sound, in m/s.</summary>
    [Export] public float QuietLanding { get; set; } = 3.0f;
    /// <summary>Landings faster than this sound heavy, in m/s.</summary>
    [Export] public float HardLanding { get; set; } = 8.0f;

    private const string Impact = "res://audio/sfx/kenney_impact_sounds/";
    private const string Rpg = "res://audio/sfx/kenney_rpg_audio/";
    private const string Ui = "res://audio/sfx/kenney_interface_sounds/";

    private readonly Dictionary<string, AudioStream[]> _sets = new();
    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[6];
    private readonly RandomNumberGenerator _random = new();
    private int _next;
    private bool _enabled;

    [Export] public float ReportAfter { get; set; } = 10.0f;

    /// <summary>`-- --audio-report`: prints buses, every audio player and the output level (to check a build).</summary>
    private void Report()
    {
        GD.Print($"Audio report: device '{AudioServer.OutputDevice}', mix {AudioServer.GetMixRate()} Hz, enabled {_enabled}");
        for (int bus = 0; bus < AudioServer.BusCount; bus++)
        {
            GD.Print($"  bus {AudioServer.GetBusName(bus)}: {AudioServer.GetBusVolumeDb(bus):0.0} dB, mute {AudioServer.IsBusMute(bus)}, peak {AudioServer.GetBusPeakVolumeLeftDb(bus, 0):0.0} dB");
        }
        foreach (Node node in GetTree().Root.FindChildren("*", "AudioStreamPlayer", true, false))
        {
            if (node is AudioStreamPlayer player)
            {
                GD.Print($"  player {player.GetPath()}: bus {player.Bus}, {player.VolumeDb:0.0} dB, playing {player.Playing}, stream {player.Stream?.ResourcePath ?? "none"}");
            }
        }
        foreach (Node node in GetTree().Root.FindChildren("*", "AudioStreamPlayer3D", true, false))
        {
            if (node is AudioStreamPlayer3D player)
            {
                GD.Print($"  player3D {player.GetPath()}: bus {player.Bus}, {player.VolumeDb:0.0} dB, playing {player.Playing}");
            }
        }
    }

    public override void _Ready()
    {
        _random.Randomize();
        // Headless runs (tests) have no audio output and never release playing streams.
        _enabled = DisplayServer.GetName() != "headless";
        Load("land", Impact, "impactSoft_medium_00{0}.ogg", 5);
        Load("land_hard", Impact, "impactSoft_heavy_00{0}.ogg", 5);
        Load("fall", Impact, "impactPunch_heavy_00{0}.ogg", 5);
        Load("pickup", Rpg, "cloth{0}.ogg", 4, 1);
        Load("eat", Rpg, "handleSmallLeather{0}.ogg", 2, 1, true);
        Load("drink", Ui, "glass_00{0}.ogg", 6, 1);
        Load("pack_open", Rpg, "beltHandle1.ogg", 1);
        Load("pack_close", Rpg, "beltHandle2.ogg", 1);
        Load("map_open", Rpg, "bookOpen.ogg", 1);
        Load("map_close", Rpg, "bookClose.ogg", 1);
        Load("boon", Ui, "confirmation_00{0}.ogg", 4, 1);
        Load("station", Ui, "bong_001.ogg", 1);
        Load("run_end", Impact, "impactBell_heavy_00{0}.ogg", 5);
        for (int i = 0; i < _players.Length; i++)
        {
            _players[i] = new AudioStreamPlayer { Bus = Bus };
            AddChild(_players[i]);
        }
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--audio-report") >= 0)
        {
            GetTree().CreateTimer(ReportAfter).Timeout += Report;
        }

        GameEvents.Landed += OnLanded;
        GameEvents.DownedChanged += OnDowned;
        GameEvents.ItemPickedUp += OnPickedUp;
        GameEvents.ItemConsumed += OnConsumed;
        GameEvents.PackingModeChanged += OnPacking;
        GameEvents.MapOpened += OnMap;
        GameEvents.BoonChosen += OnBoon;
        GameEvents.StationReached += OnStation;
        GameEvents.RunEnded += OnRunEnded;
    }

    public override void _ExitTree()
    {
        GameEvents.Landed -= OnLanded;
        GameEvents.DownedChanged -= OnDowned;
        GameEvents.ItemPickedUp -= OnPickedUp;
        GameEvents.ItemConsumed -= OnConsumed;
        GameEvents.PackingModeChanged -= OnPacking;
        GameEvents.MapOpened -= OnMap;
        GameEvents.BoonChosen -= OnBoon;
        GameEvents.StationReached -= OnStation;
        GameEvents.RunEnded -= OnRunEnded;
        foreach (AudioStreamPlayer player in _players)
        {
            if (player != null)
            {
                player.Stop();
                player.Stream = null;
            }
        }
        _sets.Clear();
    }

    /// <summary>Loads a numbered set of sounds (pattern with {0}); a missing file is skipped.</summary>
    private void Load(string key, string folder, string pattern, int count, int first = 0, bool skipFirstSuffix = false)
    {
        List<AudioStream> found = new();
        for (int i = first; i < first + count; i++)
        {
            string file = string.Format(pattern, i);
            if (skipFirstSuffix && i == first)
            {
                file = pattern.Replace("{0}", "");
            }
            if (ResourceLoader.Exists(folder + file) && ResourceLoader.Load<AudioStream>(folder + file) is AudioStream stream)
            {
                found.Add(stream);
            }
        }
        _sets[key] = found.ToArray();
    }

    /// <summary>Plays a random sound of a set (also used by tests to check the sets loaded).</summary>
    public bool Play(string key, float volumeDb = 0.0f)
    {
        if (!_sets.TryGetValue(key, out AudioStream[]? set) || set.Length == 0)
        {
            return false;
        }
        if (!_enabled)
        {
            return true;
        }
        AudioStreamPlayer player = _players[_next];
        _next = (_next + 1) % _players.Length;
        player.Stream = set[_random.RandiRange(0, set.Length - 1)];
        player.VolumeDb = VolumeDb + volumeDb;
        player.PitchScale = _random.RandfRange(0.92f, 1.08f);
        player.Play();
        return true;
    }

    public int SetSize(string key)
    {
        if (_sets.TryGetValue(key, out AudioStream[]? set))
        {
            return set.Length;
        }
        return 0;
    }

    private void OnLanded(float speed)
    {
        if (speed >= HardLanding)
        {
            Play("land_hard");
        }
        else if (speed >= QuietLanding)
        {
            Play("land", -4.0f);
        }
    }

    private void OnDowned(bool downed)
    {
        if (downed)
        {
            Play("fall");
        }
    }

    private void OnPickedUp(string itemId, string category)
    {
        Play("pickup");
    }

    private void OnConsumed(SupplyNeed need, string itemId, float restore)
    {
        if (need == SupplyNeed.Hunger)
        {
            Play("eat");
        }
        else
        {
            Play("drink", -6.0f);
        }
    }

    private void OnPacking(bool packing)
    {
        if (packing)
        {
            Play("pack_open");
        }
        else
        {
            Play("pack_close");
        }
    }

    private void OnMap(bool open)
    {
        if (open)
        {
            Play("map_open");
        }
        else
        {
            Play("map_close");
        }
    }

    private void OnBoon(string name)
    {
        Play("boon");
    }

    private void OnStation(int station)
    {
        Play("station");
    }

    private void OnRunEnded(RunSummary summary)
    {
        Play("run_end", 2.0f);
    }
}
