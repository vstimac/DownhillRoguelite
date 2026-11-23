using DownhillRoguelite.Core;
using DownhillRoguelite.Player;
using Godot;

namespace DownhillRoguelite.Inventory;

/// <summary>
/// Taking the pack off to repack it, anywhere on the mountain, with one key (Tab, Y on a controller).
/// A short press only asks for the quick look. Holding it makes the carrier set the pack down, which
/// takes time and fills a ring; letting go early leaves it on the back. Then the pack stands open
/// (GameEvents.IsPackOpen) and the pack page rearranges it through requests. The pack key, Esc or the
/// page's Done shoulders it again, which takes time too. I does the same without holding: it sets the
/// pack down (or shoulders it) on its own.
///
/// Nothing here holds the carrier longer than it looks: walking or jumping at any point ends it at
/// once (setting down is called off; an open or half-shouldered pack is swung onto the back while the
/// carrier walks on), and a press made while the pack is on its way is kept and acted on (a hold
/// while shouldering turns it back to the ground from where it is). While the pack is off the back
/// (GameEvents.IsPacking) movement and balance input are held.
/// </summary>
public partial class PackingMode : Node3D
{
    private static readonly StringName ActionToggle = "inventory_toggle_packing";
    private static readonly StringName ActionOpen = "inventory_open";
    private static readonly StringName ActionPause = "pause";
    private static readonly StringName ActionJump = "player_jump";
    private static readonly StringName MoveLeft = "player_move_left";
    private static readonly StringName MoveRight = "player_move_right";
    private static readonly StringName MoveForward = "player_move_forward";
    private static readonly StringName MoveBack = "player_move_back";

    /// <summary>The key the player used last for the pack, as written on it ("Tab", "I", "Y"), for the ring.</summary>
    public static string KeyLabel { get; private set; } = "Tab";
    /// <summary>True while the pack is going down on its own (I), so the ring doesn't say "hold".</summary>
    public static bool SettingDownByItself { get; private set; }

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
    [Export] public float SetDownTime { get; set; } = 1.2f;
    /// <summary>How long shouldering the pack again takes, in s.</summary>
    [Export] public float PutOnTime { get; set; } = 1.0f;
    /// <summary>A press that can't be acted on yet (the carrier is mid-fall, landing, sliding) is kept this long, in s.</summary>
    [Export] public float KeepPressFor { get; set; } = 1.0f;
    /// <summary>Stick or keys pushed this far count as walking off, 0..1.</summary>
    [Export] public float WalkOff { get; set; } = 0.5f;
    /// <summary>Delay before --open-pack opens the pack, in s.</summary>
    [Export] public float OpenPackDelay { get; set; } = 0.5f;

    /// <summary>The carrier's packing in this scene (null when there is none), for debug and shot tools.</summary>
    public static PackingMode? Current { get; private set; }

    public PackingPhase Phase { get; private set; } = PackingPhase.Carried;
    /// <summary>True while the pack stands open for rearranging.</summary>
    public bool Active => Phase == PackingPhase.Open;
    /// <summary>How many times walking or jumping ended a set-down, an open pack or a shouldering (for checks).</summary>
    public int Interrupts { get; private set; }

    /// <summary>The pack key is down and it isn't known yet whether it is a tap or a hold.</summary>
    private bool _pressed;
    private float _heldFor;
    private float _timer;
    /// <summary>Setting down goes on without the key held (I, or a rendered check).</summary>
    private bool _byItself;
    /// <summary>A set-down asked for with I that has to wait for the carrier to stand: seconds it is still kept.</summary>
    private float _queuedSetDown;
    /// <summary>A tap made while the pack was on its way: the quick look once it is on the back.</summary>
    private bool _queuedLook;
    /// <summary>The stick or keys were already pushed when this phase began (pushing on doesn't call it off; a new push does).</summary>
    private bool _moveLatched;

