using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// Every number for the predators (wolves) in one place: how they hunt, bite, break off, where and
/// when they come. Their senses, speeds, body, health, loot and the circles they live in are in their
/// species entry in default_wildlife.tres, like every animal's (Temperament = Predator).
/// Set Enabled to false and no wolf is ever placed (wolves placed by hand still behave).
/// </summary>
public static class PredatorTuning
{
    /// <summary>The one switch: false and the spawner places no predators at all.</summary>
    public const bool Enabled = true;

    // --- Where and when ---

    /// <summary>Most predator packs alive at once around the carriers.</summary>
    public const int MaxPacks = 1;
    /// <summary>Daylight (WorldClock.Daylight, 1 day, 0 night) below which it counts as night for them.</summary>
    public const float NightBelow = 0.35f;
    /// <summary>Their species weight is multiplied by this by day and at night (more common at night).</summary>
    public const float DayWeight = 1.0f;
    public const float NightWeight = 3.0f;

    // --- Stalking ---

    /// <summary>The ring they circle the carrier on when they start stalking, by day and at night, in m.</summary>
    public const float StalkRingDay = 18.0f;
    public const float StalkRingNight = 14.0f;
    /// <summary>The ring they close in to while stalking, in m.</summary>
    public const float StalkRingClose = 8.0f;
    /// <summary>Seconds of circling before the pack's first bite (random in range), by day and at night (bolder).</summary>
    public static readonly Vector2 StalkSecondsDay = new(10.0f, 15.0f);
    public static readonly Vector2 StalkSecondsNight = new(5.0f, 8.0f);
    /// <summary>Seconds between the pack's bites (one wolf bites at a time), by day and at night.</summary>
    public static readonly Vector2 AttackIntervalDay = new(6.0f, 9.0f);
    public static readonly Vector2 AttackIntervalNight = new(3.5f, 6.0f);
    /// <summary>Circling speed and the speed it rushes in (or catches up) at, in m/s.</summary>
    public const float StalkSpeed = 2.6f;
    public const float RushSpeed = 7.0f;
    /// <summary>Gives up a rush it can't finish after this long, in s.</summary>
    public const float RushSeconds = 5.0f;
    /// <summary>A hunting wolf keeps track of its prey by scent within this share of its sight range; farther, it loses it.</summary>
    public const float TrackRange = 1.5f;

    // --- The bite ---

    /// <summary>It stops and crouches this close to its prey, in m.</summary>
    public const float WindUpDistance = 3.2f;
    /// <summary>The wind-up: it crouches and growls this long before it springs, in s (the warning to dodge).</summary>
    public const float WindUpSeconds = 0.7f;
    /// <summary>It fixes the way it will spring after this share of the wind-up; moving aside after that makes it miss.</summary>
    public const float WindUpAimShare = 0.5f;
    /// <summary>The spring: this fast, this long (about 3 m).</summary>
    public const float LungeSpeed = 9.0f;
    public const float LungeSeconds = 0.35f;
    /// <summary>The bite lands if the carrier is within this much of its body during the spring, in m (beyond both bodies' radii).</summary>
    public const float BiteReach = 0.45f;
    /// <summary>Health a bite takes.</summary>
    public const float BiteDamage = 10.0f;
    /// <summary>After a bite it backs off to this far beyond the close ring before it circles again, in m.</summary>
    public const float BackOff = 3.0f;
    /// <summary>The clip speed of its idle while crouched to spring (a shiver); guests read the wind-up from it.</summary>
    public const float WindUpClipSpeed = 1.8f;
    /// <summary>How low it crouches (share of its height) and how far its nose dips while winding up, in degrees.</summary>
    public const float CrouchDrop = 0.18f;
    public const float CrouchPitch = 9.0f;
    /// <summary>The growl as it crouches (one at random), heard on every machine.</summary>
    public static readonly string[] GrowlSounds =
    {
        "res://audio/sonniss/creatures/wolf_growl_0.ogg",
        "res://audio/sonniss/creatures/wolf_growl_1.ogg",
    };

    // --- Hints (keyboard first) ---

    /// <summary>Shown once a run when wolves first hunt this carrier, and when one first crouches to spring at it.</summary>
    public const string StalkHint = "Wolves are circling you. Draw your sword (Z) and strike one (left mouse) or kick it (middle mouse): hurt, they lose their nerve. A station's fire keeps them off.";
    public const string WindUpHint = "A wolf crouches to spring: step aside or dive (Ctrl) before it leaps!";
    /// <summary>Hints count wolves within this distance of the carrier, in m.</summary>
    public const float HintRange = 25.0f;

    // --- Downed carriers ---

    /// <summary>A downed carrier (fallen, or a hard landing that threw them) counts this much closer when the pack picks its prey.</summary>
    public const float DownedPull = 0.6f;
    /// <summary>With its prey down, the pack's wait for the next bite is this share of usual.</summary>
    public const float DownedHurry = 0.4f;
    /// <summary>A carrier bleeding out (co-op, waiting for a friend) is circled this close, never bitten.</summary>
    public const float BleedingRing = 5.0f;

    // --- Breaking off ---

    /// <summary>A blow this hard (any sword hit or kick) scares a wolf off.</summary>
    public const float ScareDamage = 8.0f;
    /// <summary>A scared wolf runs until this far from the carrier, in m, and hunts no one for this long, in s.</summary>
    public const float ScaredDistance = 40.0f;
    public const float ScaredRest = 45.0f;
    /// <summary>The whole pack retreats when this share of it is hurt or dead, and hunts no one for this long, in s.</summary>
    public const float PackRetreatShare = 0.5f;
    public const float PackRest = 90.0f;
    /// <summary>A carrier within this distance of a station's fire (or in a shelter) is left alone, in m.</summary>
    public const float FireRadius = 25.0f;
    /// <summary>Wolves keep at least this far from a carrier at a fire, in m.</summary>
    public const float FireKeepOff = 30.0f;

    // --- Co-op ---

    /// <summary>A wolf keeps after its prey unless another carrier is clearly closer: its prey's distance counts this much.</summary>
    public const float Loyalty = 0.75f;

    /// <summary>Night for the predators.</summary>
    public static bool IsNight(float daylight) => daylight < NightBelow;
}
