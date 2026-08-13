using Relander.Core.Math;
using Relander.Core.Data;
using Relander.Core.Interfaces;

namespace Relander.Core.Engine;

/// <summary>
/// Particle system: 8-word particle buffer, per-frame update, and drawing.
/// Based on MoveAndDrawParticles from Lander.arm:2780-3843.
/// </summary>
public class ParticleSystem
{
    private readonly GameState _state;
    private readonly LandscapeGenerator _landscape;
    private readonly ObjectMap _objectMap;
    private readonly GraphicsBuffers _buffers;
    private readonly IRandomSource _random;

    // Particle data: flat array, 8 ints per particle, max 484 particles.
    // Sized MAX_PARTICLES+1 slots so the null terminator written after the last
    // particle (at index MAX_PARTICLES*8) is always within bounds.
    private readonly int[] _data = new int[(FixedPoint.MAX_PARTICLES + 1) * 8];
    private int _endIndex;  // Next write position in 8-int units

    // Particle field offsets (8 words = 32 bytes per particle)
    private const int P_X = 0, P_Y = 1, P_Z = 2;
    private const int P_VX = 3, P_VY = 4, P_VZ = 5;
    private const int P_LIFE = 6, P_FLAGS = 7;

    // Flag bits
    public const int FLAG_FADE = 1 << 16;     // Colour fades white → red
    public const int FLAG_ROCK = 1 << 17;     // Is a rock (3D object, collides with ship)
    public const int FLAG_SPLASH = 1 << 18;   // Splash on sea impact
    public const int FLAG_BOUNCE = 1 << 19;   // Bounce on ground
    public const int FLAG_GRAVITY = 1 << 20;  // Gravity applies
    public const int FLAG_DESTROY = 1 << 21;  // Can destroy objects
    public const int FLAG_BIG_SPLASH = 1 << 23;
    public const int FLAG_EXPLODE = 1 << 24;  // Explode on ground impact

    public ParticleSystem(GameState state, LandscapeGenerator landscape,
        ObjectMap objectMap, GraphicsBuffers buffers, IRandomSource? random = null)
    {
        _state = state;
        _landscape = landscape;
        _objectMap = objectMap;
        _buffers = buffers;
        _random = random ?? new RandomGenerator(12345);
        _endIndex = 0;
        _data[P_FLAGS] = 0;  // Null terminator
    }

    public int Count => _endIndex;

    /// <summary>Read-only snapshot of a particle's data (for tests and diagnostics).</summary>
    public (int X, int Y, int Z, int VX, int VY, int VZ, int Life, int Flags) GetParticle(int index)
    {
        int i = index * 8;
        return (_data[i + P_X], _data[i + P_Y], _data[i + P_Z],
            _data[i + P_VX], _data[i + P_VY], _data[i + P_VZ],
            _data[i + P_LIFE], _data[i + P_FLAGS]);
    }

    /// <summary>Add a particle to the buffer. Returns false if buffer is full.</summary>
    public bool AddParticle(int x, int y, int z, int vx, int vy, int vz, int lifespan, int flags)
    {
        if (_endIndex >= FixedPoint.MAX_PARTICLES)
            return false;

        int i = _endIndex * 8;
        _data[i + P_X] = x;
        _data[i + P_Y] = y;
        _data[i + P_Z] = z;
        _data[i + P_VX] = vx;
        _data[i + P_VY] = vy;
        _data[i + P_VZ] = vz;
        _data[i + P_LIFE] = lifespan;
        _data[i + P_FLAGS] = flags;

        _endIndex++;
        _data[_endIndex * 8 + P_FLAGS] = 0;  // New null terminator
        return true;
    }

    /// <summary>Add a moving particle with random variation added to velocity and lifespan (Lander.arm:3739-3783).</summary>
    public bool AddMovingParticle(int x, int y, int z, int vx, int vy, int vz, int lifespan, int flags, int velocityShift = 10, int lifeShift = 29)
    {
        var (r0, _) = _random.GetRandomNumbers();
        vx += r0 >> velocityShift;
        var (r2, _) = _random.GetRandomNumbers();
        vy += r2 >> velocityShift;
        var (r4, _) = _random.GetRandomNumbers();
        vz += r4 >> velocityShift;

        var (r6, _) = _random.GetRandomNumbers();
        lifespan += (int)((uint)r6 >> lifeShift);

        return AddParticle(x, y, z, vx, vy, vz, lifespan, flags);
    }

