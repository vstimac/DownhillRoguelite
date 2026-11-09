using System.Collections.Generic;
using System;

namespace DownhillRoguelite.Core;

/// <summary>
/// Static C# event hub that systems use to talk without referencing each other.
/// Subscribers must unsubscribe in _ExitTree, because these events outlive scenes.
/// </summary>
public static partial class GameEvents
{
    /// <summary>Raised by the player controller once per physics frame.</summary>
    public static event Action<LocomotionSnapshot>? LocomotionUpdated;

    /// <summary>Raised by the foot IK once per rendered frame, after the feet are placed.</summary>
    public static event Action<FootPlacementSnapshot>? FootPlacementUpdated;

    /// <summary>Raised by the inventory (a test load until I1) whenever what the carrier holds changes.</summary>
    public static event Action<LoadSnapshot>? LoadChanged;

    /// <summary>Raised by the balance system once per physics frame.</summary>
    public static event Action<BalanceSnapshot>? BalanceUpdated;

    /// <summary>The most recent load, so systems created after the last change can catch up.</summary>
    public static LoadSnapshot CurrentLoad { get; private set; } = LoadSnapshot.Empty;

    /// <summary>Raised when the carrier sets the pack down to repack it (true) or puts it back on (false).</summary>
    public static event Action<bool>? PackingModeChanged;

    /// <summary>Whether the carrier is packing; movement, balance input and debug keys stand down.</summary>
    public static bool IsPacking { get; private set; }

    public static void PublishPackingMode(bool packing)
    {
        IsPacking = packing;
        PackingModeChanged?.Invoke(packing);
    }

    /// <summary>Raised when the carrier touches down after being airborne, with the downward speed in m/s.</summary>
    public static event Action<float>? Landed;

    /// <summary>Raised when the carrier goes down as a ragdoll (true) and when it is back on its feet (false).</summary>
    public static event Action<bool>? DownedChanged;

    /// <summary>Whether the carrier is a ragdoll or getting up: no movement or balance input.</summary>
    public static bool IsDowned { get; private set; }

    /// <summary>Raised when the carrier takes damage, with the amount.</summary>
    public static event Action<float>? Damaged;

    /// <summary>Raised by the health system once per physics frame.</summary>
    public static event Action<VitalsSnapshot>? VitalsUpdated;

    /// <summary>Raised when the carrier died and should be brought back (debug respawn until runs exist).</summary>
    public static event Action? RespawnRequested;

    /// <summary>The carrier wants to eat or drink: (need, how much is missing, 0 to 100). The inventory answers.</summary>
    public static event Action<SupplyNeed, float>? ConsumeRequested;

    /// <summary>An item was eaten or drunk: (need, item id, how much it restores).</summary>
    public static event Action<SupplyNeed, string, float>? ItemConsumed;

    /// <summary>The carrier tried to eat or drink with nothing suitable packed.</summary>
    public static event Action<SupplyNeed>? ConsumeFailed;

    /// <summary>An item went into the pack from the world: (item id, category name).</summary>
    public static event Action<string, string>? ItemPickedUp;

    public static void RequestConsume(SupplyNeed need, float deficit)
    {
        ConsumeRequested?.Invoke(need, deficit);
    }

    public static void PublishItemConsumed(SupplyNeed need, string itemId, float restore)
    {
        ItemConsumed?.Invoke(need, itemId, restore);
    }

    public static void PublishConsumeFailed(SupplyNeed need)
    {
        ConsumeFailed?.Invoke(need);
    }

    public static void PublishItemPickedUp(string itemId, string category)
    {
        ItemPickedUp?.Invoke(itemId, category);
    }

    /// <summary>Whether the carrier is inside a station's shelter (warm, can pack and sleep).</summary>
    public static bool InShelter { get; private set; }

    /// <summary>The carrier entered (true) or left (false) a station's shelter: (inside, station index).</summary>
    public static event Action<bool, int>? ShelterChanged;

    /// <summary>The carrier reached a station for the first time this run: (station index).</summary>
    public static event Action<int>? StationReached;

    /// <summary>The carrier slept through the night: (hours slept).</summary>
    public static event Action<float>? Slept;

    /// <summary>The latest estimate to the next station, from the carrier's route estimator.</summary>
    public static event Action<RouteEstimate>? RouteEstimateUpdated;

    public static RouteEstimate LatestEstimate { get; private set; }

