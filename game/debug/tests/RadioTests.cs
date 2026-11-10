using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Audio;
using DownhillRoguelite.Core;
using DownhillRoguelite.Environment;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Ui;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// Headless checks for the summit's Reno 4 and its radio: the car and the skeleton (rose in hand)
/// stand on the ground beside the start; walking up to the car focuses it and E starts "Reno 4" and
/// asks for the radio page; pause, play, next, previous (and previous restarting a song past a few
/// seconds), volume, seek and wrapping round all work; the page shows it; with no songs the radio
/// stays quiet and says where to put them. Uses three short generated WAVs in a test folder (the
/// player's own songs are never needed). If the player has MP3s in user://music, one is loaded too.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/RadioTests.tscn
/// </summary>
public partial class RadioTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }
    [Export] public SummitRadio? Summit { get; set; }
    [Export] public RadioPlayer? Radio { get; set; }
    /// <summary>The car must stand within this distance of the spawn, in m.</summary>
    [Export] public float MaxDistanceFromSpawn { get; set; } = 18.0f;

    private const string TestFolder = "user://radio_tests";
    private readonly List<string> _failures = new();
    private int _pageRequests;

    public override async void _Ready()
    {
        if (PlayerBody == null || Streamer == null || Summit == null || Radio == null)
        {
            GD.PushError("RadioTests: PlayerBody, Streamer, Summit and Radio are needed.");
            GetTree().Quit(1);
            return;
        }
        GameEvents.RadioPageRequested += OnPageRequested;
        string folder = MakeSongs();
        Radio.FoldersOverride = new List<string> { folder };
        Radio.Rescan();
        CanvasLayer layer = new();
        AddChild(layer);
        RadioScreen page = new();
        layer.AddChild(page);

        await WaitSeconds(1.0f);
        TestPlaced(Summit, Streamer);
        TestLevel(Summit, Streamer);
        await TestInteract(PlayerBody, Summit, Radio, page);
        await TestControls(Radio, page);
        TestEmpty(Radio, page);
        TestMp3();

        GameEvents.RadioPageRequested -= OnPageRequested;
        if (_failures.Count == 0)
        {
            GD.Print("RADIO TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print($"RADIO TESTS: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void OnPageRequested()
    {
        _pageRequests++;
    }

    private void TestPlaced(SummitRadio summit, TerrainStreamer streamer)
    {
        Check("car placed", summit.Placed && summit.Visible && summit.Car != null, $"placed {summit.Placed}");
        if (summit.Car == null || streamer.Generator == null)
        {
            return;
        }
        Vector3 car = summit.Car.GlobalPosition;
        float distance = new Vector2(car.X - streamer.SpawnPoint.X, car.Z - streamer.SpawnPoint.Z).Length();
        Check("car beside the start", distance <= MaxDistanceFromSpawn, $"{distance:0.0} m from the spawn");
        float ground = streamer.Generator.Height(car.X, car.Z);
        Check("car on the ground", Mathf.Abs(car.Y - ground) < 0.6f, $"car {car.Y:0.00}, ground {ground:0.00}");
        Check("car upright", summit.Car.GlobalBasis.Y.Dot(Vector3.Up) > 0.85f, $"up·Y {summit.Car.GlobalBasis.Y.Dot(Vector3.Up):0.00}");
        Check("car has its model", summit.Car.GetChildCount() >= 2, $"{summit.Car.GetChildCount()} children");
        Check("car is solid", summit.Car.FindChild("Body", true, false) is StaticBody3D, "static body");
        bool skeleton = summit.Skeleton != null && summit.Skeleton.IsInsideTree();
        Check("skeleton beside the car", skeleton, "skeleton in the tree");
        if (summit.Skeleton != null && summit.Rose != null)
        {
            float apart = summit.Skeleton.GlobalPosition.DistanceTo(car);
            Check("skeleton near the car", apart < 5.0f, $"{apart:0.0} m");
            Check("rose in his hand", summit.Skeleton.IsAncestorOf(summit.Rose) && summit.Rose.GetParent().Name == summit.HandNode, $"rose under {summit.Rose.GetParent().Name}");
        }
        else
        {
            Check("rose in his hand", false, "no skeleton or rose");
        }
    }

    /// <summary>
    /// The summit pad is level; the car's four wheels and the bench's four feet stand on the ground
    /// (none floats, none sinks), inside the pad; the skeleton has his head and chest to move; and no
    /// boulder, trunk, tree, grass or pebble lies on the pad.
    /// </summary>
    private void TestLevel(SummitRadio summit, TerrainStreamer streamer)
    {
        TerrainGenerator? generator = streamer.Generator;
        if (generator == null || summit.Car == null || summit.Bench == null)
        {
            Check("summit pad", false, "no generator, car or bench");
            return;
        }
        Vector3 pad = generator.SummitPad;
        float radius = generator.Settings.SummitPadRadius;
        float low = float.MaxValue;
        float high = float.MinValue;
        for (float x = -radius; x <= radius; x += 0.5f)
        {
            for (float z = -radius; z <= radius; z += 0.5f)
            {
                if (x * x + z * z > radius * radius)
                {
                    continue;
                }
                float h = generator.Height(pad.X + x, pad.Z + z);
                low = Mathf.Min(low, h);
                high = Mathf.Max(high, h);
            }
        }
        Check("summit pad is level", high - low < 0.02f, $"heights {low:0.000}..{high:0.000} over {radius:0.0} m");

        Vector2 carHalf = new(summit.CarSize.X * 0.5f, summit.CarSize.Z * 0.5f);
        Vector2 benchHalf = new(BenchHalfWidth * summit.BenchScale, BenchHalfDepth * summit.BenchScale);
        StandsOnGround("car's wheels on the ground", summit.Car.GlobalTransform, carHalf, summit.GroundSink, generator, pad, radius);
        StandsOnGround("bench's feet on the ground", summit.Bench.GlobalTransform, benchHalf, summit.GroundSink, generator, pad, radius);

        bool moving = summit.Skeleton?.FindChild(summit.HeadNode, true, false) != null && summit.Skeleton?.FindChild(summit.TorsoNode, true, false) != null;
        Check("skeleton has head and chest", moving, $"{summit.HeadNode}, {summit.TorsoNode}");
        if (summit.Skeleton != null)
        {
            float seat = summit.Skeleton.GlobalPosition.Y - summit.Bench.GlobalPosition.Y;
            Check("skeleton sits on the plank", Mathf.Abs(seat - SeatHeight * summit.BenchScale) < 0.05f, $"{seat:0.00} m above the bench's feet");
        }

        int onPad = 0;
        Vector2I first = generator.ChunkAt(pad.X - radius, pad.Z - radius);
        Vector2I last = generator.ChunkAt(pad.X + radius, pad.Z + radius);
        for (int cx = first.X; cx <= last.X; cx++)
        {
            for (int cz = first.Y; cz <= last.Y; cz++)
            {
                TerrainChunkData chunk = generator.BuildChunk(new Vector2I(cx, cz), Palette, 0);
                foreach (ObstacleData obstacle in chunk.Obstacles)
                {
                    if (OnPad(obstacle.Position, pad, radius))
                    {
                        onPad++;
                    }
                }
                foreach (PlantData plant in chunk.Plants)
                {
                    if (OnPad(plant.Position, pad, radius))
                    {
                        onPad++;
                    }
                }
            }
        }
        Check("nothing scattered on the pad", onPad == 0, $"{onPad} things on it");
    }

    // The Kenney bench (bench.glb, its own units): half its width and depth, and its seat's height.
    private const float BenchHalfWidth = 0.40f;
    private const float BenchHalfDepth = 0.22f;
    private const float SeatHeight = 0.237f;
    private static readonly Color[] Palette = { new(0.56f, 0.57f, 0.6f), new(0.68f, 0.6f, 0.48f), new(0.42f, 0.62f, 0.3f), new(0.9f, 0.95f, 1), new(0.4f, 0.3f, 0.2f) };

    private static bool OnPad(Vector3 at, Vector3 pad, float radius)
    {
        return new Vector2(at.X - pad.X, at.Z - pad.Z).Length() < radius;
    }

    /// <summary>All four corners of a footprint touch the ground (within a few cm of its sink) and lie on the pad.</summary>
    private void StandsOnGround(string name, Transform3D frame, Vector2 half, float sink, TerrainGenerator generator, Vector3 pad, float radius)
    {
        float worst = 0.0f;
        bool inside = true;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector3 corner = frame * new Vector3(sx * half.X, 0.0f, sz * half.Y);
                float gap = corner.Y + sink - generator.Height(corner.X, corner.Z);
                if (Mathf.Abs(gap) > Mathf.Abs(worst))
                {
                    worst = gap;
                }
                inside = inside && OnPad(corner, pad, radius);
            }
        }
        Check(name, Mathf.Abs(worst) < 0.04f && inside, $"worst corner {worst * 100.0f:0.0} cm off the ground, inside the pad {inside}");
    }

    private async Task TestInteract(PlayerController player, SummitRadio summit, RadioPlayer radio, RadioScreen page)
    {
        PickupInteractor? interactor = player.GetNodeOrNull<PickupInteractor>("Interactor");
        if (interactor == null || summit.Car == null)
        {
            Check("interactor", false, "no Interactor on the player");
            return;
        }
        Check("quiet before E", !GameEvents.Radio.Playing, $"playing {GameEvents.Radio.Playing}");
        Check("prompt asks him to play", summit.Prompt.Contains("play"), summit.Prompt);
        // Stand a step off the car's side, facing it.
        Transform3D car = summit.Car.GlobalTransform;
        Vector3 side = car.Basis.X.Normalized() * -(summit.CarSize.X * 0.5f + 1.0f);
        Vector3 stand = car.Origin + side;
        stand.Y = Streamer!.Generator!.Height(stand.X, stand.Z);
        Vector3 toCar = car.Origin - stand;
        float yaw = Mathf.Atan2(-toCar.X, -toCar.Z);
        player.Teleport(stand + Vector3.Up * 0.1f, yaw);
        await WaitSeconds(0.5f);
        Check("car focused", interactor.Focused == summit, $"focused {interactor.Focused}");
        bool used = interactor.InteractNow();
        await WaitSeconds(0.1f);
        Check("E starts the radio", used && GameEvents.Radio.Playing, $"used {used}, playing {GameEvents.Radio.Playing}");
        Check("he plays Reno 4", GameEvents.Radio.TrackName.Contains("Reno 4"), GameEvents.Radio.TrackName);
        Check("radio page asked for", _pageRequests == 1, $"{_pageRequests} requests");
        Check("page shows the song", page.TrackText.Contains("Reno 4") && page.PlayText == "Pause", $"'{page.TrackText}', button '{page.PlayText}'");
        Check("prompt opens the radio", summit.Prompt.Contains("radio"), summit.Prompt);
        await WaitSeconds(0.5f);
        Check("song moves on", GameEvents.Radio.Position > 0.3f, $"{GameEvents.Radio.Position:0.00} s");
    }

    private async Task TestControls(RadioPlayer radio, RadioScreen page)
    {
        GameEvents.CommandRadio(RadioCommand.Pause);
        float at = GameEvents.Radio.Position;
        await WaitSeconds(0.3f);
        Check("pause", !GameEvents.Radio.Playing && Mathf.IsEqualApprox(GameEvents.Radio.Position, at) && page.PlayText == "Play", $"playing {GameEvents.Radio.Playing}, {at:0.00} -> {GameEvents.Radio.Position:0.00}, button '{page.PlayText}'");
        GameEvents.CommandRadio(RadioCommand.Toggle);
        Check("play again", GameEvents.Radio.Playing && GameEvents.Radio.Position >= at, $"playing {GameEvents.Radio.Playing}");

        int first = GameEvents.Radio.Index;
        GameEvents.CommandRadio(RadioCommand.Next);
        Check("next", GameEvents.Radio.Index == (first + 1) % GameEvents.Radio.Count && GameEvents.Radio.Playing && GameEvents.Radio.Position < 0.1f, $"{first} -> {GameEvents.Radio.Index} '{GameEvents.Radio.TrackName}'");
        GameEvents.CommandRadio(RadioCommand.Previous);
        Check("previous", GameEvents.Radio.Index == first, $"back to {GameEvents.Radio.Index}");

        GameEvents.RequestRadioSeek(0.75f);
        float seeked = GameEvents.Radio.Position;
        Check("seek", Mathf.Abs(seeked - GameEvents.Radio.Length * 0.75f) < 0.05f, $"{seeked:0.00} of {GameEvents.Radio.Length:0.00} s");
        GameEvents.CommandRadio(RadioCommand.Previous);
        Check("previous restarts a song", GameEvents.Radio.Index == first && GameEvents.Radio.Position < 0.1f, $"song {GameEvents.Radio.Index} at {GameEvents.Radio.Position:0.00} s");

        // Wrap round both ways.
        int count = GameEvents.Radio.Count;
        for (int i = 0; i < count; i++)
        {
            GameEvents.CommandRadio(RadioCommand.Next);
        }
        Check("next wraps round", GameEvents.Radio.Index == first, $"after {count} nexts: {GameEvents.Radio.Index}");
        GameEvents.CommandRadio(RadioCommand.Previous);
        Check("previous wraps round", GameEvents.Radio.Index == (first - 1 + count) % count, $"{GameEvents.Radio.Index}");

        GameEvents.RequestRadioVolume(0.25f);
        AudioStreamPlayer? speaker = radio.GetNodeOrNull<AudioStreamPlayer>("Speaker");
        float expected = radio.FullVolumeDb + Mathf.LinearToDb(0.25f * 0.25f);
        Check("volume", Mathf.IsEqualApprox(GameEvents.Radio.Volume, 0.25f) && speaker != null && Mathf.Abs(speaker.VolumeDb - expected) < 0.1f && speaker.Bus == "Music", $"volume {GameEvents.Radio.Volume:0.00}, speaker {speaker?.VolumeDb:0.0} dB on {speaker?.Bus}");
        GameEvents.RequestRadioVolume(0.0f);
        Check("volume off", speaker != null && speaker.VolumeDb <= -79.0f, $"{speaker?.VolumeDb:0.0} dB");
        GameEvents.RequestRadioVolume(2.0f);
        Check("volume clamps", Mathf.IsEqualApprox(GameEvents.Radio.Volume, 1.0f), $"{GameEvents.Radio.Volume:0.00}");

        // A song that ends hands over to the next one.
        int before = GameEvents.Radio.Index;
        GameEvents.RequestRadioSeek(0.99f);
        await WaitSeconds(0.4f);
        Check("next song follows", GameEvents.Radio.Index == (before + 1) % count && GameEvents.Radio.Playing, $"{before} -> {GameEvents.Radio.Index}");
        GameEvents.CommandRadio(RadioCommand.Stop);
        Check("stop", !GameEvents.Radio.Playing, $"playing {GameEvents.Radio.Playing}");
    }

    private void TestEmpty(RadioPlayer radio, RadioScreen page)
    {
        string empty = ProjectSettings.GlobalizePath(TestFolder).PathJoin("empty");
        DirAccess.MakeDirRecursiveAbsolute(empty);
        radio.FoldersOverride = new List<string> { empty };
        radio.Rescan();
        GameEvents.CommandRadio(RadioCommand.Play);
        Check("no songs: quiet", !GameEvents.Radio.HasTracks && !GameEvents.Radio.Playing, $"tracks {GameEvents.Radio.HasTracks}, playing {GameEvents.Radio.Playing}");
        Check("no songs: says where to put them", page.SourceText.Contains("MP3") && SummitRadio.DetailsFor(GameEvents.Radio).Contains(GameEvents.Radio.Folder), page.SourceText);
    }

    /// <summary>The player's own MP3s load from their bytes (skipped when there are none).</summary>
    private void TestMp3()
    {
        foreach (string path in LocalMusicLibrary.Scan(new[] { LocalMusicLibrary.UserFolder }))
        {
            if (!path.EndsWith(".mp3"))
            {
                continue;
            }
            AudioStream? stream = LocalMusicLibrary.Load(path);
            Check("an MP3 from user://music loads", stream is AudioStreamMP3 && stream.GetLength() > 1.0, $"{LocalMusicLibrary.TrackName(path)}: {stream?.GetLength():0.0} s");
            return;
        }
        GD.Print("SKIP  MP3 loading: no MP3s in user://music");
    }

    /// <summary>Three 8-second WAVs (quiet tones), one named after the car's song.</summary>
    private static string MakeSongs()
    {
        string folder = ProjectSettings.GlobalizePath(TestFolder).PathJoin("songs");
        DirAccess.MakeDirRecursiveAbsolute(folder);
        string[] names = { "A Band - First Song (Live).wav", "Mile Kekin - Reno 4 (Official Video).wav", "Z Band - Last Song.wav" };
        for (int i = 0; i < names.Length; i++)
        {
            WriteTone(folder.PathJoin(names[i]), 220.0f * (i + 1), 8.0f);
        }
        return folder;
    }

    private static void WriteTone(string path, float hertz, float seconds)
    {
        const int rate = 8000;
        int samples = Mathf.RoundToInt(rate * seconds);
        using Godot.FileAccess? file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        if (file == null)
        {
            return;
        }
        file.StoreBuffer(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        file.Store32((uint)(36 + samples * 2));
        file.StoreBuffer(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        file.Store32(16);
        file.Store16(1);
        file.Store16(1);
        file.Store32(rate);
        file.Store32(rate * 2);
        file.Store16(2);
        file.Store16(16);
        file.StoreBuffer(System.Text.Encoding.ASCII.GetBytes("data"));
        file.Store32((uint)(samples * 2));
        for (int i = 0; i < samples; i++)
        {
            short value = (short)(Mathf.Sin(Mathf.Tau * hertz * i / rate) * 2000.0f);
            file.Store16((ushort)value);
        }
    }

    private async Task WaitSeconds(float seconds)
    {
        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += dt)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            GD.Print($"PASS  {name}: {detail}");
        }
        else
        {
            GD.Print($"FAIL  {name}: {detail}");
            _failures.Add(name);
        }
    }
}