    /// <summary>Process all particles: move, apply physics, draw.</summary>
    public void UpdateAndDraw()
    {
        int idx = 0;
        while (idx < _endIndex)
        {
            int i = idx * 8;
            int flags = _data[i + P_FLAGS];

            // Null terminator check
            if (flags == 0) break;

            // Decrement lifespan
            int life = _data[i + P_LIFE] - 1;
            if (life <= 0)
            {
                DeleteParticle(idx);
                continue;  // Don't advance idx — swapped with last
            }
            _data[i + P_LIFE] = life;

            // Apply velocity
            int x = _data[i + P_X] + _data[i + P_VX];
            int y = _data[i + P_Y] + _data[i + P_VY];
            int z = _data[i + P_Z] + _data[i + P_VZ];

            // Apply gravity
            if ((flags & FLAG_GRAVITY) != 0)
                _data[i + P_VY] += _state.Gravity;

            // Colour fade (FLAG_FADE: white -> yellow -> orange -> red based on lifespan)
            if ((flags & FLAG_FADE) != 0)
            {
                byte fadeColour = GetFadingColour(life);
                flags = (flags & ~0xFF) | fadeColour;
                _data[i + P_FLAGS] = flags;
            }

            // Check terrain collision
            int terrainAlt = _landscape.GetAltitude(x, z);

            // Object destruction
            if ((flags & FLAG_DESTROY) != 0)
            {
                // Only destroy objects when the particle is within SAFE_HEIGHT of the
                // ground at this tile (Lander.arm:3292-3296: R8 = altitude - y, proceed
                // only when R8 < SAFE_HEIGHT unsigned, i.e. 0 <= altitude - y < SAFE_HEIGHT).
                // The unsigned compare also rejects particles below the terrain.
                if ((uint)(terrainAlt - y) < (uint)FixedPoint.SAFE_HEIGHT)
                {
                    int objType = _objectMap.GetObjectAt(x, z);
                    if (ObjectTypes.IsLiveObject(objType))
                    {
                        // Destroy the object!
                        _objectMap.SetObjectAt(x, z, (byte)(objType + 12));
                        // Only bullets score: the original skips the +20 when bit 17
                        // (rock) is set on the hitting particle (Lander.arm:3352-3355)
                        if ((flags & FLAG_ROCK) == 0)
                            _state.CurrentScore += FixedPoint.SCORE_PER_DESTROY;
                        MinimapRenderer.InvalidateCache();
                        AddSmallExplosion(x, y, z);
                        DeleteParticle(idx);
                        continue;
                    }
                }
            }

            // Bounce / splash on ground
            if (y >= terrainAlt)
            {
                if (terrainAlt >= FixedPoint.SEA_LEVEL && (flags & FLAG_SPLASH) != 0)
                {
                    AddSplash(x, terrainAlt, z, (flags & FLAG_BIG_SPLASH) != 0);
                    DeleteParticle(idx);
                    continue;
                }
                if ((flags & FLAG_EXPLODE) != 0)
                {
                    AddSmallExplosion(x, terrainAlt, z);
                    DeleteParticle(idx);
                    continue;
                }
                if ((flags & FLAG_BOUNCE) != 0)
                {
                    // Bounce: halve velocity, negate y
                    y = terrainAlt;
                    _data[i + P_VX] >>= 1;
                    _data[i + P_VY] = -(_data[i + P_VY] >> 1);
                    _data[i + P_VZ] >>= 1;
                }
            }

            _data[i + P_X] = x;
            _data[i + P_Y] = y;
            _data[i + P_Z] = z;

            // Draw particle to buffers
            DrawParticle(x, y, z, flags);

            idx++;
        }
    }

    private static byte GetFadingColour(int life)
    {
        int r = 15;
        int g, b;
        if (life >= 8)
        {
            g = 15;
            b = global::System.Math.Min(15, (life - 8) * 2);
        }
        else
        {
            g = global::System.Math.Max(0, life * 2);
            b = 0;
        }
        return VidcColour.Encode(r, g, b);
    }

