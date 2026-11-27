using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The developer console: it is there in every scene (an autoload); /commands lists every command and
/// /help explains one; an unknown command says so; /heal, /god, /give and /tp do what they say; other
/// systems can add their own commands.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/DevConsoleTests.tscn
/// </summary>
public partial class DevConsoleTests : Node3D
{
    [Export] public PlayerController? PlayerBody { get; set; }
    [Export] public TerrainStreamer? Streamer { get; set; }

    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        if (PlayerBody == null || Streamer == null)
        {
            GD.PushError("DevConsoleTests: PlayerBody and Streamer are needed.");
            GetTree().Quit(1);
            return;
        }
        PlayerBody.Teleport(Streamer.SpawnPoint + Vector3.Up * 0.1f, 0.0f);
        await Wait(1.0f);

        Check("the console is there in every scene", DevConsole.Instance != null, "");
        string list = DevConsole.Execute("/commands");
        bool listed = true;
        foreach (string name in new[] { "/help", "/commands", "/heal", "/god", "/give", "/spawn", "/tp", "/time", "/timescale", "/fps", "/killall" })
        {
            listed = listed && list.Contains(name);
        }
        Check("/commands lists every command", listed && DevConsole.All.Count >= 15, $"{DevConsole.All.Count} commands");
        string help = DevConsole.Execute("help give");
        Check("/help explains one (the slash is optional)", help.Contains("/give <item> [count]") && DevConsole.Execute("/help").Contains("/commands"), help);
        Check("an unknown command says so", DevConsole.Execute("/flyaway").Contains("Unknown command"), "");

        PlayerBody.Health!.TakeDamage(40.0f, "test");
        float hurt = PlayerBody.Health.Current;
        DevConsole.Execute("/heal");
        Check("/heal fills health", hurt < PlayerBody.Health.Max && Mathf.IsEqualApprox(PlayerBody.Health.Current, PlayerBody.Health.Max), $"{hurt:0} to {PlayerBody.Health.Current:0}");

        DevConsole.Execute("/god");
        PlayerBody.Health.TakeDamage(30.0f, "test");
        await Wait(0.1f);
        bool kept = Mathf.IsEqualApprox(PlayerBody.Health.Current, PlayerBody.Health.Max);
        DevConsole.Execute("/god");
        Check("/god keeps health full until it's toggled off", kept && DevConsole.Instance?.God == false, $"health {PlayerBody.Health.Current:0}");

        CarrierInventory? inventory = PlayerBody.GetNodeOrNull<CarrierInventory>("Inventory");
        int before = inventory?.AllItems.Count ?? 0;
        string given = DevConsole.Execute("/give ration 2");
        Check("/give puts items in the pack", inventory != null && inventory.AllItems.Count == before + 2, given);
        Check("/give with a wrong id says so", DevConsole.Execute("/give nonsense").Contains("No item"), "");

        Vector3 from = PlayerBody.GlobalPosition;
        string moved = DevConsole.Execute("/tp ahead 6");
        await Wait(0.3f);
        float distance = new Vector2(PlayerBody.GlobalPosition.X - from.X, PlayerBody.GlobalPosition.Z - from.Z).Length();
        Check("/tp ahead moves him onto the ground ahead", distance > 5.0f && distance < 7.0f, $"{moved} ({distance:0.0} m)");
        Check("a bad number says how to use it", DevConsole.Execute("/timescale fast").Contains("Usage"), "");

        DevConsole.Register("echo_test", "echo_test <words>", "Says the words back.", args => string.Join(' ', args));
        Check("other systems can add commands", DevConsole.Execute("/echo_test hi there") == "hi there" && DevConsole.Execute("/commands").Contains("/echo_test"), "");

        await TestKeepsTheKeyboard();
        await TestSpawnEverything();
        await TestTravel();

        if (_failures.Count == 0)
        {
            GD.Print("DEV CONSOLE TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("DEV CONSOLE TESTS: FAIL (" + string.Join(", ", _failures) + ")");
            GetTree().Quit(1);
        }
    }

