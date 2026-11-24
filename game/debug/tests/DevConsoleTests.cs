using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using DownhillRoguelite.Terrain;
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