    private void DrawParticle(int x, int y, int z, int flags)
    {
        // 3D Rock particle handling (Lander.arm:2930-3025)
        if ((flags & FLAG_ROCK) != 0)
        {
            // 1. Rock vs player ship collision check
            if (_state.PlayingGame != 0)
            {
                int relX = global::System.Math.Abs(x - _state.XPlayer);
                int relZ = global::System.Math.Abs(z - _state.ZPlayer);
                int relY = global::System.Math.Abs(y - _state.YPlayer);

                if (relX < FixedPoint.TILE_SIZE && relZ < FixedPoint.TILE_SIZE && relY < FixedPoint.TILE_SIZE)
                {
                    _state.CrashedFlag = -1;  // Hit by falling rock!
                }
            }

            // 2. Draw 3D rock object using ObjectBlueprints.Rock
            int rockObjX = x - _state.XCamera;
            int rockObjY = y - _state.YCamera;
            int rockObjZ = z - _state.ZCamera + FixedPoint.LANDSCAPE_Z;

            // Visibility culling
            if ((uint)rockObjZ >= (uint)FixedPoint.LANDSCAPE_Z) return;
            if (rockObjZ < FixedPoint.LANDSCAPE_Z_FRONT) return;
            if (global::System.Math.Abs(rockObjX) >= FixedPoint.LANDSCAPE_X_HALF) return;

            ObjectRenderer.DrawObject(ObjectBlueprints.Rock, rockObjX, rockObjY, rockObjZ, x, z, _state, _buffers, _landscape);
            return;
        }

        // Convert to camera-relative
        int cx = x - _state.XCamera;
        int cy = y - _state.YCamera;
        int cz = z - _state.ZCamera + FixedPoint.LANDSCAPE_Z;

        // Visibility culling: particle must be between front and back of visible landscape
        if ((uint)cz >= (uint)FixedPoint.LANDSCAPE_Z) return;       // Too far back
        if (cz < FixedPoint.LANDSCAPE_Z_FRONT) return;              // Too close
        if (global::System.Math.Abs(cx) >= FixedPoint.LANDSCAPE_X_HALF) return;  // Off left/right

        // Project to screen
        if (!Projection.Project(cx, cy, cz, out int screenX, out int screenY))
            return;

        // Clamp to screen
        if (!Projection.IsOnScreen(screenX, screenY))
            return;

        // Determine particle size from z-depth (Lander.arm:8472 cz >> 25, clamped 0..8)
        int size = global::System.Math.Clamp((int)((uint)cz >> 25), 0, 8);
        byte colour = (byte)(flags & 0xFF);

        // Draw particle. The buffer index already includes the +TILE_SIZE offset
        // (Lander.arm:8451-8453: RSB R14, R8, #LANDSCAPE_Z / ADD R14, R14, #TILE_SIZE);
        // passing cz - TILE_SIZE shifted every particle one buffer nearer the camera,
        // so particles overpainted objects and landscape up to a tile nearer than them.
        int bufferIdx = _buffers.GetBufferIndex(cz);
        _buffers.AddParticle(bufferIdx, size, screenX, screenY, colour);

        // Draw shadow on ground (one buffer back, command 9-17)
        int terrainAlt = _landscape.GetAltitude(x, z);
        int shadowCy = terrainAlt - _state.YCamera;
        if (Projection.Project(cx, shadowCy, cz, out int shadowSx, out int shadowSy) && Projection.IsOnScreen(shadowSx, shadowSy))
        {
            int shadowIdx = _buffers.GetShadowBufferIndex(cz);
            _buffers.AddParticle(shadowIdx, size + 9, shadowSx, shadowSy, 0);
        }
    }

    private void DeleteParticle(int index)
    {
        // Swap with last
        _endIndex--;
        if (index < _endIndex)
        {
            int src = _endIndex * 8;
            int dst = index * 8;
            Array.Copy(_data, src, _data, dst, 8);
        }
        _data[_endIndex * 8 + P_FLAGS] = 0;  // Clear old last slot
    }

