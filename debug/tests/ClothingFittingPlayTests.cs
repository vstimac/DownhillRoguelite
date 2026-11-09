using System.Collections.Generic;
using System.Threading.Tasks;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>
/// The keyboard clothing fitting scene: it finds pieces to fit; Tab picks the next piece; the arrows
/// shift it, Shift turns it, Ctrl stretches it, + sizes it, R resets it; O and C change the outfit and
/// the character and keep the scene usable; Ctrl+S saves fits that load back. The fit file is put back
/// as it was afterwards.
/// Run: godot --headless --path . --fixed-fps 60 res://debug/tests/ClothingFittingPlayTests.tscn
/// </summary>
public partial class ClothingFittingPlayTests : Node
{
    private readonly List<string> _failures = new();

    public override async void _Ready()
    {
        string file = ProjectSettings.GlobalizePath(ClothingFit.DefaultPath);
        byte[]? before = null;
        if (FileAccess.FileExists(ClothingFit.DefaultPath))
        {
            before = FileAccess.GetFileAsBytes(ClothingFit.DefaultPath);
        }
        ClothingFit.Reload();

        ClothingFittingPlay play = GD.Load<PackedScene>("res://debug/ClothingFittingPlay.tscn").Instantiate<ClothingFittingPlay>();
        AddChild(play);
        await Frames(20);
        Check("the scene sets up with pieces to fit", play.IsSetUp && play.Handles.Count > 0 && play.Selected != null,
            $"set up {play.IsSetUp}, {play.Handles.Count} pieces");
        if (play.Selected == null)
        {
            Finish(file, before);
            return;
        }

        Marker3D first = play.Selected;
        await Press(Key.Tab);
        bool picked = play.Handles.Count < 2 || play.Selected != first;
        await Press(Key.Tab, shift: true);
        Check("Tab and Shift+Tab move between pieces", picked && play.Selected == first, $"moved {picked}, back {play.Selected == first}");

        await Press(Key.Up);
        await Press(Key.Up);
        await Press(Key.Pageup);
        await Press(Key.Right, shift: true);
        await Press(Key.Up, ctrl: true);
        await Press(Key.Equal);
        Marker3D handle = play.Selected!;
        Vector3 position = handle.Position;
        Vector3 turn = handle.RotationDegrees;
        Vector3 size = handle.Scale;
        Check("arrows shift, Shift turns, Ctrl stretches and + sizes the piece",
            Mathf.IsEqualApprox(position.Y, 0.01f) && Mathf.IsEqualApprox(position.Z, 0.005f) && Mathf.IsEqualApprox(turn.Y, 5.0f)
            && Mathf.IsEqualApprox(size.Y, 1.04f) && Mathf.IsEqualApprox(size.X, 1.02f),
            $"shift {position}, turn {turn}, size {size}");

        string key = handle.GetMeta("clothing_fit_key", handle.Name.ToString()).AsString();
        string character = play.Fitting!.CharacterId;
        await Press(Key.S, ctrl: true);
        ClothingFit? saved = ResourceLoader.Load<ClothingFit>(ClothingFit.DefaultPath, null, ResourceLoader.CacheMode.Ignore);
        ClothingFitEntry? entry = saved?.Find(character, key);
        Check("Ctrl+S saves the fit and it loads back the same",
            entry != null && entry.Offset.IsEqualApprox(position) && entry.RotationDegrees.IsEqualApprox(turn) && entry.Scale.IsEqualApprox(size),
            $"message '{play.Message}', entry {entry != null} for {character} {key}");

        await Press(Key.R);
        Check("R resets the piece", play.Selected != null && play.Selected.Transform.IsEqualApprox(Transform3D.Identity), $"now {play.Selected?.Transform}");

        await Press(Key.O);
        await Frames(4);
        int afterOutfit = play.Handles.Count;
        await Press(Key.C);
        await Frames(4);
        bool characterChanged = play.Fitting.CharacterId != character;
        await Press(Key.Space);
        await Frames(2);
        Check("O and C change the outfit and the character and still find pieces",
            afterOutfit > 0 && play.Handles.Count > 0 && play.Selected != null,
            $"{afterOutfit} pieces after O, {play.Handles.Count} after C (character changed {characterChanged}, now {play.Fitting.CharacterId})");

        Finish(file, before);
    }

    private void Finish(string file, byte[]? before)
    {
        // Put the fit file back as it was.
        if (before != null)
        {
            using FileAccess? writer = FileAccess.Open(ClothingFit.DefaultPath, FileAccess.ModeFlags.Write);
            writer?.StoreBuffer(before);
        }
        else if (FileAccess.FileExists(ClothingFit.DefaultPath))
        {
            DirAccess.RemoveAbsolute(file);
        }
        ClothingFit.Reload();

        if (_failures.Count == 0)
        {
            GD.Print("CLOTHING FITTING PLAY TESTS: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.Print("CLOTHING FITTING PLAY TESTS: FAIL (" + string.Join(", ", _failures) + ")");
            GetTree().Quit(1);
        }
    }

    private async Task Press(Key key, bool shift = false, bool ctrl = false)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true, ShiftPressed = shift, CtrlPressed = ctrl });
        await Frames(2);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false, ShiftPressed = shift, CtrlPressed = ctrl });
        await Frames(2);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
