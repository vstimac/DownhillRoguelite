using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using DownhillRoguelite.Player;
using DownhillRoguelite.Wildlife;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The in-game developer console (an autoload): ` (the key left of 1) or F1 opens it. Type a command,
/// with or without the leading slash, and Enter; Up/Down walk the history, Tab completes a command's
/// name. /commands lists every command, /help &lt;command&gt; explains one. Other systems add their own
/// commands with DevConsole.Register (the fog adds /fog ...). While it is open the carrier doesn't
/// take keys and the mouse is free.
/// </summary>
public partial class DevConsole : CanvasLayer
{
    public sealed record Command(string Name, string Usage, string Help, Func<string[], string> Run);

    private static readonly SortedDictionary<string, Command> Commands = new(StringComparer.OrdinalIgnoreCase);

    [Export] public Key ToggleKey { get; set; } = Key.Quoteleft;
    [Export] public Key AltToggleKey { get; set; } = Key.F1;
    [Export] public int MaxLines { get; set; } = 400;
    [Export] public float Height { get; set; } = 0.45f;

    public static DevConsole? Instance { get; private set; }
    public bool Open { get; private set; }
    /// <summary>Health and stamina kept full (/god).</summary>
    public bool God { get; private set; }

    private PanelContainer? _panel;
    private RichTextLabel? _log;
    private LineEdit? _input;
    private Label? _fps;
    private readonly List<string> _history = new();
    private int _historyAt;
    private Input.MouseModeEnum _mouseBefore;
    private bool _inputBefore = true;
    private PlayerController? _player;

    /// <summary>Adds (or replaces) a command. <paramref name="run"/> gets the words after the name and returns what to print.</summary>
    public static void Register(string name, string usage, string help, Func<string[], string> run)
    {
        string key = name.TrimStart('/');
        Commands[key] = new Command(key, usage, help, run);
    }

    /// <summary>Runs a line as if typed; returns what it printed.</summary>
    public static string Execute(string line)
    {
        string[] words = line.Trim().TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return "";
        }
        if (!Commands.TryGetValue(words[0], out Command? command))
        {
            return $"Unknown command '{words[0]}'. /commands lists them all.";
        }
        try
        {
            return command.Run(words.Skip(1).ToArray());
        }
        catch (Exception error)
        {
            return $"{command.Name} failed: {error.Message}\nUsage: /{command.Usage}";
        }
    }

    public static IReadOnlyCollection<Command> All => Commands.Values;

    public override void _Ready()
    {
        Instance = this;
        Layer = 120;
        ProcessMode = ProcessModeEnum.Always;
        RegisterBuiltIns();
        Build();
        SetOpen(false);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Build()
    {
        _panel = new PanelContainer { Name = "Console", MouseFilter = Control.MouseFilterEnum.Stop };
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.05f, 0.05f, 0.06f, 0.9f), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8 });
        AddChild(_panel);
        VBoxContainer column = new();
        _panel.AddChild(column);
        _log = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SelectionEnabled = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
        };
        // Monospace, so /commands lines up in columns.
        _log.AddThemeFontOverride("normal_font", new SystemFont { FontNames = new[] { "Consolas", "DejaVu Sans Mono", "Liberation Mono", "Courier New", "monospace" } });
        _log.AddThemeFontSizeOverride("normal_font_size", 15);
        column.AddChild(_log);
        _input = new LineEdit { PlaceholderText = "type a command (/commands, /help <command>)", CaretBlink = true };
        _input.AddThemeFontSizeOverride("font_size", 16);
        _input.TextSubmitted += OnSubmitted;
        _input.GuiInput += OnInputKey;
        column.AddChild(_input);
        _fps = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Right };
        _fps.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _fps.Position = new Vector2(-520, 8);
        _fps.Size = new Vector2(510, 24);
        _fps.AddThemeColorOverride("font_color", new Color(1.0f, 1.0f, 0.6f));
        _fps.AddThemeColorOverride("font_outline_color", Colors.Black);
        _fps.AddThemeConstantOverride("outline_size", 4);
        AddChild(_fps);
        Print("[color=#c8b26a]Developer console.[/color] /commands lists every command, /help <command> explains one.");
        GetViewport().SizeChanged += Resize;
        Resize();
    }

    private void Resize()
    {
        if (_panel == null)
        {
            return;
        }
        Vector2 size = GetViewport().GetVisibleRect().Size;
        _panel.Position = Vector2.Zero;
        _panel.Size = new Vector2(size.X, size.Y * Height);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }
        if (key.PhysicalKeycode == ToggleKey || key.Keycode == AltToggleKey)
        {
            SetOpen(!Open);
            GetViewport().SetInputAsHandled();
        }
        else if (Open && key.Keycode == Key.Escape)
        {
            SetOpen(false);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Opens or closes the console.</summary>
    public void SetOpen(bool open)
    {
        if (_panel == null || _input == null)
        {
            return;
        }
        bool was = Open;
        Open = open;
        _panel.Visible = open;
        PlayerController? player = Player();
        if (open && !was)
        {
            _mouseBefore = Input.MouseMode;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            if (player != null)
            {
                _inputBefore = player.InputEnabled;
                player.InputEnabled = false;
            }
            GameEvents.PublishScreenOpen(true);
            _input.Text = "";
            _input.CallDeferred(Control.MethodName.GrabFocus);
        }
        else if (!open && was)
        {
            _input.ReleaseFocus();
            Input.MouseMode = _mouseBefore;
            if (player != null)
            {
                player.InputEnabled = _inputBefore;
            }
            GameEvents.PublishScreenOpen(false);
        }
    }

    private void OnSubmitted(string text)
    {
        if (_input == null)
        {
            return;
        }
        _input.Text = "";
        string line = text.Trim();
        if (line.Length == 0)
        {
            return;
        }
        _history.Remove(line);
        _history.Add(line);
        _historyAt = _history.Count;
        Print($"[color=#8fa8c8]> {Escape(line)}[/color]");
        string output = Execute(line);
        if (output.Length > 0)
        {
            Print(Escape(output));
        }
    }

    private void OnInputKey(InputEvent @event)
    {
        if (_input == null || @event is not InputEventKey key || !key.Pressed)
        {
            return;
        }
        if (key.Keycode == Key.Up && _history.Count > 0)
        {
            _historyAt = Mathf.Max(0, _historyAt - 1);
            _input.Text = _history[_historyAt];
            _input.CaretColumn = _input.Text.Length;
            _input.AcceptEvent();
        }
        else if (key.Keycode == Key.Down && _history.Count > 0)
        {
            _historyAt = Mathf.Min(_history.Count, _historyAt + 1);
            _input.Text = "";
            if (_historyAt < _history.Count)
            {
                _input.Text = _history[_historyAt];
            }
            _input.CaretColumn = _input.Text.Length;
            _input.AcceptEvent();
        }
        else if (key.Keycode == Key.Tab)
        {
            string typed = _input.Text.TrimStart('/');
            string[] matches = Commands.Keys.Where(name => name.StartsWith(typed, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 1)
            {
                _input.Text = "/" + matches[0] + " ";
            }
            else if (matches.Length > 1)
            {
                Print(string.Join("  ", matches.Select(name => "/" + name)));
            }
            _input.CaretColumn = _input.Text.Length;
            _input.AcceptEvent();
        }
    }

    public void Print(string bbcode)
    {
        if (_log == null)
        {
            return;
        }
        _log.AppendText(bbcode + "\n");
        if (_log.GetParagraphCount() > MaxLines)
        {
            _log.RemoveParagraph(0);
        }
    }

    private static string Escape(string text) => text.Replace("[", "[lb]");

    public override void _Process(double delta)
    {
        if (God)
        {
            PlayerController? player = Player();
            player?.Health?.Refill();
            player?.Stamina?.Refill();
        }
        if (_fps != null && _fps.Visible)
        {
            double frame = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
            double physics = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
            _fps.Text = $"{Engine.GetFramesPerSecond():0} fps  process {frame:0.0} ms  physics {physics:0.0} ms";
        }
    }

    /// <summary>The carrier, found once (again if the scene changed).</summary>
    public PlayerController? Player()
    {
        if (_player != null && IsInstanceValid(_player) && _player.IsInsideTree())
        {
            return _player;
        }
        _player = Find<PlayerController>(GetTree().Root);
        return _player;
    }

    public T? Find<T>(Node from) where T : class
    {
        if (from is T found)
        {
            return found;
        }
        foreach (Node child in from.GetChildren())
        {
            T? inner = Find<T>(child);
            if (inner != null)
            {
                return inner;
            }
        }
        return null;
    }

    private static float Number(string[] args, int index, float fallback)
    {
        if (index < args.Length && float.TryParse(args[index].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            return value;
        }
        if (index < args.Length)
        {
            throw new ArgumentException($"'{args[index]}' is not a number");
        }
        return fallback;
    }

    private PlayerController Carrier()
    {
        return Player() ?? throw new InvalidOperationException("no carrier in this scene");
    }

    private void RegisterBuiltIns()
    {
        Register("help", "help [command]", "Explains a command; without one, how the console works.", args =>
        {
            if (args.Length == 0)
            {
                return "Type a command and press Enter (the slash is optional). Up/Down: history. Tab: complete a name. ` or F1: close.\n"
                    + "/commands lists every command; /help <command> shows its usage.";
            }
            string name = args[0].TrimStart('/');
            if (!Commands.TryGetValue(name, out Command? command))
            {
                return $"No command '{name}'.";
            }
            return $"/{command.Usage}\n  {command.Help}";
        });
        Register("commands", "commands", "Lists every command with a line about it.", args =>
            string.Join("\n", Commands.Values.Select(c => $"/{c.Usage,-32} {c.Help}")));
        Register("clear", "clear", "Clears the console.", args =>
        {
            _log?.Clear();
            return "";
        });
        Register("heal", "heal", "Fills health and stamina.", args =>
        {
            PlayerController player = Carrier();
            player.Health?.Refill();
            player.Stamina?.Refill();
            return "Health and stamina full.";
        });
        Register("god", "god", "Toggles keeping health and stamina full.", args =>
        {
            God = !God;
            if (God)
            {
                return "God mode on.";
            }
            return "God mode off.";
        });
        Register("pos", "pos", "Prints where the carrier stands and faces.", args =>
        {
            PlayerController player = Carrier();
            Vector3 at = player.GlobalPosition;
            return $"at ({at.X:0.0}, {at.Y:0.0}, {at.Z:0.0}), facing {Mathf.RadToDeg(player.Rotation.Y):0}°";
        });
        Register("tp", "tp <x> <y> <z> | tp ahead <m>", "Moves the carrier to a point, or that far ahead (onto the ground).", args =>
        {
            PlayerController player = Carrier();
            Vector3 to;
            if (args.Length >= 1 && args[0] == "ahead")
            {
                Vector3 ahead = -player.GlobalBasis.Z;
                ahead.Y = 0.0f;
                to = player.GlobalPosition + ahead.Normalized() * Number(args, 1, 10.0f);
                Godot.Collections.Dictionary hit = player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(to + Vector3.Up * 200.0f, to + Vector3.Down * 400.0f, 1));
                if (hit.Count > 0)
                {
                    to = (Vector3)hit["position"];
                }
            }
            else if (args.Length >= 3)
            {
                to = new Vector3(Number(args, 0, 0.0f), Number(args, 1, 0.0f), Number(args, 2, 0.0f));
            }
            else
            {
                return "Usage: /tp <x> <y> <z> | /tp ahead <m>";
            }
            player.Teleport(to + Vector3.Up * 0.1f, player.Rotation.Y);
            return $"Moved to ({to.X:0.0}, {to.Y:0.0}, {to.Z:0.0}).";
        });
        Register("items", "items [filter]", "Lists the item ids /give takes.", args =>
        {
            string filter = "";
            if (args.Length > 0)
            {
                filter = args[0];
            }
            IEnumerable<string> ids = DirAccess.GetFilesAt("res://inventory/items")
                .Select(file => file.Replace(".remap", ""))
                .Where(file => file.EndsWith(".tres"))
                .Select(file => file[..^5])
                .Where(id => id.Contains(filter, StringComparison.OrdinalIgnoreCase));
            return string.Join("  ", ids);
        });
        Register("give", "give <item> [count]", "Puts an item (its id, see /items) in the pack.", args =>
        {
            if (args.Length == 0)
            {
                return "Usage: /give <item> [count]";
            }
            string path = $"res://inventory/items/{args[0]}.tres";
            if (!ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not ItemDefinition item)
            {
                return $"No item '{args[0]}'. /items lists them.";
            }
            CarrierInventory? inventory = Find<CarrierInventory>(GetTree().Root);
            if (inventory == null)
            {
                return "No inventory in this scene.";
            }
            int count = Mathf.Max(1, (int)Number(args, 1, 1.0f));
            int added = 0;
            for (int i = 0; i < count; i++)
            {
                if (inventory.TryAdd(item))
                {
                    added++;
                }
            }
            if (added < count)
            {
                return $"Added {added} of {count} {item.DisplayName} (no room for the rest).";
            }
            return $"Added {added} {item.DisplayName}.";
        });
        Register("enemies", "enemies", "Lists the enemy kinds /spawn takes.", args =>
        {
            EnemyDirector? director = Find<EnemyDirector>(GetTree().Root);
            if (director == null)
            {
                return "No enemy director in this scene.";
            }
            return string.Join("  ", director.Kinds.Select(kind => kind.Id).Distinct());
        });
        Register("spawn", "spawn <enemy> [metres]", "Brings an enemy (see /enemies) that far ahead (10 m).", args =>
        {
            EnemyDirector? director = Find<EnemyDirector>(GetTree().Root);
            if (director == null)
            {
                return "No enemy director in this scene.";
            }
            if (args.Length == 0)
            {
                return "Usage: /spawn <enemy> [metres]";
            }
            HumanoidEnemy? enemy = director.SpawnAhead(args[0], Number(args, 1, 10.0f));
            if (enemy == null)
            {
                return $"No enemy kind '{args[0]}'. /enemies lists them.";
            }
            return $"{enemy.Species?.DisplayName} is here.";
        });
        Register("wake", "wake <enemy>", "Wakes an enemy kind by the nearest tree, set on the carrier (as the jade urn does).", args =>
        {
            if (args.Length == 0)
            {
                return "Usage: /wake <enemy>";
            }
            GameEvents.RequestWake(args[0], "console");
            return $"Asked to wake {args[0]}.";
        });
        Register("killall", "killall", "Fells every enemy around.", args =>
        {
            int felled = 0;
            foreach (Node node in GetTree().GetNodesInGroup(IMeleeTarget.Group))
            {
                if (node is HumanoidEnemy enemy && enemy.CanBeHit)
                {
                    enemy.TakeHit(100000.0f, Vector3.Zero, "console");
                    felled++;
                }
            }
            return $"Felled {felled}.";
        });
        Register("time", "time <hour>", "Moves the clock forward to that hour (0-24).", args =>
        {
            WorldClock? clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
            if (clock == null)
            {
                return "No world clock.";
            }
            if (args.Length == 0)
            {
                return $"It's {clock.TimeOfDay:0.0}h, day {clock.Day}. Usage: /time <hour>";
            }
            float hour = Mathf.PosMod(Number(args, 0, 12.0f), 24.0f);
            clock.AdvanceHours(Mathf.PosMod(hour - clock.TimeOfDay, 24.0f));
            return $"It's {clock.TimeOfDay:0.0}h, day {clock.Day}.";
        });
        Register("timescale", "timescale <x>", "Speeds up or slows down the whole game (1 = normal).", args =>
        {
            Engine.TimeScale = Mathf.Clamp(Number(args, 0, 1.0f), 0.05f, 10.0f);
            return $"Time scale {Engine.TimeScale:0.00}.";
        });
        Register("fps", "fps", "Toggles the frame rate and frame times in the corner.", args =>
        {
            if (_fps == null)
            {
                return "";
            }
            _fps.Visible = !_fps.Visible;
            if (_fps.Visible)
            {
                return "Frame counter on.";
            }
            return "Frame counter off.";
        });
        Register("lock", "lock", "Locks on to the nearest enemy in view, or lets go (as Tab).", args =>
        {
            LockOn? lockOn = Find<LockOn>(GetTree().Root);
            if (lockOn == null)
            {
                return "No lock-on in this scene.";
            }
            lockOn.Toggle();
            if (GameEvents.LockTarget == null)
            {
                return "No lock.";
            }
            return $"Locked on to {GameEvents.LockTarget.Name}.";
        });
        Register("dive", "dive", "Dives forward (as Ctrl), stamina permitting.", args =>
        {
            Dive? dive = Find<Dive>(GetTree().Root);
            if (dive != null && dive.TryDive())
            {
                return "Diving.";
            }
            return "Can't dive now.";
        });
        Register("hint", "hint <text>", "Shows a hint on the screen.", args =>
        {
            GameEvents.PublishHint(string.Join(' ', args));
            return "";
        });
        Register("respawn", "respawn", "Respawns the carrier (as after a death).", args =>
        {
            GameEvents.PublishRespawnRequest();
            return "Respawning.";
        });
    }
}