    public static void PublishShelter(bool inside, int station)
    {
        InShelter = inside;
        ShelterChanged?.Invoke(inside, station);
    }

    public static void PublishStationReached(int station)
    {
        StationReached?.Invoke(station);
    }

    public static void PublishSlept(float hours)
    {
        Slept?.Invoke(hours);
    }

    public static void PublishRouteEstimate(RouteEstimate estimate)
    {
        LatestEstimate = estimate;
        RouteEstimateUpdated?.Invoke(estimate);
    }

    /// <summary>
    /// Raised while the carrier is at the top of a stage with a choice of paths: the stage and an
    /// estimate per path (index = path). Lookout signs show them (M10).
    /// </summary>
    public static event Action<int, RouteEstimate[]>? RouteOptionsUpdated;

    public static void PublishRouteOptions(int stage, RouteEstimate[] options)
    {
        RouteOptionsUpdated?.Invoke(stage, options);
    }

    /// <summary>Raised when the carrier commits to a path of a stage (its kind), for the narrator and later systems.</summary>
    public static event Action<int, RouteKind>? RouteChosen;

    public static void PublishRouteChosen(int stage, RouteKind kind)
    {
        RouteChosen?.Invoke(stage, kind);
    }

    /// <summary>
    /// Raised when a new run begins on a new mountain, before RespawnRequested resets the systems:
    /// terrain rebuilds from the seed first, so everything placed afterwards lands on the new world.
    /// </summary>
    public static event Action<ulong>? NewMountain;

    public static void PublishNewMountain(ulong seed)
    {
        NewMountain?.Invoke(seed);
    }

    /// <summary>Raised when the carrier dies, with what killed it.</summary>
    public static event Action<string>? CarrierDied;

    public static void PublishCarrierDied(string cause)
    {
        CarrierDied?.Invoke(cause);
    }

    /// <summary>Raised once per run when it ends: the journey's summary.</summary>
    public static event Action<RunSummary>? RunEnded;

    public static void PublishRunEnded(RunSummary summary)
    {
        RunEnded?.Invoke(summary);
    }

    /// <summary>What the carrier is looking at and could interact with (null when nothing).</summary>
    public static object? Focused { get; private set; }

    /// <summary>Raised when the focused interactable changes (the UI tooltip).</summary>
    public static event Action<object?>? FocusChanged;

    public static void PublishFocus(object? focused)
    {
        Focused = focused;
        FocusChanged?.Invoke(focused);
    }

    /// <summary>Asks the inventory to use one carried item with this id (eat or drink it), e.g. from the hotbar.</summary>
    public static event Action<string>? UseItemRequested;

    public static void RequestUseItem(string itemId)
    {
        UseItemRequested?.Invoke(itemId);
    }

    /// <summary>What the carrier wears and what it does (M14).</summary>
    public static GearSnapshot CurrentGear { get; private set; } = GearSnapshot.None;

    /// <summary>Raised whenever gear is put on, taken off, worn down or mended.</summary>
    public static event Action<GearSnapshot>? GearChanged;

    public static void PublishGear(GearSnapshot gear)
    {
        CurrentGear = gear;
        GearChanged?.Invoke(gear);
    }

    /// <summary>Asks to put on a carried item (by id).</summary>
    public static event Action<string>? WearRequested;

    public static void RequestWear(string itemId) => WearRequested?.Invoke(itemId);

    /// <summary>Asks to take off what is worn in a slot (back into the pack, or pockets).</summary>
    public static event Action<EquipSlot>? TakeOffRequested;

    public static void RequestTakeOff(EquipSlot slot) => TakeOffRequested?.Invoke(slot);

    /// <summary>Asks to mend with a carried material (by id): the most worn thing it can mend.</summary>
    public static event Action<string>? MendRequested;

    public static void RequestMend(string materialId) => MendRequested?.Invoke(materialId);

    /// <summary>Raised when something is mended (what, how much) or a mend can't be done (reason in what, amount 0).</summary>
    public static event Action<string, float>? Mended;

    public static void PublishMended(string what, float amount) => Mended?.Invoke(what, amount);

    /// <summary>The player asks to drop one carried item with this id on the ground.</summary>
    public static event Action<string>? DropRequested;

    public static void RequestDrop(string itemId) => DropRequested?.Invoke(itemId);

    /// <summary>A short message for the player (shown as a note): confirmations and hints.</summary>
    public static event Action<string>? HintShown;

    public static void PublishHint(string text) => HintShown?.Invoke(text);

