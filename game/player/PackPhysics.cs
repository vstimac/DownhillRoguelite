using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The pack as its own body, held by its straps: a rigid body with mass and a turning inertia, whose
/// strap points (where the straps are sewn on) are each tied to the place on the body that strap
/// holds them (the shoulders, the ribs, the hips), moving with that part. The shoulder straps' tops
/// hold it firmly, the lower ends and the hip belt give a little, so it lags, swings, bounces and
/// settles by itself, following the spine as it bends, without ever coming loose. Its back panel rests on the body's surface (BodyHull, measured from the
/// mesh), never inside it. Solved in substeps (extended position-based dynamics).
/// Visual only: the carried load's balance and weight are calculated elsewhere.
/// </summary>
public sealed class PackPhysics
{
    public Vector3 Gravity = new(0.0f, -9.8f, 0.0f);
    public float LinearDamping = 1.5f;
    public float AngularDamping = 6.0f;
    /// <summary>The pack's back panel stays this far off the body.</summary>
    public float PackClearance = 0.012f;
    /// <summary>The furthest (m) and most (radians) the pack may swing from its resting place (a strap only stretches so far).</summary>
    public float MaxDistance = float.PositiveInfinity;
    public float MaxAngle = float.PositiveInfinity;
    /// <summary>The bones whose outlines the back panel rests on (the torso; null: all), so a swinging arm doesn't knock the pack.</summary>
    public ICollection<int>? PanelBones;
    /// <summary>Every strap's give is multiplied by this (the harness loosens the hold as the load grows).</summary>
    public float GiveScale = 1.0f;

    // The pack: centre of mass, orientation, motion, mass and turning inertia (its own axes).
    public Vector3 Position;
    public Quaternion Rotation = Quaternion.Identity;
    public Vector3 Velocity;
    public Vector3 Spin;
    private float _inverseMass = 1.0f / 8.0f;
    private Vector3 _inverseInertia = Vector3.One;
    private Vector3 _size = Vector3.One;
    private float _mass = 8.0f;

    /// <summary>The pack's mass now, in kg (its own and what is in it).</summary>
    public float Mass => _mass;

    /// <summary>
    /// Strap points: offset from the centre of mass (pack axes), where their strap pulls them (the same,
    /// a little in towards the body: the straps' tension), and how much they give (compliance, m/N).
    /// </summary>
    private readonly List<(Vector3 Local, float Give)> _ties = new();
    /// <summary>Where each tie's strap holds it on the body, at the last update and at this one (world).</summary>
    private readonly List<Vector3> _heldBefore = new();
    private readonly List<Vector3> _heldNow = new();
    private readonly List<Vector3> _contacts = new();
    private BodyHull? _hull;
    /// <summary>Where the pack rests now (its centre of mass's frame), at the last update and this one; the swing is limited around it.</summary>
    private Transform3D _restBefore = Transform3D.Identity;
    private Transform3D _restNow = Transform3D.Identity;

    /// <summary>The deepest the back panel was found inside the body last step (world units).</summary>
    public float DeepestInside { get; private set; }
    /// <summary>How deep the back panel is inside the body after the last step (what is seen; m).</summary>
    public float InsideAfter { get; private set; }
    /// <summary>The furthest a strap point was from where its strap holds it, last step (m).</summary>
    public float WidestTie { get; private set; }
    public int Ties => _ties.Count;
    /// <summary>A tie's strap point on the pack now (world).</summary>
    public Vector3 TiePoint(int index) => Position + new Basis(Rotation) * _ties[index].Local;
    /// <summary>Where its strap holds that tie on the body now (world).</summary>
    public Vector3 TieHeld(int index) => _heldNow[index];

    /// <summary>Starts over, still, at <paramref name="fit"/> (the centre of mass's frame, no scale).</summary>
    public void Clear(BodyHull? hull, float mass, Vector3 size, Transform3D fit)
    {
        _hull = hull;
        _ties.Clear();
        _heldBefore.Clear();
        _heldNow.Clear();
        _contacts.Clear();
        _size = size;
        SetMass(mass);
        _restBefore = fit;
        _restNow = fit;
        Position = fit.Origin;
        Rotation = fit.Basis.GetRotationQuaternion();
        Velocity = Vector3.Zero;
        Spin = Vector3.Zero;
    }

