using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// Taking the pack off to repack it, anywhere on the mountain. A short press of the pack key (Tab)
/// only asks for the quick look. Holding it makes the carrier stop and set the pack down, which
/// takes time; then the pack stands open (GameEvents.IsPackOpen) and the packing page rearranges
/// it through requests. Tab, Esc or the page's Done puts it back on, which takes time again, so
/// repacking is never free in the middle of trouble. While the pack is off the back
/// (GameEvents.IsPacking) movement and balance input are held.
/// </summary>
public partial class PackingMode : Node3D
{
    private static readonly StringName ActionToggle = "inventory_toggle_packing";
    private static readonly StringName ActionPause = "pause";

    [Export] public CarrierInventory? Inventory { get; set; }
    [Export] public PlayerController? PlayerBody { get; set; }
    /// <summary>Kept for scenes that assign it; the view no longer leaves the carrier.</summary>
    [Export] public Camera3D? PlayerCamera { get; set; }
    /// <summary>Repack anywhere; off, only in a station's shelter.</summary>
    [Export] public bool AllowAnywhere { get; set; } = true;

    [ExportGroup("Timing")]
    /// <summary>Holding the key this long starts setting the pack down; a shorter press is the quick look, in s.</summary>
    [Export] public float HoldThreshold { get; set; } = 0.25f;
    /// <summary>How long setting the pack down takes while the key stays held, in s.</summary>
    [Export] public float SetDownTime { get; set; } = 1.5f;
    /// <summary>How long shouldering the pack again takes, in s.</summary>
    [Export] public float PutOnTime { get; set; } = 1.5f;
    /// <summary>Delay before --open-pack opens the pack, in s.</summary>
    [Export] public float OpenPackDelay { get; set; } = 0.5f;

    public PackingPhase Phase { get; private set; } = PackingPhase.Carried;
    /// <summary>True while the pack stands open for rearranging.</summary>
    public bool Active => Phase == PackingPhase.Open;

    private bool _pressed;
    private float _heldFor;
    private float _timer;

    public override async void _Ready()
    {
        // Setting down and the open pack keep working while the world is stopped for the page (PageFreeze).
        ProcessMode = ProcessModeEnum.Always;
        GameEvents.PackCloseRequested += BeginPutOn;
        // For rendered checks (--write-movie): `-- --open-pack` opens the pack once the carrier has landed.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--open-pack") >= 0)
        {
            await ToSignal(GetTree().CreateTimer(OpenPackDelay), SceneTreeTimer.SignalName.Timeout);
            TryOpen();
        }
        // `-- --pack-look` shows the quick look instead.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--pack-look") >= 0)
        {
            await ToSignal(GetTree().CreateTimer(OpenPackDelay), SceneTreeTimer.SignalName.Timeout);
            GameEvents.RequestPackQuickView();
        }
    }

    public override void _ExitTree()
    {
        GameEvents.PackCloseRequested -= BeginPutOn;
        if (Phase != PackingPhase.Carried)
        {
            Close();
        }
    }

    /// <summary>True while the pause menu or title screen holds the game (not the pages' own freeze).</summary>
    private bool MenuPaused => GetTree().Paused && !GameEvents.PagesFrozen;