    public override async void _Ready()
    {
        // Setting down and the open pack keep working while the world is stopped for the page (PageFreeze).
        ProcessMode = ProcessModeEnum.Always;
        GameEvents.PackCloseRequested += BeginPutOn;
        Current = this;
        // For rendered checks (--write-movie): `-- --open-pack` opens the pack once the carrier has landed.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--open-pack") >= 0)
        {
            await ToSignal(GetTree().CreateTimer(OpenPackDelay), SceneTreeTimer.SignalName.Timeout);
            TryOpen();
        }
        // `-- --set-pack-down` sets it down in its own time, as if the key were held.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--set-pack-down") >= 0)
        {
            await ToSignal(GetTree().CreateTimer(OpenPackDelay), SceneTreeTimer.SignalName.Timeout);
            BeginSetDown(true);
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
        if (Current == this)
        {
            Current = null;
        }
        if (Phase != PackingPhase.Carried)
        {
            Close();
        }
        SettingDownByItself = false;
    }

    /// <summary>True while the pause menu or title screen holds the game (not the pages' own freeze).</summary>
    private bool MenuPaused => (GetTree().Paused && !GameEvents.PagesFrozen) || GameEvents.MenuOpen;

    public override void _Input(InputEvent @event)
    {
        if (MenuPaused || Phase == PackingPhase.Carried)
        {
            return;
        }
        // Esc closes the pack too, before the pause menu sees it.
        if (@event.IsActionPressed(ActionPause))
        {
            if (Phase == PackingPhase.Open)
            {
                BeginPutOn();
            }
            else if (Phase == PackingPhase.SettingDown)
            {
                CancelSetDown();
            }
            else
            {
                _pressed = false;
                _queuedLook = false;
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        // Walking or jumping off: ended here, before the body's physics frame reads the same press,
        // so the step or the jump happens at once. The event isn't taken.
        if (!@event.IsEcho() && (@event.IsActionPressed(ActionJump) || IsMoveKey(@event)))
        {
            Interrupt();
        }
    }

    private static bool IsMoveKey(InputEvent @event)
    {
        // Keys only; the stick is read each frame (WalkingOff), past a real push rather than the dead zone.
        if (@event is InputEventJoypadMotion)
        {
            return false;
        }
        return @event.IsActionPressed(MoveForward) || @event.IsActionPressed(MoveBack) || @event.IsActionPressed(MoveLeft) || @event.IsActionPressed(MoveRight);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (MenuPaused || @event.IsEcho())
        {
            return;
        }
        bool toggle = @event.IsActionPressed(ActionToggle);
        bool open = @event.IsActionPressed(ActionOpen);
        if (!toggle && !open)
        {
            return;
        }
        // Another page has the screen: the pack waits its turn.
        if (Phase == PackingPhase.Carried && (GameEvents.IsScreenOpen || GameEvents.IsMapOpen))
        {
            return;
        }
        KeyLabel = LabelOf(@event, open);
        if (open)
        {
            PressOpen();
        }
        else
        {
            PressToggle();
        }
        GetViewport().SetInputAsHandled();
    }

    /// <summary>The pack key went down (Tab, Y): a tap looks, a hold sets the pack down; on an open pack it shoulders it.</summary>
    private void PressToggle()
    {
        switch (Phase)
        {
            case PackingPhase.Carried:
            case PackingPhase.PuttingOn:
                // While shouldering, a hold turns the pack back to the ground and a tap is kept for a look afterwards.
                _pressed = true;
                _heldFor = 0.0f;
                break;
            case PackingPhase.Open:
                BeginPutOn();
                break;
            case PackingPhase.SettingDown:
                // Going down by itself (I): the pack key calls it off.
                if (_byItself)
                {
                    CancelSetDown();
                }
                break;
        }
    }

    /// <summary>I: the same as holding the pack key, without holding it; again to change one's mind.</summary>
    private void PressOpen()
    {
        switch (Phase)
        {
            case PackingPhase.Carried:
                if (!BeginSetDown(true))
                {
                    _queuedSetDown = KeepPressFor;
                }
                break;
            case PackingPhase.SettingDown:
                if (_byItself)
                {
                    CancelSetDown();
                }
                else
                {
                    // Pressed while holding Tab: the hold may end now, the pack goes down anyway.
                    _byItself = true;
                    SettingDownByItself = true;
                }
                break;
            case PackingPhase.Open:
                BeginPutOn();
                break;
            case PackingPhase.PuttingOn:
                TurnBack(true);
                break;
        }
    }

    private static string LabelOf(InputEvent @event, bool open)
    {
        if (@event is InputEventJoypadButton)
        {
            return "Y";
        }
        if (open)
        {
            return "I";
        }
        return "Tab";
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        bool keyDown = Input.IsActionPressed(ActionToggle);
        if (Phase != PackingPhase.Carried && WalkingOff())
        {
            Interrupt();
        }
        switch (Phase)
        {
            case PackingPhase.Carried:
                if (_pressed)
                {
                    if (!keyDown)
                    {
                        // Let go early: only a look.
                        _pressed = false;
                        GameEvents.RequestPackQuickView();
                        break;
                    }
                    _heldFor += dt;
                    // Kept while the key stays down: the pack comes off as soon as the carrier stands.
                    if (_heldFor >= HoldThreshold && BeginSetDown(false))
                    {
                        _pressed = false;
                    }
                    break;
                }
                if (_queuedSetDown > 0.0f)
                {
                    _queuedSetDown -= dt;
                    if (BeginSetDown(true))
                    {
                        _queuedSetDown = 0.0f;
                    }
                    break;
                }
                if (_queuedLook)
                {
                    _queuedLook = false;
                    GameEvents.RequestPackQuickView();
                }
                break;
            case PackingPhase.SettingDown:
                if ((!keyDown && !_byItself) || !CanSetDown())
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
                if (_pressed)
                {
                    if (!keyDown)
                    {
                        _pressed = false;
                        _queuedLook = true;
                    }
                    else
                    {
                        _heldFor += dt;
                        if (_heldFor >= HoldThreshold)
                        {
                            _pressed = false;
                            TurnBack(false);
                            break;
                        }
                    }
                }
                _timer += dt;
                GameEvents.PublishPackingProgress(Mathf.Clamp(_timer / Mathf.Max(PutOnTime, 0.01f), 0.0f, 1.0f), false);
                if (_timer >= PutOnTime)
                {
                    Finish();
                }
                break;
        }
    }

    private bool MovePushed() => Input.GetVector(MoveLeft, MoveRight, MoveForward, MoveBack).Length() >= WalkOff;

    /// <summary>The stick or the keys pushed past WalkOff, newly in this phase (a push held from before counts once let go and pushed again).</summary>
    private bool WalkingOff()
    {
        if (!MovePushed())
        {
            _moveLatched = false;
            return false;
        }
        return !_moveLatched;
    }

    /// <summary>
    /// Walking or jumping off: nothing waits for the animation. Setting down is called off, and an
    /// open or half-shouldered pack is on the back again at once (PackSetDown swings the picture of
    /// it up while the carrier already walks).
    /// </summary>
    public void Interrupt()
    {
        switch (Phase)
        {
            case PackingPhase.SettingDown:
                Interrupts++;
                CancelSetDown();
                break;
            case PackingPhase.Open:
                Interrupts++;
                GameEvents.PublishPackOpen(false);
                Finish();
                break;
            case PackingPhase.PuttingOn:
                Interrupts++;
                Finish();
                break;
        }
        _queuedSetDown = 0.0f;
    }

    /// <summary>Starts setting the pack down; byItself goes on without the key held (I). False if the carrier can't right now.</summary>
    public bool BeginSetDown(bool byItself = true)
    {
        if (Phase != PackingPhase.Carried || !CanSetDown())
        {
            return false;
        }
        Phase = PackingPhase.SettingDown;
        _timer = 0.0f;
        _byItself = byItself;
        SettingDownByItself = byItself;
        _moveLatched = MovePushed();
        _queuedLook = false;
        GameEvents.PublishPackingMode(true);
        GameEvents.PublishPackingProgress(0.0f, true);
        return true;
    }

    /// <summary>A shouldering turned back to the ground from where the pack is now.</summary>
    private void TurnBack(bool byItself)
    {
        if (Phase != PackingPhase.PuttingOn || !CanSetDown())
        {
            return;
        }
        float done = Mathf.Clamp(_timer / Mathf.Max(PutOnTime, 0.01f), 0.0f, 1.0f);
        Phase = PackingPhase.SettingDown;
        _timer = (1.0f - done) * SetDownTime;
        _byItself = byItself;
        SettingDownByItself = byItself;
        _queuedLook = false;
        GameEvents.PublishPackingProgress(_timer / Mathf.Max(SetDownTime, 0.01f), true);
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
        _moveLatched = MovePushed();
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
        _byItself = false;
        SettingDownByItself = false;
        Phase = PackingPhase.Open;
        _moveLatched = MovePushed();
        GameEvents.PublishPackingProgress(-1.0f, true);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GameEvents.PublishPackOpen(true);
    }

    private void CancelSetDown()
    {
        Phase = PackingPhase.Carried;
        _pressed = false;
        _byItself = false;
        SettingDownByItself = false;
        GameEvents.PublishPackingProgress(-1.0f, true);
        GameEvents.PublishPackingMode(false);
    }

    private void Finish()
    {
        Phase = PackingPhase.Carried;
        _pressed = false;
        _byItself = false;
        SettingDownByItself = false;
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
