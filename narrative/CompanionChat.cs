using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Narrative;

/// <summary>
/// The companion's live conversation (M11), through the NobodyWho addon and a local language model
/// (a .gguf file). The model loads the first time the player talks (it takes video memory, so not
/// before). Every question goes with a short note of the carrier's situation (circle, depth, time,
/// weather, shelter, how they feel, what they wear and carry), so the replies fit the moment; the
/// reply streams in token by token (GameEvents.CompanionReplied). The persona lives in
/// narrative/data/companion_prompt.txt. A new run gives a new carrier, so the conversation starts
/// over. Without a model the companion stays silent and says so. Headless runs stay silent unless
/// `-- --llm-live` is given; `--llm-model=PATH` picks another model, `--no-llm` switches it off.
/// </summary>
public partial class CompanionChat : Node
{
    /// <summary>The model file: absolute, or relative to the project folder (or the exported game's folder).</summary>
    [Export] public string ModelPath { get; set; } = "../Models/qwen2.5-3b-instruct-q4_k_m.gguf";
    [Export(PropertyHint.File, "*.txt")] public string PromptPath { get; set; } = "res://narrative/data/companion_prompt.txt";
    /// <summary>Replies are cut off after this many tokens (keeps them short and quick).</summary>
    [Export] public int MaxReplyTokens { get; set; } = 90;
    /// <summary>
    /// While the companion thinks, the game draws at most this many frames a second, so the GPU is
    /// free for the model (the world is behind the conversation page then); 0 leaves it alone.
    /// </summary>
    [Export] public int ThinkingMaxFps { get; set; } = 15;
    [Export] public bool LoadOnStart { get; set; }
    /// <summary>What the companion "says" when there's no model to answer with.</summary>
    [Export] public string SilentReply { get; set; } = "The companion only listens, and says nothing.";

    public string ResolvedModelPath { get; private set; } = "";
    public string LastReply { get; private set; } = "";
    public int Replies { get; private set; }

    private GodotObject? _model;
    private GodotObject? _chat;
    private RunState? _run;
    private WorldClock? _clock;
    private VitalsSnapshot? _vitals;
    private string? _pending;
    private bool _resetPending;
    private bool _gone;