    /// <summary>Changes the pack's mass (and with it its turning inertia, as a box of its size) without stopping its swing.</summary>
    public void SetMass(float mass)
    {
        _mass = Mathf.Max(mass, 0.1f);
        _inverseMass = 1.0f / _mass;
        // A box's inertia about its own axes.
        float a = _size.X * _size.X, b = _size.Y * _size.Y, c = _size.Z * _size.Z;
        Vector3 inertia = new(_mass * (b + c) / 12.0f, _mass * (a + c) / 12.0f, _mass * (a + b) / 12.0f);
        _inverseInertia = new Vector3(1.0f / Mathf.Max(inertia.X, 0.0001f), 1.0f / Mathf.Max(inertia.Y, 0.0001f), 1.0f / Mathf.Max(inertia.Z, 0.0001f));
    }

    /// <summary>
    /// Ties a strap point (offset from the centre of mass, pack axes) to where its strap holds it on the
    /// body (<paramref name="held"/>, world; Step moves it); <paramref name="give"/>: its compliance.
    /// </summary>
    public void Tie(Vector3 local, float give, Vector3 held)
    {
        _ties.Add((local, give));
        _heldBefore.Add(held);
        _heldNow.Add(held);
    }
    /// <summary>A point of the pack's back panel that rests on the body (offset from the centre of mass).</summary>
    public void Contact(Vector3 local) => _contacts.Add(local);

    /// <summary>
    /// Advances by <paramref name="dt"/> in <paramref name="substeps"/> steps while the places the
    /// straps hold the ties move to <paramref name="held"/> (world, one per tie, in order) and the
    /// pack's resting place to <paramref name="rest"/>.
    /// </summary>
    public void Step(float dt, int substeps, IReadOnlyList<Vector3> held, Transform3D rest)
    {
        _restBefore = _restNow;
        _restNow = rest;
        for (int i = 0; i < _ties.Count && i < held.Count; i++)
        {
            _heldBefore[i] = _heldNow[i];
            _heldNow[i] = held[i];
        }
        DeepestInside = 0.0f;
        WidestTie = 0.0f;
        if (dt <= 0.0f)
        {
            _hull?.Between(1.0f);
            Panel();
            return;
        }
        substeps = Mathf.Max(1, substeps);
        float h = dt / substeps;
        Quaternion restFrom = _restBefore.Basis.GetRotationQuaternion();
        Quaternion restTo = _restNow.Basis.GetRotationQuaternion();
        for (int step = 0; step < substeps; step++)
        {
            float share = (step + 1.0f) / substeps;
            _hull?.Between(share);
            Vector3 lastPosition = Position;
            Quaternion lastRotation = Rotation;
            Velocity += Gravity * h;
            Position += Velocity * h;
            Turn(Spin * h);

            Tighten(share, h);
            Limit(_restBefore.Origin.Lerp(_restNow.Origin, share), restFrom.Slerp(restTo, share));
            Panel();

            Velocity = (Position - lastPosition) / h * Mathf.Exp(-LinearDamping * h);
            Quaternion change = Rotation * lastRotation.Inverse();
            Vector3 spin = new Vector3(change.X, change.Y, change.Z) * (2.0f / h);
            if (change.W < 0.0f) { spin = -spin; }
            Spin = spin * Mathf.Exp(-AngularDamping * h);
        }
        InsideAfter = Inside();
    }

    /// <summary>
    /// How far <paramref name="pose"/> (the pack's resting place, centre of mass frame) must move to
    /// bring its back panel out of the body as it is posed now: a bending, turning torso comes into
    /// where the pack rests, and a resting place inside the body would set the straps pulling the
    /// pack in against the panel pushing it out. Zero when it's clear.
    /// </summary>
    /// <summary>The back panel point (its index) and the body bone that were deepest at the last OutOfBody.</summary>
    public int DeepestContact { get; private set; } = -1;
    public int DeepestBone { get; private set; } = -1;

    public Vector3 OutOfBody(Transform3D pose)
    {
        DeepestContact = -1;
        DeepestBone = -1;
        Vector3 total = Vector3.Zero;
        if (_hull == null || _contacts.Count == 0)
        {
            return total;
        }
        _hull.Between(1.0f);
        for (int pass = 0; pass < 3; pass++)
        {
            Vector3 deepest = Vector3.Zero;
            for (int i = 0; i < _contacts.Count; i++)
            {
                Vector3 q = pose.Origin + total + pose.Basis * _contacts[i];
                Vector3 pushed = q;
                if (_hull.Collide(ref pushed, PackClearance, PanelBones, out _, out int bone, out _) && pushed.DistanceSquaredTo(q) > deepest.LengthSquared())
                {
                    deepest = pushed - q;
                    if (pass == 0)
                    {
                        DeepestContact = i;
                        DeepestBone = bone;
                    }
                }
            }
            if (deepest.LengthSquared() < 0.0000001f)
            {
                break;
            }
            total += deepest;
        }
        return total;
    }

