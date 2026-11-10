using System;
using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Environment.Fog;

/// <summary>
/// The ground fog as a shallow fluid on a square patch of cells around the carrier: per cell the
/// ground height (learnt by the sampler), whether something blocks it (a trunk, a rock), the fog's
/// density (its depth, in units of GroundFogSettings.Depth) and its flow (m/s, along X and Z).
/// Pure C#, no scene tree, so it can be tested and timed headless.
///
/// Each fixed step:
/// 1. Forces: the fog runs down the slope of its own surface (ground + depth), so it pours downhill,
///    piles into hollows and spreads out of heaps; a slow swirl field keeps still fog alive.
/// 2. Drag, and no flow into blocked cells.
/// 3. Semi-Lagrangian advection of density and flow (traced back along the flow, sampled
///    bilinearly over the open cells only), with the density compressed where flow converges.
/// 4. Diffusion of density and flow.
/// 5. Recovery: every cell eases back toward its settled density (thicker in hollows) over
///    RecoveryTime, so a wake closes up behind the carrier.
///
/// The patch is aligned to whole cells in the world; Shift moves it by whole cells and keeps
/// everything in the overlap, so nothing snaps. New cells start as settled fog over unknown ground.
/// </summary>
public sealed class GroundFogSimulation
{
    /// <summary>Ground under a cell: not looked for yet, known, looked for but not found (chunks still loading), queued to be looked for.</summary>
    public const byte Unknown = 0;
    public const byte Known = 1;
    public const byte Missed = 2;
    public const byte Queued = 3;

    public GroundFogSettings Settings { get; }
    public int Size { get; }
    public float Cell { get; }
    /// <summary>World cell index of the patch's first cell (its corner is OriginX × Cell, OriginZ × Cell).</summary>
    public int OriginX { get; private set; }
    public int OriginZ { get; private set; }
    public double Time { get; private set; }
    public int Steps { get; private set; }
    /// <summary>The last step's cost, in ms.</summary>
    public double LastStepMs { get; private set; }

    public readonly float[] Height;
    public readonly byte[] Ground;
    public readonly bool[] Solid;
    public readonly float[] Density;
    public readonly float[] FlowX;
    public readonly float[] FlowZ;
    public readonly float[] Settled;

    private readonly float[] _density2;
    private readonly float[] _flowX2;
    private readonly float[] _flowZ2;
    private readonly float[] _surface;
    private readonly float[] _sum;
    private readonly float[] _count;
    private readonly float[] _sinX;
    private readonly float[] _cosX;
    private readonly float[] _sinZ;
    private readonly float[] _cosZ;
    private readonly float[] _sinA;
    private readonly float[] _cosA;
    private readonly float[] _sinB;
    private readonly float[] _cosB;
    private bool _settledDirty = true;
    private readonly Random _random = new(1907);
    private readonly System.Diagnostics.Stopwatch _watch = new();

    public GroundFogSimulation(GroundFogSettings settings)
    {
        Settings = settings;
        Size = Math.Max(8, settings.GridSize);
        Cell = Math.Max(0.05f, settings.CellSize);
        int n = Size * Size;
        Height = new float[n];
        Ground = new byte[n];
        Solid = new bool[n];
        Density = new float[n];
        FlowX = new float[n];
        FlowZ = new float[n];
        Settled = new float[n];
        _density2 = new float[n];
        _flowX2 = new float[n];
        _flowZ2 = new float[n];
        _surface = new float[n];
        _sum = new float[n];
        _count = new float[n];
        _sinX = new float[Size];
        _cosX = new float[Size];
        _sinZ = new float[Size];
        _cosZ = new float[Size];
        _sinA = new float[Size];
        _cosA = new float[Size];
        _sinB = new float[Size];
        _cosB = new float[Size];
        Reset(0, 0);
    }