    /// <summary>The vault on offer right now (the Jump Over timing event), updated every physics frame.</summary>
    public static VaultCue CurrentVault { get; private set; } = VaultCue.None;

    public static void PublishVaultCue(VaultCue cue) => CurrentVault = cue;

    /// <summary>True while the carrier is vaulting: the controller stands aside and the vault moves the body.</summary>
    public static bool IsVaulting { get; private set; }

    /// <summary>Raised when a vault is tried: true if it cleared the obstacle, false if the carrier tripped.</summary>
    public static event Action<bool>? Vaulted;

    public static void PublishVaulting(bool vaulting) => IsVaulting = vaulting;

    public static void PublishVaulted(bool cleared) => Vaulted?.Invoke(cleared);

    /// <summary>True while a landing roll is primed (Space in the window while falling): the next landing is soft.</summary>
    public static bool RollPrimed { get; private set; }

    public static void PublishRollPrimed(bool primed) => RollPrimed = primed;

    /// <summary>Raised when a primed landing became a roll.</summary>
    public static event Action? Rolled;

    public static void PublishRolled() => Rolled?.Invoke();

    /// <summary>The player throws one carried item with this id out of the way (dragged off the inventory page).</summary>
    public static event Action<string>? ThrowRequested;

    public static void RequestThrow(string itemId) => ThrowRequested?.Invoke(itemId);

    /// <summary>Raised when an item was dropped on the ground.</summary>
    public static event Action<string>? ItemDropped;

    public static void PublishItemDropped(string itemId) => ItemDropped?.Invoke(itemId);

    /// <summary>Raised when a carried item is lost (falls out of a torn pack).</summary>
    public static event Action<string>? ItemLost;

    public static void PublishItemLost(string itemId) => ItemLost?.Invoke(itemId);

    /// <summary>True while a full-screen UI page (inventory, journal) is open: the mouse belongs to it.</summary>
    public static bool IsScreenOpen { get; private set; }

    public static void PublishScreenOpen(bool open)
    {
        IsScreenOpen = open;
    }

    /// <summary>True while the carrier holds the map up (slow walking, no leaning or bracing).</summary>
    public static bool IsMapOpen { get; private set; }

    /// <summary>Raised when the map is raised (true) or lowered (false).</summary>
    public static event Action<bool>? MapOpened;

    /// <summary>True while the world is stopped because the inventory or the pack is open (PageFreeze), not the pause menu.</summary>
    public static bool PagesFrozen { get; private set; }

    public static void PublishPagesFrozen(bool frozen) => PagesFrozen = frozen;

    public static void PublishMapOpen(bool open)
    {
        IsMapOpen = open;
        MapOpened?.Invoke(open);
    }

    /// <summary>Item ids the carrier owns right now, one entry per item (published by the inventory).</summary>
    public static IReadOnlyList<string> CarriedItems { get; private set; } = System.Array.Empty<string>();

    public static void PublishCarriedItems(IReadOnlyList<string> itemIds)
    {
        CarriedItems = itemIds;
    }

    /// <summary>Asks the inventory to use up one carried item with this id (a worn-out pencil).</summary>
    public static event Action<string>? ItemUseRequested;

    public static void RequestItemUse(string itemId)
    {
        ItemUseRequested?.Invoke(itemId);
    }

    /// <summary>Raised when the carrier takes a boon at a station (its name).</summary>
    public static event Action<string>? BoonChosen;

    public static void PublishBoonChosen(string name)
    {
        BoonChosen?.Invoke(name);
    }

    /// <summary>Raised to put items in the carrier's pack (resource paths), e.g. the starting kit.</summary>
    public static event Action<string[]>? ItemsGranted;

    public static void PublishItemsGranted(string[] itemPaths)
    {
        ItemsGranted?.Invoke(itemPaths);
    }

    /// <summary>
    /// Asks the inventory to pack contract cargo: (item resource path, cargo instance id). The
    /// inventory packs its own copy of the item tagged with the id, and answers with CargoGranted.
    /// </summary>
    public static event Action<string, string>? CargoGrantRequested;

    /// <summary>The inventory's answer to CargoGrantRequested: (cargo instance id, whether it was packed).</summary>
    public static event Action<string, bool>? CargoGranted;

    /// <summary>Asks the inventory to give up the carried cargo with this instance id (delivery).</summary>
    public static event Action<string>? CargoHandOverRequested;