    /// <summary>
    /// Open, the console keeps the keyboard until it is closed: a button that takes the focus (as the
    /// HUD's do when the pointer passes over them) doesn't take the typing, and after Enter the next
    /// line can be typed at once.
    /// </summary>
    private async Task TestKeepsTheKeyboard()
    {
        DevConsole console = DevConsole.Instance!;
        console.SetOpen(true);
        await Wait(0.1f);
        LineEdit line = console.Line!;
        bool focused = line.HasFocus() && line.IsEditing();

        Button thief = new() { FocusMode = Control.FocusModeEnum.All, Position = new Vector2(10, 400), Size = new Vector2(60, 30) };
        console.AddChild(thief);
        thief.GrabFocus();
        await Wait(0.05f);
        Type("ab");
        await Wait(0.05f);
        bool kept = line.HasFocus() && line.Text == "ab";

        line.Text = "";
        Type("/heal");
        Key(Godot.Key.Enter);
        await Wait(0.05f);
        Type("x");
        await Wait(0.05f);
        bool next = line.HasFocus() && line.Text == "x";

        thief.QueueFree();
        console.SetOpen(false);
        await Wait(0.05f);
        bool released = !line.HasFocus();
        Check("the open console has the keyboard", focused, $"focused and editing {focused}");
        Check("a button taking the focus doesn't take the typing", kept, $"typed text kept {kept}");
        Check("after Enter the next line is typed at once", next, $"next {next}");
        Check("closed, it lets the keyboard go", released, $"released {released}");
    }

    /// <summary>
    /// The long jumps for testing: to a station, a circle, a depth and further down; the ground there is
    /// built at once, so the carrier stands on it (doesn't fall through) and /where says where they are.
    /// </summary>
    private async Task TestTravel()
    {
        PlayerController player = PlayerBody!;
        TerrainStreamer streamer = Streamer!;
        TerrainGenerator generator = streamer.Generator!;
        string stations = DevConsole.Execute("/stations 6");
        Check("/stations lists the stations with their depth and circle", stations.Split('\n').Length == 6 && stations.Contains("circle 1 Limbo"), stations.Split('\n')[0]);

        string moved = DevConsole.Execute("/tp station 2");
        await Wait(1.0f);
        Vector3 fire = generator.Station(2);
        float fromFire = new Vector2(player.GlobalPosition.X - fire.X, player.GlobalPosition.Z - fire.Z).Length();
        Check("/tp station 2 sets the carrier by that station's fire, standing", fromFire < 8.0f && Standing(player, generator), $"{fromFire:0.0} m from the fire; {moved}");

        await Jump("/tp circle greed", 3 * 1050.0f, 3 * 1050.0f + 400.0f, "Greed");
        await Jump("/tp circle 6", 5 * 1050.0f, 5 * 1050.0f + 400.0f, "Heresy");
        await Jump("/tp depth 2500", 2500.0f, 2560.0f, "");
        float before = streamer.SpawnPoint.Y - player.GlobalPosition.Y;
        await Jump("/tp down 300", before + 300.0f, before + 360.0f, "");
        string far = DevConsole.Execute("/tp depth 8800");
        await Wait(1.0f);
        float deep = streamer.SpawnPoint.Y - player.GlobalPosition.Y;
        Check("/tp depth reaches the last circle too, standing", deep >= 8800.0f && deep < 8900.0f && Standing(player, generator), $"{deep:0} m; {far}");
        string biome = DevConsole.Execute("/tp biome " + generator.Settings.Biomes[2].Name);
        await Wait(0.5f);
        Check("/tp biome goes into that biome", biome.Contains(generator.Settings.Biomes[2].Name) && Standing(player, generator), biome);
        Check("a wrong circle or biome lists the right names", DevConsole.Execute("/tp circle nowhere").Contains("Limbo") && DevConsole.Execute("/tp biome").Contains(generator.Settings.Biomes[0].Name), "");
        string web = DevConsole.Execute("/tp web");
        Check("/tp web goes to a web, or says where webs are", web.Contains("spider web"), web);
        DevConsole.Execute("/respawn");
        await Wait(0.5f);
    }