    /// <summary>How deep the back panel is inside the body now (without moving it).</summary>
    private float Inside()
    {
        float deepest = 0.0f;
        if (_hull == null) { return deepest; }
        foreach (Vector3 local in _contacts)
        {
            Vector3 q = Position + new Basis(Rotation) * local;
            Vector3 pushed = q;
            if (_hull.Collide(ref pushed, 0.0f, PanelBones, out _, out _, out _)) { deepest = Mathf.Max(deepest, pushed.DistanceTo(q)); }
        }
        return deepest;
    }

    /// <summary>Keeps the pack within MaxDistance and MaxAngle of its resting place (before the back panel's contact, so that wins).</summary>
    private void Limit(Vector3 origin, Quaternion turn)
    {
        Vector3 off = Position - origin;
        float length = off.Length();
        if (length > MaxDistance && length > 0.000001f)
        {
            Position = origin + off * (MaxDistance / length);
        }
        float turned = Rotation.AngleTo(turn);
        if (turned > MaxAngle && turned > 0.000001f)
        {
            Rotation = turn.Slerp(Rotation, MaxAngle / turned).Normalized();
        }
    }

    /// <summary>Lets the pack come to rest with everything held still (a fresh start), then stops it.</summary>
    public void Settle(IReadOnlyList<Vector3> held, float seconds)
    {
        float distance = MaxDistance;
        float angle = MaxAngle;
        MaxDistance = float.PositiveInfinity;
        MaxAngle = float.PositiveInfinity;
        int steps = Mathf.Max(1, Mathf.CeilToInt(seconds * 120.0f));
        for (int i = 0; i < steps; i++)
        {
            Step(1.0f / 120.0f, 2, held, _restNow);
        }
        Velocity = Vector3.Zero;
        Spin = Vector3.Zero;
        MaxDistance = distance;
        MaxAngle = angle;
    }

    /// <summary>Pulls each strap point towards where its strap holds it, as stiffly as that strap allows.</summary>
    private void Tighten(float share, float h)
    {
        for (int i = 0; i < _ties.Count; i++)
        {
            (Vector3 local, float give) = _ties[i];
            Vector3 r = new Basis(Rotation) * local;
            Vector3 target = _heldBefore[i].Lerp(_heldNow[i], share);
            Vector3 delta = target - (Position + r);
            float length = delta.Length();
            WidestTie = Mathf.Max(WidestTie, length);
            if (length < 0.0000001f) { continue; }
            Vector3 n = delta / length;
            float move = length / (BodyWeight(r, n) + give * GiveScale / (h * h));
            Push(r, n * move);
        }
    }

    /// <summary>The back panel rests on the body.</summary>
    private void Panel()
    {
        if (_hull == null) { return; }
        foreach (Vector3 local in _contacts)
        {
            Vector3 r = new Basis(Rotation) * local;
            Vector3 q = Position + r;
            Vector3 pushed = q;
            if (!_hull.Collide(ref pushed, PackClearance, PanelBones, out _, out _, out _)) { continue; }
            Vector3 delta = pushed - q;
            float length = delta.Length();
            if (length < 0.000001f) { continue; }
            DeepestInside = Mathf.Max(DeepestInside, length);
            Vector3 n = delta / length;
            Push(r, n * (length / BodyWeight(r, n)));
        }
    }

    /// <summary>How hard the pack is to move at offset r along n (its inverse mass there).</summary>
    private float BodyWeight(Vector3 r, Vector3 n)
    {
        Vector3 arm = r.Cross(n);
        return _inverseMass + arm.Dot(InverseInertia(arm));
    }

    private Vector3 InverseInertia(Vector3 v)
    {
        Basis turn = new(Rotation);
        Vector3 local = turn.Transposed() * v;
        return turn * new Vector3(local.X * _inverseInertia.X, local.Y * _inverseInertia.Y, local.Z * _inverseInertia.Z);
    }

    /// <summary>Moves the pack as a positional impulse at offset r.</summary>
    private void Push(Vector3 r, Vector3 impulse)
    {
        Position += impulse * _inverseMass;
        Turn(InverseInertia(r.Cross(impulse)));
    }

    private void Turn(Vector3 angle)
    {
        if (angle.LengthSquared() < 1e-14f) { return; }
        Quaternion spin = new(angle.X, angle.Y, angle.Z, 0.0f);
        Quaternion turned = Rotation + spin * Rotation * 0.5f;
        Rotation = turned.Normalized();
    }
}