    /// <summary>The inventory's answer to CargoHandOverRequested: (cargo instance id, whether it was carried and removed).</summary>
    public static event Action<string, bool>? CargoHandedOver;

    /// <summary>Instance ids of the contract cargo the carrier owns right now (published by the inventory).</summary>
    public static IReadOnlyList<string> CarriedCargo { get; private set; } = System.Array.Empty<string>();

    public static void RequestCargoGrant(string itemPath, string cargoId) => CargoGrantRequested?.Invoke(itemPath, cargoId);

    public static void PublishCargoGranted(string cargoId, bool packed) => CargoGranted?.Invoke(cargoId, packed);

    public static void RequestCargoHandOver(string cargoId) => CargoHandOverRequested?.Invoke(cargoId);

    public static void PublishCargoHandedOver(string cargoId, bool removed) => CargoHandedOver?.Invoke(cargoId, removed);

    public static void PublishCarriedCargo(IReadOnlyList<string> cargoIds)
    {
        CarriedCargo = cargoIds;
    }

    /// <summary>Raised when a station's board offers a contract.</summary>
    public static event Action<ContractNote>? ContractOffered;

    /// <summary>Raised when the carrier takes a contract and its cargo is packed.</summary>
    public static event Action<ContractNote>? ContractAccepted;

    /// <summary>Raised once per contract when it ends (delivered, failed, abandoned): the only moment rewards come from.</summary>
    public static event Action<ContractNote>? ContractResolved;

    /// <summary>Raised when the open contract changes without ending (its cargo was knocked about).</summary>
    public static event Action<ContractNote>? ContractUpdated;

    /// <summary>The contract the carrier is working on (null when none).</summary>
    public static ContractNote? ActiveContract { get; private set; }

    /// <summary>
    /// How exposed each carried contract cargo is to knocks, by cargo instance id: 0 deep inside the
    /// pack, 1 against its outside (or in a pocket). Published by the inventory with CarriedCargo.
    /// </summary>
    public static IReadOnlyDictionary<string, float> CargoExposure { get; private set; } = new Dictionary<string, float>();

    public static void PublishCargoExposure(IReadOnlyDictionary<string, float> exposure)
    {
        CargoExposure = exposure;
    }

    public static void PublishContractUpdated(ContractNote note)
    {
        if (ActiveContract != null && ActiveContract.Value.InstanceId == note.InstanceId)
        {
            ActiveContract = note;
        }
        ContractUpdated?.Invoke(note);
    }

    public static void PublishContractOffered(ContractNote note) => ContractOffered?.Invoke(note);

    public static void PublishContractAccepted(ContractNote note)
    {
        ActiveContract = note;
        ContractAccepted?.Invoke(note);
    }

    public static void PublishContractResolved(ContractNote note)
    {
        if (ActiveContract != null && ActiveContract.Value.InstanceId == note.InstanceId)
        {
            ActiveContract = null;
        }
        ContractResolved?.Invoke(note);
    }

    /// <summary>Forgets the active contract without an ending (a new run started).</summary>
    public static void ClearActiveContract() => ActiveContract = null;

    /// <summary>Raised the first time in a run the carrier finds a relic (comes close to it, or picks it up).</summary>
    public static event Action<RelicNote>? RelicDiscovered;

    /// <summary>Raised when a relic is left at a station's altar and studied.</summary>
    public static event Action<RelicNote>? RelicDelivered;

    public static void PublishRelicDiscovered(RelicNote note) => RelicDiscovered?.Invoke(note);

    public static void PublishRelicDelivered(RelicNote note) => RelicDelivered?.Invoke(note);

    /// <summary>Raised when the carrier reads a found text (a sign, note, ledger or inscription). FirstThisRun is false on a re-read.</summary>
    public static event Action<LoreNote>? LoreRead;

    public static void PublishLoreRead(LoreNote note) => LoreRead?.Invoke(note);

    /// <summary>The carrier's goals right now: the main goal first, then the circle, then side goals (objective director).</summary>
    public static IReadOnlyList<ObjectiveNote> CurrentObjectives { get; private set; } = System.Array.Empty<ObjectiveNote>();

    /// <summary>Goals finished or failed this run, oldest first (the journal's "Now" page strikes them through).</summary>
    public static IReadOnlyList<ObjectiveNote> FinishedObjectives { get; private set; } = System.Array.Empty<ObjectiveNote>();

    /// <summary>Raised whenever the goals change (a step done, a new station, a job taken).</summary>
    public static event Action<IReadOnlyList<ObjectiveNote>>? ObjectivesChanged;