    /// <summary>Forgets everything: settled fog over unknown ground, the patch's corner at these world cells.</summary>
    public void Reset(int originX, int originZ)
    {
        OriginX = originX;
        OriginZ = originZ;
        for (int i = 0; i < Density.Length; i++)
        {
            Clear(i);
        }
        _settledDirty = true;
    }

    /// <summary>The patch's corner so that this world point sits in its middle.</summary>
    public void CentreOn(float x, float z)
    {
        Reset(Mathf.FloorToInt(x / Cell) - Size / 2, Mathf.FloorToInt(z / Cell) - Size / 2);
    }

    private void Clear(int i)
    {
        Height[i] = 0.0f;
        Ground[i] = Unknown;
        Solid[i] = false;
        Density[i] = Settings.Ambient;
        FlowX[i] = 0.0f;
        FlowZ[i] = 0.0f;
        Settled[i] = Settings.Ambient;
    }

    public int Index(int x, int z) => z * Size + x;

    /// <summary>The middle of a cell, in world X/Z.</summary>
    public Vector2 CellCentre(int x, int z) => new((OriginX + x + 0.5f) * Cell, (OriginZ + z + 0.5f) * Cell);

    /// <summary>The cell a world point falls in; false if it's off the patch.</summary>
    public bool CellAt(float worldX, float worldZ, out int x, out int z)
    {
        x = Mathf.FloorToInt(worldX / Cell) - OriginX;
        z = Mathf.FloorToInt(worldZ / Cell) - OriginZ;
        return x >= 0 && z >= 0 && x < Size && z < Size;
    }

    /// <summary>World X/Z of the patch's middle.</summary>
    public Vector2 Centre => new((OriginX + Size * 0.5f) * Cell, (OriginZ + Size * 0.5f) * Cell);

    /// <summary>What the sampler found under a cell.</summary>
    public void SetGround(int x, int z, float height, bool solid)
    {
        int i = Index(x, z);
        Height[i] = height;
        Ground[i] = Known;
        Solid[i] = solid;
        if (solid)
        {
            Density[i] = 0.0f;
            FlowX[i] = 0.0f;
            FlowZ[i] = 0.0f;
        }
        _settledDirty = true;
    }

    public void MarkGround(int x, int z, byte state)
    {
        Ground[Index(x, z)] = state;
    }

    public int KnownCells
    {
        get
        {
            int known = 0;
            foreach (byte state in Ground)
            {
                if (state == Known)
                {
                    known++;
                }
            }
            return known;
        }
    }

    /// <summary>
    /// Moves the patch by whole cells (positive: toward +X / +Z), keeping density, flow and ground
    /// in the overlap; the cells that come in are settled fog over unknown ground.
    /// </summary>
    public void Shift(int dx, int dz)
    {
        if (dx == 0 && dz == 0)
        {
            return;
        }
        if (Math.Abs(dx) >= Size || Math.Abs(dz) >= Size)
        {
            Reset(OriginX + dx, OriginZ + dz);
            return;
        }
        int startZ = 0;
        int endZ = Size;
        int stepZ = 1;
        if (dz < 0)
        {
            startZ = Size - 1;
            endZ = -1;
            stepZ = -1;
        }
        int startX = 0;
        int endX = Size;
        int stepX = 1;
        if (dx < 0)
        {
            startX = Size - 1;
            endX = -1;
            stepX = -1;
        }
        // Walk in the direction that never overwrites a cell before it's been copied.
        for (int z = startZ; z != endZ; z += stepZ)
        {
            for (int x = startX; x != endX; x += stepX)
            {
                int to = Index(x, z);
                int fromX = x + dx;
                int fromZ = z + dz;
                if (fromX < 0 || fromZ < 0 || fromX >= Size || fromZ >= Size)
                {
                    Clear(to);
                    continue;
                }
                int from = Index(fromX, fromZ);
                Height[to] = Height[from];
                Ground[to] = Ground[from];
                Solid[to] = Solid[from];
                Density[to] = Density[from];
                FlowX[to] = FlowX[from];
                FlowZ[to] = FlowZ[from];
                Settled[to] = Settled[from];
            }
        }
        OriginX += dx;
        OriginZ += dz;
        _settledDirty = true;
    }

