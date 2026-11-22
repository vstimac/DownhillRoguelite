using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// Keeps a four-legged creature's feet on the ground while it moves. The walk or run clip plays at
/// the rate that makes a planted foot travel back exactly as fast as the body moves forward, and the
/// creature changes from its walk to its run clip (and back) by its real speed. Without this a wolf
/// played its walk at a fixed rate whether it ambled at 1 m/s or charged at 12, and slid along the
/// ground as if swimming.
///
/// Each clip's ground speed is measured once per model (the planted foot's speed at rate 1, at the
/// species' model scale) and written into the species; 0 leaves the creature on its old fixed rates.
/// Only the host (or a solo game) decides: a co-op guest's copy plays the clip and rate it is sent.
/// </summary>
public sealed class CreatureGait
{
    /// <summary>The library extra clips (made for a model that came without them) are added under.</summary>
    public const string Library = "extra";
    /// <summary>Slowest and fastest a locomotion clip plays (slower reads as frozen, faster as frantic); a species may allow faster (a spider's scuttle).</summary>
    public const float MinRate = 0.35f;
    public const float MaxRate = 3.2f;
    /// <summary>Changes to the run clip above this many times the walk clip's ground speed, back below the second.</summary>
    public const float RunAbove = 3.0f;
    public const float WalkBelow = 2.4f;
    /// <summary>How quickly the measured speed follows the body (s).</summary>
    public const float SpeedSmoothing = 0.12f;

    private static readonly Dictionary<string, AnimationLibrary> Libraries = new();

    private readonly string _walk;
    private readonly string _run;
    private readonly float _walkGround;
    private readonly float _runGround;
    private readonly float _maxRate;

    public CreatureGait(string walkClip, string runClip, float walkGroundSpeed, float runGroundSpeed, float maxRate = MaxRate)
    {
        _maxRate = maxRate > 0.0f ? maxRate : MaxRate;
        _walk = walkClip;
        _run = runClip;
        _walkGround = Mathf.Max(walkGroundSpeed, 0.01f);
        _runGround = Mathf.Max(runGroundSpeed, 0.01f);
    }

    /// <summary>The body's horizontal speed, smoothed (m/s).</summary>
    public float Speed { get; private set; }
    /// <summary>True while it runs (the run clip), false while it walks.</summary>
    public bool Running { get; private set; }
    /// <summary>The walk clip's rate at the species' ordinary walking speed (the guest's "calm walk" reference).</summary>
    public float WalkRateAt(float speed) => Mathf.Clamp(speed / _walkGround, MinRate, _maxRate);

    /// <summary>A gait for a species, or null when its clips' ground speeds aren't known.</summary>
    public static CreatureGait? For(string walkClip, string runClip, float walkGroundSpeed, float runGroundSpeed, float maxRate = 0.0f)
    {
        if (walkClip.Length == 0 || runClip.Length == 0 || walkGroundSpeed <= 0.0f || runGroundSpeed <= 0.0f)
        {
            return null;
        }
        return new CreatureGait(walkClip, runClip, walkGroundSpeed, runGroundSpeed, maxRate);
    }

    /// <summary>True for the clips this gait plays: the walk and the run.</summary>
    public bool Manages(string clip) => clip.Length > 0 && (clip == _walk || clip == _run);

    /// <summary>Takes this tick's horizontal movement into the measured speed.</summary>
    public void Measure(Vector3 moved, float dt)
    {
        if (dt <= 0.0f)
        {
            return;
        }
        float speed = new Vector2(moved.X, moved.Z).Length() / dt;
        float weight = 1.0f - Mathf.Exp(-dt / SpeedSmoothing);
        Speed = Mathf.Lerp(Speed, speed, weight);
    }

    /// <summary>The clip to play for a walk or run request: whichever suits the measured speed.</summary>
    public string Pick()
    {
        Running = Running ? Speed > _walkGround * WalkBelow : Speed > _walkGround * RunAbove;
        return Running ? _run : _walk;
    }

    /// <summary>The rate for the clip <see cref="Pick"/> chose, so planted feet keep still on the ground.</summary>
    public float Rate()
    {
        return Mathf.Clamp(Speed / (Running ? _runGround : _walkGround), MinRate, _maxRate);
    }

    /// <summary>
    /// Adds the clips of <paramref name="sources"/> (animation-only files made for this model) to its
    /// player under <see cref="Library"/>, named by what follows the armature's "|" ("extra/Gallop").
    /// Safe to call again; the library is built once per set of files and shared.
    /// </summary>
    public static void AddClips(AnimationPlayer? player, Godot.Collections.Array<PackedScene> sources)
    {
        if (player == null || sources.Count == 0 || player.HasAnimationLibrary(Library))
        {
            return;
        }
        string key = "";
        foreach (PackedScene source in sources)
        {
            key += source.ResourcePath + ";";
        }
        if (!Libraries.TryGetValue(key, out AnimationLibrary? library))
        {
            library = new AnimationLibrary();
            foreach (PackedScene source in sources)
            {
                Node root = source.Instantiate();
                foreach (Node node in root.FindChildren("*", "AnimationPlayer", true, false))
                {
                    if (node is not AnimationPlayer from)
                    {
                        continue;
                    }
                    foreach (string name in from.GetAnimationList())
                    {
                        string clip = name.Contains('|') ? name[(name.LastIndexOf('|') + 1)..] : name;
                        Animation animation = from.GetAnimation(name);
                        animation.LoopMode = Animation.LoopModeEnum.Linear;
                        library.AddAnimation(clip, animation);
                    }
                }
                root.Free();
            }
            Libraries[key] = library;
        }
        player.AddAnimationLibrary(Library, library);
    }
}