    /// <summary>/enemies lists the enemies, the night beasts and the animals, and /spawn brings each kind, one or several.</summary>
    private async Task TestSpawnEverything()
    {
        EnemyDirector enemies = new() { Name = "Enemies", Species = new[] { GD.Load<EnemySpecies>("res://wildlife/enemies/ogre.tres") }, Wakeable = new[] { GD.Load<EnemySpecies>("res://wildlife/enemies/jade_glutton.tres") } };
        BeastDirector beasts = new() { Name = "Beasts", Roster = GD.Load<BeastRoster>("res://wildlife/default_beasts.tres") };
        WildlifeSpawner wildlife = new() { Name = "Wildlife", Table = GD.Load<WildlifeTable>("res://wildlife/default_wildlife.tres"), Spawning = false };
        AddChild(enemies);
        AddChild(beasts);
        AddChild(wildlife);
        await Wait(0.3f);
        string kinds = DevConsole.Execute("/enemies");
        bool listed = kinds.Contains("ogre") && kinds.Contains("jade_glutton") && kinds.Contains("she_wolf") && kinds.Contains("giant_spider") && kinds.Contains("deer") && kinds.Contains("wolf");
        Check("/enemies lists the enemies, the night beasts and the animals", listed, kinds.Replace('\n', ' '));

        int animals = wildlife.Animals.Count;
        string deer = DevConsole.Execute("/spawn deer 12 3");
        int beastCount = beasts.Beasts.Count;
        string wolf = DevConsole.Execute("/spawn she_wolf 15");
        string glutton = DevConsole.Execute("/spawn jade_glutton 18");
        await Wait(0.2f);
        bool spawned = wildlife.Animals.Count == animals + 3 && beasts.Beasts.Count == beastCount + 1 && glutton.Contains("is here");
        float ahead = wildlife.Animals.Count > 0 ? new Vector2(wildlife.Animals[^1].GlobalPosition.X - PlayerBody!.GlobalPosition.X, wildlife.Animals[^1].GlobalPosition.Z - PlayerBody.GlobalPosition.Z).Length() : 0.0f;
        Check("/spawn brings an animal herd, a night beast and an enemy ahead", spawned && ahead > 9.0f && ahead < 16.0f, $"{deer} / {wolf} / {glutton}; a deer {ahead:0.0} m away");
        Check("/spawn of an unknown kind says so", DevConsole.Execute("/spawn dragon").Contains("No kind"), "");
        DevConsole.Execute("/killall");
        enemies.QueueFree();
        beasts.QueueFree();
        wildlife.QueueFree();
        await Wait(0.2f);
    }

    private async Task Jump(string command, float minDepth, float maxDepth, string name)
    {
        PlayerController player = PlayerBody!;
        TerrainStreamer streamer = Streamer!;
        string said = DevConsole.Execute(command);
        await Wait(1.0f);
        float depth = streamer.SpawnPoint.Y - player.GlobalPosition.Y;
        string where = DevConsole.Execute("/where");
        bool fine = depth >= minDepth && depth <= maxDepth && Standing(player, streamer.Generator!) && where.Contains(name);
        Check($"{command} lands {minDepth:0}-{maxDepth:0} m down and the carrier stands there", fine, $"{depth:0} m; {where}");
    }

    /// <summary>On the ground a second after the jump (not fallen through it, not left in the air).</summary>
    private static bool Standing(PlayerController player, TerrainGenerator generator)
    {
        Vector3 at = player.GlobalPosition;
        return Mathf.Abs(at.Y - generator.Height(at.X, at.Z)) < 1.0f && player.IsOnFloor();
    }

    private void Type(string text)
    {
        foreach (char c in text)
        {
            GetViewport().PushInput(new InputEventKey { Pressed = true, Unicode = c, Keycode = Godot.Key.None });
            GetViewport().PushInput(new InputEventKey { Pressed = false, Unicode = c, Keycode = Godot.Key.None });
        }
    }

    private void Key(Godot.Key key)
    {
        GetViewport().PushInput(new InputEventKey { Pressed = true, Keycode = key, PhysicalKeycode = key });
        GetViewport().PushInput(new InputEventKey { Pressed = false, Keycode = key, PhysicalKeycode = key });
    }

    private async Task Wait(float seconds)
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