    // --- The carrier ---

    /// <summary>
    /// The carrier's body moving through the fog for <paramref name="dt"/> seconds: a bubble around
    /// <paramref name="feet"/> is cleared and shoved outward (wider at a sprint), the fog is dragged
    /// along a little, and two eddies peel off behind, churning harder the faster it goes.
    /// </summary>
    public void Push(Vector3 feet, Vector3 velocity, float dt)
    {
        GroundFogSettings s = Settings;
        Vector2 at = new(feet.X, feet.Z);
        Vector2 move = new(velocity.X, velocity.Z);
        float speed = move.Length();
        float sprint = Mathf.Clamp(speed / Mathf.Max(s.SprintSpeed, 0.1f), 0.0f, 1.0f);
        float radius = s.PushRadius + s.SprintRadiusBoost * sprint;
        float strength = s.WakeStrength;
        if (strength <= 0.0f || radius <= 0.0f)
        {
            return;
        }
        float blend = Mathf.Clamp(10.0f * dt * strength, 0.0f, 1.0f);
        float clear = Mathf.Clamp(s.Displace * (0.6f + 1.4f * sprint) * dt * strength, 0.0f, 1.0f);
        float shove = s.PushSpeed * (0.5f + sprint);
        ForCells(at, radius, (i, offset, distance) =>
        {
            float r = distance / radius;
            float share = 1.0f - r * r;
            Vector2 outward = Vector2.Zero;
            if (distance > 0.001f)
            {
                outward = offset / distance;
            }
            Vector2 wanted = outward * shove + move * s.DragAlong;
            float k = blend * share;
            FlowX[i] += (wanted.X - FlowX[i]) * k;
            FlowZ[i] += (wanted.Y - FlowZ[i]) * k;
            Density[i] *= 1.0f - clear * share;
        });
        if (speed < 0.3f)
        {
            return;
        }
        Vector2 ahead = move / speed;
        Vector2 side = new(-ahead.Y, ahead.X);
        Vector2 behind = at - ahead * (s.WakeBehind + radius * 0.5f);
        float spin = s.WakeSpin * strength * Mathf.Clamp(speed / Mathf.Max(s.SprintSpeed, 0.1f), 0.2f, 1.2f);
        float rate = Mathf.Clamp(6.0f * dt, 0.0f, 1.0f);
        Swirl(behind + side * s.WakeRadius * 0.8f, s.WakeRadius, spin, 0.0f, rate);
        Swirl(behind - side * s.WakeRadius * 0.8f, s.WakeRadius, -spin, 0.0f, rate);
        float churn = s.SprintTurbulence * sprint * sprint * strength * dt;
        if (churn > 0.0f)
        {
            ForCells(behind, radius * 1.6f, (i, offset, distance) =>
            {
                FlowX[i] += ((float)_random.NextDouble() * 2.0f - 1.0f) * churn;
                FlowZ[i] += ((float)_random.NextDouble() * 2.0f - 1.0f) * churn;
            });
        }
    }

    /// <summary>A foot coming down in the fog: a little whirl (turning one way for the left foot, the other for the right) and a puff outward.</summary>
    public void Footfall(Vector3 foot, bool left)
    {
        GroundFogSettings s = Settings;
        float spin = s.StepSwirl * s.WakeStrength;
        if (!left)
        {
            spin = -spin;
        }
        Swirl(new Vector2(foot.X, foot.Z), s.StepRadius, spin, s.StepPuff * s.WakeStrength, 1.0f);
    }