    /// <summary>Spawn exhaust particles when the engine is firing (Lander.arm:2241-2340).</summary>
    public void SpawnExhaust(int x, int y, int z, int vx, int vy, int vz)
    {
        int pVx = (vx + (_state.XExhaust >> 7)) >> 1;
        int pVy = (vy + (_state.YExhaust >> 7)) >> 1;
        int pVz = (vz + (_state.ZExhaust >> 7)) >> 1;

        int pX = x - pVx + (_state.XExhaust >> 7);
        int pY = y - pVy + (_state.YExhaust >> 7);
        int pZ = z - pVz + (_state.ZExhaust >> 7);

        int count = _state.FuelBurnRate >= 4 ? 8 : 2;
        for (int i = 0; i < count; i++)
        {
            int flags = FLAG_FADE | FLAG_SPLASH | FLAG_BOUNCE | FLAG_GRAVITY;
            AddMovingParticle(pX, pY, pZ, pVx, pVy, pVz, 8, flags, 10, 29);
        }
    }

    /// <summary>Spawn a bullet particle when the fire button is pressed (Lander.arm:2377-2465).</summary>
    public bool SpawnBullet(int x, int y, int z, int vx, int vy, int vz, int xNoseV, int yNoseV, int zNoseV)
    {
        if (_state.CurrentScore <= 0) return false;
        _state.CurrentScore--;

        int pVx = vx + (xNoseV >> 8);
        int pVy = vy + (yNoseV >> 8);
        int pVz = vz + (zNoseV >> 8);

        int pX = x - pVx + (xNoseV >> 7);
        int pY = y - pVy + (yNoseV >> 7);
        int pZ = z - pVz + (zNoseV >> 7);

        int flags = FLAG_SPLASH | FLAG_BOUNCE | FLAG_GRAVITY | FLAG_DESTROY | FLAG_BIG_SPLASH | FLAG_EXPLODE | VidcColour.Encode(15, 15, 15);
        return AddParticle(pX, pY, pZ, pVx, pVy, pVz, 20, flags);
    }

    /// <summary>Add a small explosion cloud to the buffer (Lander.arm:3384-3436).</summary>
    public void AddSmallExplosion(int x, int y, int z)
    {
        for (int cluster = 0; cluster < 3; cluster++)
        {
            // 2 sparks: fade|splash|bounce|gravity, life 8 + rand >> 29, velocity
            // jitter shift 8 (+/-&1000000) — Lander.arm:4247-4267.
            AddMovingParticle(x, y, z, 0, 0, 0, 8, FLAG_FADE | FLAG_SPLASH | FLAG_BOUNCE | FLAG_GRAVITY, 8, 29);
            AddMovingParticle(x, y, z, 0, 0, 0, 8, FLAG_FADE | FLAG_SPLASH | FLAG_BOUNCE | FLAG_GRAVITY, 8, 29);
            // 1 debris particle
            AddDebrisParticle(x, y, z);
            // 1 smoke particle
            AddSmokeParticle(x, y, z);
        }
    }

    /// <summary>
    /// Add a grey smoke particle that slowly rises and bounces (Lander.arm:3885-3977,
    /// AddSmokeParticleToBuffer): colour = (rand & 7) + 3 on all channels, flags =
    /// bounce only (no fade — the previous port made smoke fade white-to-red like a
    /// spark), vy = ~SMOKE_RISING_SPEED (MVN, i.e. -(SMOKE_RISING_SPEED + 1)), life
    /// 15 + rand >> 25 (0..127), velocity jitter +/- 2^19 (shift 13).
    /// </summary>
    private void AddSmokeParticle(int x, int y, int z)
    {
        var (rand0, _) = _random.GetRandomNumbers();
        int grey = (rand0 & 7) + 3;
        byte smokeColor = VidcColour.Encode(grey, grey, grey);
        AddMovingParticle(x, y, z, 0, -(FixedPoint.SMOKE_RISING_SPEED + 1), 0,
            15, FLAG_BOUNCE | smokeColor, 13, 25);
    }