    public override void _Input(InputEvent @event)
    {
        // Esc closes the pack too, before the pause menu sees it.
        if (MenuPaused || !@event.IsActionPressed(ActionPause))
        {
            return;
        }
        if (Phase == PackingPhase.Open)
        {
            BeginPutOn();
            GetViewport().SetInputAsHandled();
        }
        else if (Phase == PackingPhase.SettingDown)
        {
            CancelSetDown();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (MenuPaused || !@event.IsActionPressed(ActionToggle) || @event.IsEcho())
        {
            return;
        }
        // Another page has the screen: the pack key waits its turn.
        if (Phase == PackingPhase.Carried && (GameEvents.IsScreenOpen || GameEvents.IsMapOpen))
        {
            return;
        }
        if (Phase == PackingPhase.Open)
        {
            BeginPutOn();
        }
        else if (Phase == PackingPhase.Carried)
        {
            _pressed = true;
            _heldFor = 0.0f;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool keyDown = Input.IsActionPressed(ActionToggle);
        switch (Phase)
        {
            case PackingPhase.Carried:
                if (!_pressed)
                {
                    break;
                }
                if (!keyDown)
                {
                    // Let go early: only a look.
                    _pressed = false;
                    GameEvents.RequestPackQuickView();
                    break;
                }
                _heldFor += dt;
                if (_heldFor >= HoldThreshold)
                {
                    _pressed = false;
                    BeginSetDown();
                }
                break;
            case PackingPhase.SettingDown:
                if (!keyDown || !CanSetDown())
                {
                    CancelSetDown();
                    break;
                }
                _timer += dt;
                GameEvents.PublishPackingProgress(Mathf.Clamp(_timer / Mathf.Max(SetDownTime, 0.01f), 0.0f, 1.0f), true);
                if (_timer >= SetDownTime)
                {
                    OpenNow();
                }
                break;
            case PackingPhase.Open:
                // Knocked over, sliding, or out of the fire's shelter: the pack goes straight back on.
                if (!CanSetDown())
                {
                    Close();
                }
                break;
            case PackingPhase.PuttingOn:
                _timer += dt;
                GameEvents.PublishPackingProgress(Mathf.Clamp(_timer / Mathf.Max(PutOnTime, 0.01f), 0.0f, 1.0f), false);
                if (_timer >= PutOnTime)
                {
                    Finish();
                }
                break;
        }
    }

    /// <summary>Starts setting the pack down (as if the key were held); false if the carrier can't right now.</summary>
    public bool BeginSetDown()
    {
        if (Phase != PackingPhase.Carried || !CanSetDown())
        {
            return false;
        }
        Phase = PackingPhase.SettingDown;
        _timer = 0.0f;
        GameEvents.PublishPackingMode(true);
        GameEvents.PublishPackingProgress(0.0f, true);
        return true;
    }

    /// <summary>Closes the open pack and starts shouldering it.</summary>
    public void BeginPutOn()
    {
        if (Phase != PackingPhase.Open)
        {
            return;
        }
        Phase = PackingPhase.PuttingOn;
        _timer = 0.0f;
        GameEvents.PublishPackOpen(false);
        GameEvents.PublishPackingProgress(0.0f, false);
    }

    /// <summary>Opens the pack at once, without the wait (tests, --open-pack); returns whether it is open.</summary>
    public bool TryOpen()
    {
        if (Phase == PackingPhase.Open)
        {
            return true;
        }
        if (!CanSetDown())
        {
            return false;
        }
        if (Phase == PackingPhase.Carried)
        {
            GameEvents.PublishPackingMode(true);
        }
        OpenNow();
        return true;
    }

    /// <summary>Puts the pack back on at once, without the wait.</summary>
    public void Close()
    {
        if (Phase == PackingPhase.Carried)
        {
            return;
        }
        GameEvents.PublishPackOpen(false);
        Finish();
    }

    private void OpenNow()
    {
        Phase = PackingPhase.Open;
        GameEvents.PublishPackingProgress(-1.0f, true);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GameEvents.PublishPackOpen(true);
    }

    private void CancelSetDown()
    {
        Phase = PackingPhase.Carried;
        _pressed = false;
        GameEvents.PublishPackingProgress(-1.0f, true);
        GameEvents.PublishPackingMode(false);
    }

    private void Finish()
    {
        Phase = PackingPhase.Carried;
        _pressed = false;
        GameEvents.PublishPackingProgress(-1.0f, false);
        if (!GameEvents.IsScreenOpen && !GameEvents.IsMapOpen)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        GameEvents.PublishPackingMode(false);
    }

    /// <summary>The pack only comes off while the carrier stands on its feet (and in a shelter, if that is required).</summary>
    private bool CanSetDown()
    {
        if (Inventory?.Backpack == null || Inventory.Grid == null || PlayerBody == null)
        {
            return false;
        }
        if (!AllowAnywhere && !GameEvents.InShelter)
        {
            return false;
        }
        if (GameEvents.IsDowned || GameEvents.IsConversing)
        {
            return false;
        }
        LocomotionState state = PlayerBody.State;
        return state != LocomotionState.Fallen && state != LocomotionState.Airborne && state != LocomotionState.Sliding;
    }
}