    /// <summary>Raised when a goal is new (a new main goal, a job offered or taken) or just finished: notes, banner, journal.</summary>
    public static event Action<ObjectiveNote>? ObjectiveAnnounced;

    public static void PublishObjectives(IReadOnlyList<ObjectiveNote> current, IReadOnlyList<ObjectiveNote> finished)
    {
        CurrentObjectives = current;
        FinishedObjectives = finished;
        ObjectivesChanged?.Invoke(current);
    }

    public static void PublishObjectiveAnnounced(ObjectiveNote note) => ObjectiveAnnounced?.Invoke(note);

    /// <summary>Raised when a traveller takes a seat by a station's fire: (station, name, who they are).</summary>
    public static event Action<int, string, string>? VisitorSeated;

    public static void PublishVisitorSeated(int station, string name, string title) => VisitorSeated?.Invoke(station, name, title);

    /// <summary>Raised when a station's job becomes a traveller's errand: (station, the traveller's name, the job's title).</summary>
    public static event Action<int, string, string>? ErrandPosted;

    public static void PublishErrandPosted(int station, string giver, string title) => ErrandPosted?.Invoke(station, giver, title);

    /// <summary>The closest unused point of interest beside the path, if one is near (null when none).</summary>
    public static WaysideNote? NearestWayside { get; private set; }

    /// <summary>Raised when the closest point of interest changes, or comes into or out of range.</summary>
    public static event Action<WaysideNote?>? WaysideNear;

    /// <summary>Raised when the carrier takes what a point of interest held (Text says what).</summary>
    public static event Action<WaysideNote>? WaysideUsed;

    public static void PublishWaysideNear(WaysideNote? note)
    {
        NearestWayside = note;
        WaysideNear?.Invoke(note);
    }

    public static void PublishWaysideUsed(WaysideNote note) => WaysideUsed?.Invoke(note);

    /// <summary>Asks for the carrier to die at once (falling into the abyss), with a cause.</summary>
    public static event Action<string>? KillRequested;

    public static void PublishKill(string cause)
    {
        KillRequested?.Invoke(cause);
    }

    /// <summary>
    /// Asks for the carrier to be thrown into the air (an animal's shove, a beast's charge):
    /// (horizontal velocity in m/s, height to reach in m, who did it). The controller launches the
    /// carrier; the landing is an ordinary one, so the timed roll (Space) can take it.
    /// </summary>
    public static event Action<Godot.Vector3, float, string>? LaunchRequested;

    public static void RequestLaunch(Godot.Vector3 horizontal, float height, string cause)
    {
        LaunchRequested?.Invoke(horizontal, height, cause);
    }

    /// <summary>Raised when the carrier is thrown (cause), for notes and sounds.</summary>
    public static event Action<string>? Launched;

    public static void PublishLaunched(string cause) => Launched?.Invoke(cause);

    /// <summary>True while a narrator line is playing (music ducks under it).</summary>
    public static bool IsNarrating { get; private set; }

    /// <summary>Raised when the narrator starts a line: its id and text (for subtitles later).</summary>
    public static event Action<string, string>? NarrationStarted;

    /// <summary>Raised when the narrator's line ends.</summary>
    public static event Action? NarrationFinished;

    public static void PublishNarrationStarted(string id, string text)
    {
        IsNarrating = true;
        NarrationStarted?.Invoke(id, text);
    }

    public static void PublishNarrationFinished()
    {
        IsNarrating = false;
        NarrationFinished?.Invoke();
    }

    /// <summary>Raised when something is said aloud (narrator lines, the companion's replies): subtitles show it.</summary>
    public static event Action<SpeechLine>? SpeechStarted;

    /// <summary>True while a spoken line (other than the narrator's recordings) is being said: music ducks.</summary>
    public static bool IsSpeaking { get; private set; }

    public static void PublishSpeech(SpeechLine line) => SpeechStarted?.Invoke(line);

    public static void PublishSpeaking(bool speaking) => IsSpeaking = speaking;

    /// <summary>Where the companion's voice comes from: a step to the carrier's left, updated every frame.</summary>
    public static Godot.Vector3 CompanionPosition { get; private set; }

    public static void PublishCompanionPosition(Godot.Vector3 position) => CompanionPosition = position;

    /// <summary>Who the player is talking to (the companion unless a traveller was spoken to).</summary>
    public static SpeakerInfo CurrentSpeaker { get; private set; } = SpeakerInfo.Companion;