    /// <summary>
    /// Add a purple-brownish-green debris particle that flies out and bounces
    /// (Lander.arm:3997-4247, AddDebrisParticleToBuffer): red = (rand & 7) + 4,
    /// green = (rand >> 29) + 2, blue = (rand >> 30) + 4, flags =
    /// splash|bounce|gravity, life 15 + rand >> 26 (0..63), velocity jitter at
    /// shift 10 (+/-&400000), starting stationary.
    /// </summary>
    private void AddDebrisParticle(int x, int y, int z)
    {
        var (rand0, rand1) = _random.GetRandomNumbers();
        int r = (rand0 & 7) + 4;
        int g = (int)(((uint)rand1 >> 29) + 2);
        int b = (int)(((uint)rand0 >> 30) + 4);
        byte debrisColor = VidcColour.Encode(r, g, b);
        AddMovingParticle(x, y, z, 0, 0, 0, 15,
            FLAG_SPLASH | FLAG_BOUNCE | FLAG_GRAVITY | debrisColor, 10, 26);
    }

    /// <summary>
    /// Splash a particle into the sea (Lander.arm:3413-3436, AddSprayParticleToBuffer
    /// at 4295-4406): blue shades (blue = (rand & 3) + 12, red = green = (rand & 4) + 8),
    /// gravity only — the spray falls straight down and is deleted at the sea surface
    /// (no bounce bit, so BounceParticle's delete branch removes it) — lifespan
    /// 20 + rand >> 26, velocity jitter at shift 10, starting stationary.
    /// </summary>
    public void AddSplash(int x, int y, int z, bool big)
    {
        int count = big ? 65 : 4;
        for (int i = 0; i < count; i++)
        {
            var (rand0, _) = _random.GetRandomNumbers();
            int blue = (rand0 & 3) + 12;
            int grey = (rand0 & 4) + 8;
            byte splashColor = VidcColour.Encode(grey, grey, blue);
            AddMovingParticle(x, y - FixedPoint.SPLASH_HEIGHT, z, 0, 0, 0, 20, FLAG_GRAVITY | splashColor, 10, 26);
        }
    }

    /// <summary>Create a big player crash explosion cloud (Lander.arm:4389-4439, 81 clusters).</summary>
    public void AddBigExplosion(int x, int y, int z, int clusters = 81)
    {
        for (int cluster = 0; cluster < clusters; cluster++)
        {
            // 2 sparks: fade|splash|bounce|gravity, life 8 + rand >> 29 (the
            // original's range 0..8, Lander.arm:4261-4263 — the previous shift
            // 28 let sparks live 8 frames longer), velocity jitter shift 8.
            AddMovingParticle(x, y, z, 0, 0, 0, 8, FLAG_FADE | FLAG_SPLASH | FLAG_BOUNCE | FLAG_GRAVITY, 8, 29);
            AddMovingParticle(x, y, z, 0, 0, 0, 8, FLAG_FADE | FLAG_SPLASH | FLAG_BOUNCE | FLAG_GRAVITY, 8, 29);
            // 1 debris particle
            AddDebrisParticle(x, y, z);
            // 1 smoke particle
            AddSmokeParticle(x, y, z);
        }
    }

    /// <summary>
    /// Drop a rock from the sky (Lander.arm:4103-4224).
    /// Spawns a rock particle with FLAG_ROCK bit set and random purple-brownish color.
    /// </summary>
    public bool DropRock(int x, int y, int z)
    {
        var (rand0, rand1) = _random.GetRandomNumbers();
        int r = (rand0 & 7) + 4;
        int g = (int)(((uint)rand1 >> 29) + 2);
        int b = (int)(((uint)rand0 >> 30) + 4);

        byte color = VidcColour.Encode(r, g, b);
        // Bits 17-23 exactly (Lander.arm:4198: ORR R7, R7, #&00FE0000). The ARM
        // source comment claims bit 24 (explode) is set too, but &00FE0000 does
        // not include it — rocks bounce on landing until their 170-frame life
        // expires; they do not explode on impact.
        int flags = FLAG_ROCK | FLAG_SPLASH | FLAG_BOUNCE | FLAG_GRAVITY | FLAG_DESTROY | FLAG_BIG_SPLASH | color;

        // velocityShift = 12 (smaller horizontal drift) so rock falls straight down in front of camera view
        return AddMovingParticle(x, y, z, 0, 0, 0, 170, flags, 12, 27);
    }
}