    public override void _Ready()
    {
        _run = GetNodeOrNull<RunState>("/root/RunState");
        _clock = GetNodeOrNull<WorldClock>("/root/WorldClock");
        GameEvents.AskRequested += OnAsk;
        GameEvents.ConversationChanged += OnConversation;
        GameEvents.VitalsUpdated += OnVitals;
        GameEvents.RespawnRequested += OnNewRun;
        ResolvedModelPath = FindModel();
        if (ResolvedModelPath.Length == 0)
        {
            GameEvents.PublishCompanion(CompanionState.Silent);
            return;
        }
        GameEvents.PublishCompanion(CompanionState.Asleep);
        if (LoadOnStart)
        {
            Wake();
        }
        // For checking a build: `-- --ask=TEXT` asks the companion once, a few seconds in (reply in
        // the log); `--llm-log=info` shows the addon's own log.
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--llm-log="))
            {
                ClassDB.ClassCallStatic("NobodyWho", "set_log_level", arg.Substring("--llm-log=".Length));
            }
            if (arg.StartsWith("--ask="))
            {
                string question = arg.Substring("--ask=".Length);
                GetTree().CreateTimer(DebugAskDelay).Timeout += () => GameEvents.RequestAsk(question);
            }
        }
    }

    [Export] public float DebugAskDelay { get; set; } = 4.0f;

    public override void _ExitTree()
    {
        _gone = true;
        GameEvents.AskRequested -= OnAsk;
        GameEvents.ConversationChanged -= OnConversation;
        GameEvents.VitalsUpdated -= OnVitals;
        GameEvents.RespawnRequested -= OnNewRun;
        if (_chat != null && GameEvents.Companion == CompanionState.Thinking)
        {
            _chat.Call("stop_generation");
        }
        if (GameEvents.Companion == CompanionState.Thinking && ThinkingMaxFps > 0)
        {
            Engine.MaxFps = _normalMaxFps;
        }
        _chat = null;
        _model = null;
        GameEvents.PublishCompanion(CompanionState.Silent);
    }

    /// <summary>The model file to use, or "" when there's none (or conversation is switched off).</summary>
    private string FindModel()
    {
        string[] args = OS.GetCmdlineUserArgs();
        bool live = false;
        string path = ModelPath;
        foreach (string arg in args)
        {
            if (arg == "--no-llm")
            {
                return "";
            }
            if (arg == "--llm-live")
            {
                live = true;
            }
            if (arg.StartsWith("--llm-model="))
            {
                path = arg.Substring("--llm-model=".Length);
            }
        }
        if (DisplayServer.GetName() == "headless" && !live)
        {
            return "";
        }
        List<string> candidates = new();
        if (path.IsAbsolutePath() && !path.StartsWith("res://") && !path.StartsWith("user://"))
        {
            candidates.Add(path);
        }
        else
        {
            candidates.Add(ProjectSettings.GlobalizePath("res://").PathJoin(path).SimplifyPath());
            string exeDir = OS.GetExecutablePath().GetBaseDir();
            candidates.Add(exeDir.PathJoin(path).SimplifyPath());
            candidates.Add(exeDir.PathJoin("Models").PathJoin(path.GetFile()));
        }
        foreach (string candidate in candidates)
        {
            if (FileAccess.FileExists(candidate))
            {
                return candidate;
            }
        }
        GD.Print($"Companion: no language model found (looked for {path}); conversation stays silent.");
        return "";
    }

    /// <summary>Loads the model and opens a chat with the companion's persona.</summary>
    public async void Wake()
    {
        if (GameEvents.Companion != CompanionState.Asleep)
        {
            return;
        }
        GameEvents.PublishCompanion(CompanionState.Waking);
        ulong started = Time.GetTicksMsec();
        string persona = "";
        using (FileAccess? file = FileAccess.Open(PromptPath, FileAccess.ModeFlags.Read))
        {
            if (file != null)
            {
                persona = file.GetAsText().StripEdges();
            }
        }
        Variant model = await Settle(ClassDB.ClassCallStatic("NobodyWhoModel", "create", ResolvedModelPath, new Godot.Collections.Dictionary()));
        if (_gone)
        {
            return;
        }
        if (model.VariantType != Variant.Type.Object || model.AsGodotObject() == null)
        {
            GD.PushWarning($"Companion: couldn't load {ResolvedModelPath}");
            GameEvents.PublishCompanion(CompanionState.Silent);
            Answer(SilentReply);
            return;
        }
        _model = model.AsGodotObject();
        Godot.Collections.Dictionary config = new() { { "system_prompt", persona } };
        Variant chat = await Settle(ClassDB.ClassCallStatic("NobodyWhoChat", "create", _model, config));
        if (_gone)
        {
            return;
        }
        if (chat.VariantType != Variant.Type.Object || chat.AsGodotObject() == null)
        {
            GD.PushWarning("Companion: couldn't open a chat with the model");
            GameEvents.PublishCompanion(CompanionState.Silent);
            Answer(SilentReply);
            return;
        }
        _chat = chat.AsGodotObject();
        GD.Print($"Companion: awake ({ResolvedModelPath.GetFile()}, {(Time.GetTicksMsec() - started) / 1000.0f:0.0} s)");
        GameEvents.PublishCompanion(CompanionState.Listening);
        TakePending();
    }

    private void OnAsk(string question)
    {
        question = question.StripEdges();
        if (question.Length == 0)
        {
            return;
        }
        switch (GameEvents.Companion)
        {
            case CompanionState.Silent:
                // A beat later, so the question is written down before the (non-)answer.
                CallDeferred(MethodName.Answer, SilentReply);
                break;
            case CompanionState.Asleep:
                _pending = question;
                Wake();
                break;
            case CompanionState.Listening:
                Ask(question);
                break;
            default:
                // Waking or still answering: the newest question waits its turn.
                _pending = question;
                break;
        }
    }

    private void TakePending()
    {
        if (_pending != null && GameEvents.Companion == CompanionState.Listening)
        {
            string question = _pending;
            _pending = null;
            Ask(question);
        }
    }

    private void Answer(string text)
    {
        LastReply = text;
        Replies++;
        GameEvents.PublishCompanionReply(text, true);
    }

    /// <summary>Asks the model, with the situation note, and streams the reply out.</summary>
    private async void Ask(string question)
    {
        if (_chat == null)
        {
            return;
        }
        if (_resetPending)
        {
            _resetPending = false;
            _chat.Call("reset_history");
        }
        SetThinking(true);
        ulong started = Time.GetTicksMsec();
        string prompt = $"{CurrentSituation()}\n{question}";
        Variant streamValue = _chat.Call("ask", prompt);
        GodotObject? stream = streamValue.AsGodotObject();
        GD.Print($"Companion: asking ({streamValue.VariantType}, stream {stream != null})");
        StringBuilder text = new();
        int tokens = 0;
        bool stopped = false;
        while (stream != null)
        {
            Variant token = await Settle(stream.Call("next_token"));
            if (_gone)
            {
                return;
            }
            if (token.VariantType == Variant.Type.Nil)
            {
                break;
            }
            if (stopped)
            {
                continue;
            }
            text.Append(token.AsString());
            tokens++;
            GameEvents.PublishCompanionReply(Clean(text.ToString()), false);
            if (tokens >= MaxReplyTokens)
            {
                // Long enough: stop the model, and let the stream run out.
                stopped = true;
                _chat?.Call("stop_generation");
            }
        }
        string reply = Clean(text.ToString());
        if (reply.Length == 0)
        {
            reply = "...";
        }
        GD.Print($"Companion: replied in {(Time.GetTicksMsec() - started) / 1000.0f:0.0} s ({tokens} tokens): {Clean(text.ToString())}");
        SetThinking(false);
        Answer(reply);
        TakePending();
    }

    /// <summary>
    /// Awaits NobodyWho's async results until a plain value comes back. Its calls return a task's
    /// "done" signal; the task is polled (is_done, result) once a frame while held here, rather than
    /// awaiting the signal: in release builds the signal could fire before it was awaited, or the
    /// task be freed with no one holding it, and the reply never came.
    /// </summary>
    private async Task<Variant> Settle(Variant value)
    {
        int guard = 0;
        while (value.VariantType == Variant.Type.Signal && guard < 8)
        {
            GodotObject? task = value.AsSignal().Owner;
            if (task == null || !IsInstanceValid(task))
            {
                return default;
            }
            while (!task.Call("is_done").AsBool())
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (_gone)
                {
                    return default;
                }
            }
            value = task.Call("result");
            guard++;
        }
        return value;
    }

    private int _normalMaxFps;

    /// <summary>Thinking (or not): the companion's state, and the frame cap that frees the GPU.</summary>
    private void SetThinking(bool thinking)
    {
        if (thinking)
        {
            _normalMaxFps = Engine.MaxFps;
            if (ThinkingMaxFps > 0 && DisplayServer.GetName() != "headless")
            {
                Engine.MaxFps = ThinkingMaxFps;
            }
            GameEvents.PublishCompanion(CompanionState.Thinking);
        }
        else
        {
            if (ThinkingMaxFps > 0 && DisplayServer.GetName() != "headless")
            {
                Engine.MaxFps = _normalMaxFps;
            }
            GameEvents.PublishCompanion(CompanionState.Listening);
        }
    }

    /// <summary>Trims the reply and drops any chat-format leftovers.</summary>
    public static string Clean(string text)
    {
        string cleaned = text.Replace("<|im_end|>", "").Replace("<|endoftext|>", "");
        return cleaned.StripEdges();
    }

    private void OnVitals(VitalsSnapshot vitals) => _vitals = vitals;

    /// <summary>Opening the conversation wakes the model, so it's ready by the time the question is typed.</summary>
    private void OnConversation(bool open)
    {
        if (open && GameEvents.Companion == CompanionState.Asleep)
        {
            Wake();
        }
    }

    private void OnNewRun()
    {
        _pending = null;
        if (_chat == null)
        {
            return;
        }
        if (GameEvents.Companion == CompanionState.Thinking)
        {
            _resetPending = true;
        }
        else
        {
            _chat.Call("reset_history");
        }
    }

    /// <summary>The note that goes with each question, from the game's current state.</summary>
    public string CurrentSituation()
    {
        int depth = 0;
        int circle = 0;
        string circleName = "";
        if (_run != null)
        {
            depth = _run.Depth;
            circle = _run.Circle;
            circleName = _run.Circles.NameOf(circle);
        }
        float hour = 12.0f;
        if (_clock != null)
        {
            hour = _clock.TimeOfDay;
        }
        List<string> carried = new();
        foreach (string id in GameEvents.CarriedItems)
        {
            carried.Add(ItemText.Find(id)?.DisplayName ?? id);
        }
        return Situation(circle, circleName, depth, hour, GameEvents.CurrentWeather.Kind, GameEvents.InShelter, _vitals,
            GameEvents.CurrentGear, GameEvents.CurrentLoad, carried);
    }

    /// <summary>Writes the situation note in words (the model reads it; the player never sees it).</summary>
    public static string Situation(int circle, string circleName, int depth, float hour, WeatherKind weather, bool sheltered,
        VitalsSnapshot? vitals, GearSnapshot gear, LoadSnapshot load, IReadOnlyList<string> carried)
    {
        List<string> parts = new();
        string place = $"circle {circle + 1}";
        if (circleName.Length > 0)
        {
            place += $", {circleName}";
        }
        parts.Add($"{place}, {depth} m below the summit");
        parts.Add(TimeWords(hour));
        parts.Add(WeatherWords(weather));
        if (sheltered)
        {
            parts.Add("resting at a station's fire");
        }
        else
        {
            parts.Add("out on the slope");
        }
        if (vitals is VitalsSnapshot v)
        {
            List<string> body = new();
            if (v.Health < v.MaxHealth * 0.35f)
            {
                body.Add("badly hurt");
            }
            else if (v.Health < v.MaxHealth * 0.75f)
            {
                body.Add("hurt");
            }
            if (v.Hunger < 25.0f)
            {
                body.Add("very hungry");
            }
            else if (v.Hunger < 50.0f)
            {
                body.Add("hungry");
            }
            if (v.Thirst < 25.0f)
            {
                body.Add("very thirsty");
            }
            else if (v.Thirst < 50.0f)
            {
                body.Add("thirsty");
            }
            if (v.Exhausted)
            {
                body.Add("exhausted");
            }
            else if (v.Stamina < v.MaxStamina * 0.4f)
            {
                body.Add("tired");
            }
            if (body.Count == 0)
            {
                body.Add("well enough");
            }
            parts.Add("the carrier is " + string.Join(", ", body));
        }
        List<string> worn = new();
        if (gear.Slots != null)
        {
            foreach (WornPiece piece in gear.Slots)
            {
                if (piece.Empty)
                {
                    continue;
                }
                string name = piece.Name.ToLowerInvariant();
                if (piece.Broken)
                {
                    name += " (worn through)";
                }
                else if (piece.Fraction < 0.3f)
                {
                    name += " (badly worn)";
                }
                worn.Add(name);
            }
        }
        if (worn.Count > 0)
        {
            parts.Add("wearing " + string.Join(", ", worn));
        }
        else
        {
            parts.Add("wearing only plain clothes");
        }
        string pack = $"pack weighs {load.Mass:0} kg";
        if (gear.Backpack.Broken)
        {
            pack += " and is torn";
        }
        else if (gear.Backpack.MaxDurability > 0.0f && gear.Backpack.Fraction < 0.3f)
        {
            pack += " and is fraying";
        }
        parts.Add(pack);
        if (carried.Count > 0)
        {
            parts.Add("carrying " + Tally(carried));
        }
        else
        {
            parts.Add("carrying nothing");
        }
        return "[" + string.Join("; ", parts) + "]";
    }

    /// <summary>"2 water flask, bread" from a list of names.</summary>
    private static string Tally(IReadOnlyList<string> names)
    {
        Dictionary<string, int> counts = new();
        List<string> order = new();
        foreach (string name in names)
        {
            if (!counts.ContainsKey(name))
            {
                counts[name] = 0;
                order.Add(name);
            }
            counts[name]++;
        }
        List<string> parts = new();
        foreach (string name in order)
        {
            if (counts[name] > 1)
            {
                parts.Add($"{counts[name]} {name.ToLowerInvariant()}");
            }
            else
            {
                parts.Add(name.ToLowerInvariant());
            }
        }
        return string.Join(", ", parts);
    }

    private static string TimeWords(float hour)
    {
        if (hour < 5.0f || hour >= 21.0f)
        {
            return "night";
        }
        if (hour < 8.0f)
        {
            return "dawn";
        }
        if (hour < 12.0f)
        {
            return "morning";
        }
        if (hour < 17.0f)
        {
            return "afternoon";
        }
        return "evening";
    }

    private static string WeatherWords(WeatherKind weather)
    {
        return weather switch
        {
            WeatherKind.Fog => "fog",
            WeatherKind.Rain => "rain",
            WeatherKind.Snow => "snow falling",
            WeatherKind.Wind => "strong wind",
            WeatherKind.Heat => "hot, dry air",
            _ => "clear sky",
        };
    }
}
