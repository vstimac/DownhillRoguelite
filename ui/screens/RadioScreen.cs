using System.Globalization;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The radio page (E at the summit's Reno 4, or K anywhere): the song playing and how far into it
/// (click or drag to jump), previous / play-pause / next, the volume, and where the songs come from:
/// the player's own folder, since the game ships none. Space plays or pauses while it's open.
/// Presentation only: it shows GameEvents.Radio and asks for everything through GameEvents.
/// </summary>
public partial class RadioScreen : ParchmentPanel
{
    private Label? _track;
    private Label? _count;
    private SliderRow? _progress;
    private SliderRow? _volume;
    private InkButton? _play;
    private Label? _source;

    public string TrackText => _track?.Text ?? "";
    public string PlayText => _play?.Text ?? "";
    public string SourceText => _source?.Text ?? "";

    public override void _Ready()
    {
        Title = "Radio";
        Closable = true;
        Seed = 41.0f;
        Size = new Vector2(540, 380);
        base._Ready();

        _track = UiStyle.MakeLabel("", UiStyle.TextSize + 4, true);
        _track.Position = new Vector2(26, 62);
        _track.Size = new Vector2(488, 34);
        _track.ClipText = true;
        _track.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        AddChild(_track);
        _count = UiStyle.MakeLabel("", UiStyle.SmallSize, false, UiStyle.InkSoft);
        _count.Position = new Vector2(26, 98);
        AddChild(_count);

        _progress = new SliderRow { Label = "0:00 / 0:00", Min = 0.0, Max = 1.0, StepSize = 0.001, Format = "{0:0}%", ShowScale = 100.0, LabelShare = 0.3f };
        _progress.Position = new Vector2(16, 126);
        _progress.Size = new Vector2(508, 40);
        _progress.Changed = value => GameEvents.RequestRadioSeek((float)value);
        AddChild(_progress);

        InkButton previous = new() { Glyph = InkButton.GlyphKind.Left, CustomMinimumSize = new Vector2(48, 48), Size = new Vector2(48, 48) };
        previous.Position = new Vector2(Size.X * 0.5f - 130, 178);
        previous.Pressed += () => GameEvents.CommandRadio(RadioCommand.Previous);
        AddChild(previous);
        _play = new InkButton { Text = "Play", Paper = true, FontSize = UiStyle.TextSize + 2, CustomMinimumSize = new Vector2(150, 48), Size = new Vector2(150, 48) };
        _play.Position = new Vector2(Size.X * 0.5f - 75, 178);
        _play.Pressed += () => GameEvents.CommandRadio(RadioCommand.Toggle);
        AddChild(_play);
        InkButton next = new() { Glyph = InkButton.GlyphKind.Right, CustomMinimumSize = new Vector2(48, 48), Size = new Vector2(48, 48) };
        next.Position = new Vector2(Size.X * 0.5f + 82, 178);
        next.Pressed += () => GameEvents.CommandRadio(RadioCommand.Next);
        AddChild(next);

        _volume = new SliderRow { Label = "Volume", Min = 0.0, Max = 1.0, StepSize = 0.05, Format = "{0:0}%", ShowScale = 100.0, Live = true, LabelShare = 0.3f };
        _volume.Position = new Vector2(16, 238);
        _volume.Size = new Vector2(508, 40);
        _volume.Changed = value => GameEvents.RequestRadioVolume((float)value);
        AddChild(_volume);

        _source = UiStyle.MakeLabel("", UiStyle.SmallSize, false, UiStyle.InkSoft);
        _source.Position = new Vector2(26, 284);
        _source.Size = new Vector2(488, 50);
        _source.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_source);
        InkButton open = new() { Text = "Open the folder", Paper = true, FontSize = UiStyle.SmallSize, CustomMinimumSize = new Vector2(150, 34), Size = new Vector2(150, 34) };
        open.Position = new Vector2(Size.X - 26 - 150 - 12 - 120, Size.Y - 50);
        open.Pressed += OpenFolder;
        AddChild(open);
        InkButton rescan = new() { Text = "Look again", Paper = true, FontSize = UiStyle.SmallSize, CustomMinimumSize = new Vector2(120, 34), Size = new Vector2(120, 34) };
        rescan.Position = new Vector2(Size.X - 26 - 120, Size.Y - 50);
        rescan.Pressed += () => GameEvents.CommandRadio(RadioCommand.Rescan);
        AddChild(rescan);

        GameEvents.RadioChanged += ShowState;
        ShowState(GameEvents.Radio);
        SetOpenNow(false);
    }

    public override void _ExitTree()
    {
        GameEvents.RadioChanged -= ShowState;
    }

    /// <summary>Shows the player's state: song, time, button, volume and where songs come from.</summary>
    public void ShowState(RadioState radio)
    {
        if (_track == null || _count == null || _progress == null || _volume == null || _play == null || _source == null)
        {
            return;
        }
        if (radio.HasTracks)
        {
            _track.Text = radio.TrackName;
            _count.Text = string.Format(CultureInfo.InvariantCulture, "song {0} of {1}", radio.Index + 1, radio.Count);
            _source.Text = "Your own songs, from " + radio.Folder + " (the game ships none).";
        }
        else
        {
            _track.Text = "The radio is quiet";
            _count.Text = "no songs yet";
            _source.Text = "Put MP3 files in " + radio.Folder + ", then Look again. The game ships no songs.";
        }
        _progress.Label = Clock(radio.Position) + " / " + Clock(radio.Length);
        bool dragging = _progress.HasFocus() && Input.IsMouseButtonPressed(MouseButton.Left);
        if (!dragging)
        {
            float fraction = 0.0f;
            if (radio.Length > 0.0f)
            {
                fraction = Mathf.Clamp(radio.Position / radio.Length, 0.0f, 1.0f);
            }
            _progress.Value = fraction;
        }
        _progress.QueueRedraw();
        if (!(_volume.HasFocus() && Input.IsMouseButtonPressed(MouseButton.Left)))
        {
            _volume.Value = radio.Volume;
            _volume.QueueRedraw();
        }
        if (radio.Playing)
        {
            _play.Text = "Pause";
        }
        else
        {
            _play.Text = "Play";
        }
        _play.QueueRedraw();
    }

    /// <summary>m:ss</summary>
    public static string Clock(float seconds)
    {
        int whole = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", whole / 60, whole % 60);
    }

    private void OpenFolder()
    {
        string folder = GameEvents.Radio.Folder;
        if (folder.Length > 0)
        {
            OS.ShellOpen(folder);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen || !Visible)
        {
            return;
        }
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Space)
        {
            GameEvents.CommandRadio(RadioCommand.Toggle);
            GetViewport().SetInputAsHandled();
        }
    }
}