    /// <summary>Raised when the player starts talking to someone (E on a traveller, Enter for the companion).</summary>
    public static event Action<SpeakerInfo>? TalkRequested;

    public static void RequestTalkTo(SpeakerInfo speaker)
    {
        CurrentSpeaker = speaker;
        TalkRequested?.Invoke(speaker);
    }

    /// <summary>Who the reply being published (CompanionReplied) comes from.</summary>
    public static SpeakerInfo ReplySpeaker { get; private set; } = SpeakerInfo.Companion;

    public static void PublishReplySpeaker(SpeakerInfo speaker) => ReplySpeaker = speaker;

    /// <summary>Raised the first time the carrier comes near a traveller at a station: name and who they are.</summary>
    public static event Action<string, string>? VisitorMet;

    public static void PublishVisitorMet(string name, string title) => VisitorMet?.Invoke(name, title);

    /// <summary>True while the conversation page is open: the narrator holds its voiced lines.</summary>
    public static bool IsConversing { get; private set; }

    public static event Action<bool>? ConversationChanged;

    public static void PublishConversation(bool open)
    {
        IsConversing = open;
        ConversationChanged?.Invoke(open);
    }

    /// <summary>The player asks the companion something (typed on the conversation page).</summary>
    public static event Action<string>? AskRequested;

    public static void RequestAsk(string question) => AskRequested?.Invoke(question);

    /// <summary>The companion's reply so far (it streams in), and whether it's finished.</summary>
    public static event Action<string, bool>? CompanionReplied;

    public static void PublishCompanionReply(string text, bool done) => CompanionReplied?.Invoke(text, done);

    public static CompanionState Companion { get; private set; } = CompanionState.Silent;

    public static event Action<CompanionState>? CompanionChanged;

    public static void PublishCompanion(CompanionState state)
    {
        Companion = state;
        CompanionChanged?.Invoke(state);
    }

    /// <summary>The weather where the carrier is, updated every physics frame.</summary>
    public static WeatherSnapshot CurrentWeather { get; private set; } = WeatherSnapshot.Calm;

    /// <summary>Raised every physics frame by the weather system.</summary>
    public static event Action<WeatherSnapshot>? WeatherUpdated;

    /// <summary>Raised when the dominant weather changes (narrator lines, later sounds).</summary>
    public static event Action<WeatherKind>? WeatherChanged;

    public static void PublishWeather(WeatherSnapshot weather)
    {
        WeatherKind previous = CurrentWeather.Kind;
        CurrentWeather = weather;
        WeatherUpdated?.Invoke(weather);
        if (weather.Kind != previous)
        {
            WeatherChanged?.Invoke(weather.Kind);
        }
    }

    /// <summary>Raised by terrain when the deepest point reached this run grows, in metres below the start.</summary>
    public static event Action<float>? DepthChanged;

    public static void PublishDepth(float depth)
    {
        DepthChanged?.Invoke(depth);
    }

    public static void PublishLanded(float downwardSpeed)
    {
        Landed?.Invoke(downwardSpeed);
    }

    public static void PublishDowned(bool downed)
    {
        IsDowned = downed;
        DownedChanged?.Invoke(downed);
    }

    public static void PublishDamaged(float amount)
    {
        Damaged?.Invoke(amount);
    }

    public static void PublishVitals(VitalsSnapshot snapshot)
    {
        VitalsUpdated?.Invoke(snapshot);
    }

    public static void PublishRespawnRequest()
    {
        RespawnRequested?.Invoke();
    }

    public static void PublishLoad(LoadSnapshot snapshot)
    {
        CurrentLoad = snapshot;
        LoadChanged?.Invoke(snapshot);
    }

    public static void PublishBalance(BalanceSnapshot snapshot)
    {
        BalanceUpdated?.Invoke(snapshot);
    }

    /// <summary>Where the carrier is (its feet), updated with every LocomotionSnapshot; for many small readers like world items.</summary>
    public static Godot.Vector3 CarrierPosition { get; private set; }

    public static void PublishLocomotion(LocomotionSnapshot snapshot)
    {
        CarrierPosition = snapshot.Position;
        LocomotionUpdated?.Invoke(snapshot);
    }

    public static void PublishFootPlacement(FootPlacementSnapshot snapshot)
    {
        FootPlacementUpdated?.Invoke(snapshot);
    }
}