    /// <summary>
    /// Adds a whirl of <paramref name="spin"/> m/s (counter-clockwise seen from above when positive)
    /// and an outward <paramref name="puff"/> m/s within <paramref name="radius"/>, times <paramref name="rate"/>.
    /// </summary>
    public void Swirl(Vector2 at, float radius, float spin, float puff, float rate)
    {
        if (radius <= 0.0f)
        {
            return;
        }
        ForCells(at, radius, (i, offset, distance) =>
        {
            float r = distance / radius;
            float fall = 1.0f - r;
            Vector2 outward = Vector2.Zero;
            if (distance > 0.001f)
            {
                outward = offset / distance;
            }
            Vector2 around = new(-outward.Y, outward.X);
            float turn = 4.0f * r * fall * fall;
            Vector2 push = around * spin * turn + outward * puff * fall * fall;
            FlowX[i] += push.X * rate;
            FlowZ[i] += push.Y * rate;
        });
    }

    private void ForCells(Vector2 at, float radius, Action<int, Vector2, float> each)
    {
        int x0 = Mathf.FloorToInt((at.X - radius) / Cell) - OriginX;
        int x1 = Mathf.FloorToInt((at.X + radius) / Cell) - OriginX;
        int z0 = Mathf.FloorToInt((at.Y - radius) / Cell) - OriginZ;
        int z1 = Mathf.FloorToInt((at.Y + radius) / Cell) - OriginZ;
        x0 = Math.Max(x0, 0);
        z0 = Math.Max(z0, 0);
        x1 = Math.Min(x1, Size - 1);
        z1 = Math.Min(z1, Size - 1);
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                int i = Index(x, z);
                if (Solid[i])
                {
                    continue;
                }
                Vector2 offset = CellCentre(x, z) - at;
                float distance = offset.Length();
                if (distance >= radius)
                {
                    continue;
                }
                each(i, offset, distance);
            }
        }
    }

    // --- The step ---

    public void Step(float dt)
    {
        _watch.Restart();
        if (_settledDirty)
        {
            UpdateSettled();
        }
        Forces(dt);
        Advect(dt);
        Diffuse(dt);
        Recover(dt);
        Walls();
        Time += dt;
        Steps++;
        LastStepMs = _watch.Elapsed.TotalMilliseconds;
    }

    /// <summary>The density each cell settles to: Ambient, more in hollows (how far it lies below the mean of the ground around it).</summary>
    private void UpdateSettled()
    {
        GroundFogSettings s = Settings;
        int n = Size;
        int r = Math.Max(1, s.PoolRadius);
        // Box mean of the known ground: along X, then along Z (sums and counts).
        for (int z = 0; z < n; z++)
        {
            float sum = 0.0f;
            float count = 0.0f;
            for (int x = -r; x < n; x++)
            {
                int enter = x + r;
                if (enter < n)
                {
                    int i = Index(enter, z);
                    if (Ground[i] == Known)
                    {
                        sum += Height[i];
                        count += 1.0f;
                    }
                }
                int leave = x - r - 1;
                if (leave >= 0)
                {
                    int i = Index(leave, z);
                    if (Ground[i] == Known)
                    {
                        sum -= Height[i];
                        count -= 1.0f;
                    }
                }
                if (x >= 0)
                {
                    _surface[Index(x, z)] = sum;
                    _density2[Index(x, z)] = count;
                }
            }
        }
        for (int x = 0; x < n; x++)
        {
            float sum = 0.0f;
            float count = 0.0f;
            for (int z = -r; z < n; z++)
            {
                int enter = z + r;
                if (enter < n)
                {
                    int i = Index(x, enter);
                    sum += _surface[i];
                    count += _density2[i];
                }
                int leave = z - r - 1;
                if (leave >= 0)
                {
                    int i = Index(x, leave);
                    sum -= _surface[i];
                    count -= _density2[i];
                }
                if (z >= 0)
                {
                    _sum[Index(x, z)] = sum;
                    _count[Index(x, z)] = count;
                }
            }
        }
        for (int i = 0; i < Settled.Length; i++)
        {
            if (Solid[i])
            {
                Settled[i] = 0.0f;
                continue;
            }
            if (Ground[i] != Known || _count[i] < 0.5f)
            {
                Settled[i] = s.Ambient;
                continue;
            }
            float mean = _sum[i] / _count[i];
            float hollow = Mathf.Clamp(mean - Height[i], 0.0f, s.PoolMaxDepth);
            Settled[i] = Mathf.Min(s.Ambient * (1.0f + s.PoolBoost * hollow), s.MaxDensity);
        }
        _settledDirty = false;
    }

    private void Forces(float dt)
    {
        GroundFogSettings s = Settings;
        int n = Size;
        for (int i = 0; i < _surface.Length; i++)
        {
            _surface[i] = Height[i] + s.Depth * Density[i];
        }
        // Swirls: the curl of a slowly drifting stream function in world space (so the pattern stays
        // put when the patch shifts), divergence-free so it stirs without thinning:
        // P = sin(k x + 0.9 t) cos(1.3 k z - 0.7 t) + 0.6 sin(1.7 k (0.7 x - 1.1 z) + 1.3 t).
        // Each factor depends on x or on z alone, so it's worked out once per column and per row.
        float k = 1.0f / Mathf.Max(s.TurbulenceScale, 0.1f);
        float t = (float)Time * s.TurbulenceRate * 6.0f;
        for (int j = 0; j < n; j++)
        {
            float wx = (OriginX + j + 0.5f) * Cell;
            float wz = (OriginZ + j + 0.5f) * Cell;
            _sinX[j] = MathF.Sin(k * wx + t * 0.9f);
            _cosX[j] = MathF.Cos(k * wx + t * 0.9f);
            _sinZ[j] = MathF.Sin(k * 1.3f * wz - t * 0.7f);
            _cosZ[j] = MathF.Cos(k * 1.3f * wz - t * 0.7f);
            float a = k * 1.7f * 0.7f * wx + t * 1.3f;
            float b = k * 1.7f * 1.1f * wz;
            _sinA[j] = MathF.Sin(a);
            _cosA[j] = MathF.Cos(a);
            _sinB[j] = MathF.Sin(b);
            _cosB[j] = MathF.Cos(b);
        }
        float drag = MathF.Exp(-s.Drag * dt);
        System.Threading.Tasks.Parallel.For(0, n, z => ForcesRow(z, dt, drag));
    }

    private void ForcesRow(int z, float dt, float drag)
    {
        GroundFogSettings s = Settings;
        int n = Size;
        float maxFlow = s.MaxFlow;
        float maxSlope = s.MaxSlope;
        float speed = s.Speed;
        float turbulence = s.Turbulence;
        for (int x = 0; x < n; x++)
        {
            int i = z * n + x;
            if (Solid[i])
            {
                FlowX[i] = 0.0f;
                FlowZ[i] = 0.0f;
                continue;
            }
            float ax = 0.0f;
            float az = 0.0f;
            if (Ground[i] == Known)
            {
                float gx = Slope(x, z, 1, 0);
                float gz = Slope(x, z, 0, 1);
                float g = MathF.Sqrt(gx * gx + gz * gz);
                if (g > maxSlope)
                {
                    gx *= maxSlope / g;
                    gz *= maxSlope / g;
                }
                ax -= speed * gx;
                az -= speed * gz;
            }
            // cos(a - b), the second term's derivative factor.
            float second = _cosA[x] * _cosB[z] + _sinA[x] * _sinB[z];
            float dPdx = _cosX[x] * _cosZ[z] + 0.714f * second;
            float dPdz = -1.3f * _sinX[x] * _sinZ[z] - 1.122f * second;
            ax += turbulence * dPdz;
            az -= turbulence * dPdx;

            float u = (FlowX[i] + ax * dt) * drag;
            float w = (FlowZ[i] + az * dt) * drag;
            float fast = MathF.Sqrt(u * u + w * w);
            if (fast > maxFlow)
            {
                u *= maxFlow / fast;
                w *= maxFlow / fast;
            }
            FlowX[i] = u;
            FlowZ[i] = w;
        }
    }

    /// <summary>No flow into what blocks it (it parts around trunks and rocks), and none inside it.</summary>
    private void Walls()
    {
        int n = Size;
        System.Threading.Tasks.Parallel.For(0, n, z =>
        {
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                if (Solid[i])
                {
                    FlowX[i] = 0.0f;
                    FlowZ[i] = 0.0f;
                    continue;
                }
                float u = FlowX[i];
                float w = FlowZ[i];
                if ((u > 0.0f && Blocked(x + 1, z)) || (u < 0.0f && Blocked(x - 1, z)))
                {
                    FlowX[i] = 0.0f;
                }
                if ((w > 0.0f && Blocked(x, z + 1)) || (w < 0.0f && Blocked(x, z - 1)))
                {
                    FlowZ[i] = 0.0f;
                }
            }
        });
    }

    private bool Blocked(int x, int z)
    {
        if (x < 0 || z < 0 || x >= Size || z >= Size)
        {
            return false;
        }
        return Solid[Index(x, z)];
    }

    /// <summary>Slope of the fog's surface across a cell along one axis (central, or one-sided next to blocked or unknown cells).</summary>
    private float Slope(int x, int z, int ax, int az)
    {
        int i = Index(x, z);
        float here = _surface[i];
        float ahead = here;
        float back = here;
        float span = 0.0f;
        int xa = x + ax;
        int za = z + az;
        if (xa < Size && za < Size && Open(Index(xa, za)))
        {
            ahead = _surface[Index(xa, za)];
            span += Cell;
        }
        int xb = x - ax;
        int zb = z - az;
        if (xb >= 0 && zb >= 0 && Open(Index(xb, zb)))
        {
            back = _surface[Index(xb, zb)];
            span += Cell;
        }
        if (span <= 0.0f)
        {
            return 0.0f;
        }
        return (ahead - back) / span;
    }

    private bool Open(int i) => Ground[i] == Known && !Solid[i];

    private void Advect(float dt)
    {
        GroundFogSettings s = Settings;
        int n = Size;
        float back = dt / Cell;
        System.Threading.Tasks.Parallel.For(0, n, z =>
        {
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                if (Solid[i])
                {
                    _density2[i] = 0.0f;
                    _flowX2[i] = 0.0f;
                    _flowZ2[i] = 0.0f;
                    continue;
                }
                float px = x - FlowX[i] * back;
                float pz = z - FlowZ[i] * back;
                if (!Sample(px, pz, out float d, out float u, out float w))
                {
                    d = Density[i];
                    u = FlowX[i];
                    w = FlowZ[i];
                }
                // Converging flow piles the fog up, spreading flow thins it.
                float divergence = (Flow(FlowX, x + 1, z) - Flow(FlowX, x - 1, z) + Flow(FlowZ, x, z + 1) - Flow(FlowZ, x, z - 1)) / (2.0f * Cell);
                d *= Mathf.Clamp(1.0f - divergence * dt, 0.5f, 1.5f);
                _density2[i] = Mathf.Clamp(d, 0.0f, s.MaxDensity);
                _flowX2[i] = u;
                _flowZ2[i] = w;
            }
        });
        Array.Copy(_density2, Density, Density.Length);
        Array.Copy(_flowX2, FlowX, FlowX.Length);
        Array.Copy(_flowZ2, FlowZ, FlowZ.Length);
    }

    private float Flow(float[] field, int x, int z)
    {
        x = Math.Clamp(x, 0, Size - 1);
        z = Math.Clamp(z, 0, Size - 1);
        return field[Index(x, z)];
    }

    /// <summary>Bilinear sample at a cell position (cell centres at whole numbers), over the open cells only; false if all four are blocked.</summary>
    private bool Sample(float px, float pz, out float d, out float u, out float w)
    {
        px = Mathf.Clamp(px, 0.0f, Size - 1.001f);
        pz = Mathf.Clamp(pz, 0.0f, Size - 1.001f);
        int x0 = (int)px;
        int z0 = (int)pz;
        float fx = px - x0;
        float fz = pz - z0;
        d = 0.0f;
        u = 0.0f;
        w = 0.0f;
        float total = 0.0f;
        Add(x0, z0, (1.0f - fx) * (1.0f - fz), ref d, ref u, ref w, ref total);
        Add(x0 + 1, z0, fx * (1.0f - fz), ref d, ref u, ref w, ref total);
        Add(x0, z0 + 1, (1.0f - fx) * fz, ref d, ref u, ref w, ref total);
        Add(x0 + 1, z0 + 1, fx * fz, ref d, ref u, ref w, ref total);
        if (total < 0.0001f)
        {
            return false;
        }
        d /= total;
        u /= total;
        w /= total;
        return true;
    }

    private void Add(int x, int z, float weight, ref float d, ref float u, ref float w, ref float total)
    {
        int i = Index(x, z);
        if (Solid[i] || weight <= 0.0f)
        {
            return;
        }
        d += Density[i] * weight;
        u += FlowX[i] * weight;
        w += FlowZ[i] * weight;
        total += weight;
    }

    private void Diffuse(float dt)
    {
        GroundFogSettings s = Settings;
        float kd = Mathf.Clamp(s.Diffusion * dt / (Cell * Cell), 0.0f, 0.2f);
        float kv = Mathf.Clamp(s.Viscosity * dt / (Cell * Cell), 0.0f, 0.2f);
        Blur(Density, _density2, kd);
        Blur(FlowX, _flowX2, kv);
        Blur(FlowZ, _flowZ2, kv);
    }

    private void Blur(float[] field, float[] scratch, float k)
    {
        if (k <= 0.0f)
        {
            return;
        }
        int n = Size;
        System.Threading.Tasks.Parallel.For(0, n, z =>
        {
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                if (Solid[i])
                {
                    scratch[i] = field[i];
                    continue;
                }
                float here = field[i];
                float sum = 0.0f;
                sum += Neighbour(field, x + 1, z, here);
                sum += Neighbour(field, x - 1, z, here);
                sum += Neighbour(field, x, z + 1, here);
                sum += Neighbour(field, x, z - 1, here);
                scratch[i] = here + k * (sum - 4.0f * here);
            }
        });
        Array.Copy(scratch, field, field.Length);
    }

    private float Neighbour(float[] field, int x, int z, float here)
    {
        if (x < 0 || z < 0 || x >= Size || z >= Size)
        {
            return here;
        }
        int i = Index(x, z);
        if (Solid[i])
        {
            return here;
        }
        return field[i];
    }

    private void Recover(float dt)
    {
        float k = 1.0f - Mathf.Exp(-dt / Mathf.Max(Settings.RecoveryTime, 0.05f));
        for (int i = 0; i < Density.Length; i++)
        {
            if (Solid[i])
            {
                Density[i] = 0.0f;
                continue;
            }
            Density[i] += (Settled[i] - Density[i]) * k;
        }
    }

    // --- Reading it ---

    /// <summary>Density at a world point (bilinear over cell centres); Ambient off the patch.</summary>
    public float DensityAt(float worldX, float worldZ)
    {
        float px = worldX / Cell - OriginX - 0.5f;
        float pz = worldZ / Cell - OriginZ - 0.5f;
        if (px < 0.0f || pz < 0.0f || px > Size - 1 || pz > Size - 1)
        {
            return Settings.Ambient;
        }
        if (!Sample(px, pz, out float d, out _, out _))
        {
            return 0.0f;
        }
        return d;
    }

    /// <summary>Flow at a world point, m/s along X and Z.</summary>
    public Vector2 FlowAt(float worldX, float worldZ)
    {
        float px = worldX / Cell - OriginX - 0.5f;
        float pz = worldZ / Cell - OriginZ - 0.5f;
        if (!Sample(px, pz, out _, out float u, out float w))
        {
            return Vector2.Zero;
        }
        return new Vector2(u, w);
    }

    /// <summary>Mean density over the cells within <paramref name="radius"/> of a world point.</summary>
    public float MeanDensity(Vector2 at, float radius)
    {
        float sum = 0.0f;
        int count = 0;
        ForCells(at, radius, (i, offset, distance) =>
        {
            sum += Density[i];
            count++;
        });
        if (count == 0)
        {
            return 0.0f;
        }
        return sum / count;
    }

    public float TotalDensity()
    {
        double sum = 0.0;
        foreach (float d in Density)
        {
            sum += d;
        }
        return (float)sum;
    }

    public float FastestFlow()
    {
        float most = 0.0f;
        for (int i = 0; i < FlowX.Length; i++)
        {
            most = Mathf.Max(most, FlowX[i] * FlowX[i] + FlowZ[i] * FlowZ[i]);
        }
        return Mathf.Sqrt(most);
    }

    /// <summary>True if every number is finite (no blow-up).</summary>
    public bool Finite()
    {
        for (int i = 0; i < Density.Length; i++)
        {
            if (!float.IsFinite(Density[i]) || !float.IsFinite(FlowX[i]) || !float.IsFinite(FlowZ[i]))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Writes the patch for the shader: per cell density, flow X, flow Z and how much of it is
    /// drawn (0 over unknown ground), and the ground heights with unknown cells filled from their
    /// known neighbours so the fog's sheet doesn't drop to zero at the patch's fringe.
    /// </summary>
    public void Export(float[] fog, float[] heights)
    {
        int n = Size;
        for (int i = 0; i < Density.Length; i++)
        {
            int o = i * 4;
            fog[o] = Density[i];
            fog[o + 1] = FlowX[i];
            fog[o + 2] = FlowZ[i];
            float shown = 0.0f;
            if (Ground[i] == Known)
            {
                shown = 1.0f;
            }
            fog[o + 3] = shown;
            heights[i] = Height[i];
        }
        FillUnknown(heights, n);
    }

    private readonly List<int> _fillRow = new();

    private void FillUnknown(float[] heights, int n)
    {
        bool any = false;
        float known = 0.0f;
        for (int i = 0; i < Ground.Length; i++)
        {
            if (Ground[i] == Known)
            {
                any = true;
                known = Height[i];
                break;
            }
        }
        if (!any)
        {
            return;
        }
        // Along each row (nearest known to the left, else to the right), then rows with none from the row above or below.
        bool[] rowKnown = new bool[n];
        for (int z = 0; z < n; z++)
        {
            int last = -1;
            _fillRow.Clear();
            for (int x = 0; x < n; x++)
            {
                int i = Index(x, z);
                if (Ground[i] == Known)
                {
                    if (last < 0)
                    {
                        foreach (int j in _fillRow)
                        {
                            heights[j] = Height[i];
                        }
                    }
                    last = i;
                    continue;
                }
                if (last >= 0)
                {
                    heights[i] = heights[last];
                }
                else
                {
                    _fillRow.Add(i);
                }
            }
            rowKnown[z] = last >= 0;
        }
        for (int z = 0; z < n; z++)
        {
            if (rowKnown[z])
            {
                continue;
            }
            int from = -1;
            for (int step = 1; step < n && from < 0; step++)
            {
                if (z - step >= 0 && rowKnown[z - step])
                {
                    from = z - step;
                }
                else if (z + step < n && rowKnown[z + step])
                {
                    from = z + step;
                }
            }
            for (int x = 0; x < n; x++)
            {
                if (from >= 0)
                {
                    heights[Index(x, z)] = heights[Index(x, from)];
                }
                else
                {
                    heights[Index(x, z)] = known;
                }
            }
        }
    }
}
