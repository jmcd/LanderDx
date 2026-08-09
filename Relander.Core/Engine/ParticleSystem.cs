using Relander.Core.Math;
using Relander.Core.Data;

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

    // Particle data: flat array, 8 ints per particle, max 484 particles
    private readonly int[] _data = new int[FixedPoint.MAX_PARTICLES * 8];
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
        ObjectMap objectMap, GraphicsBuffers buffers)
    {
        _state = state;
        _landscape = landscape;
        _objectMap = objectMap;
        _buffers = buffers;
        _endIndex = 0;
        _data[P_FLAGS] = 0;  // Null terminator
    }

    public int Count => _endIndex;

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

            // Check terrain collision
            int terrainAlt = _landscape.GetAltitude(x, z);

            // Object destruction
            if ((flags & FLAG_DESTROY) != 0)
            {
                int tileAlt = _landscape.GetAltitude(x, z);
                if (y - tileAlt < FixedPoint.SAFE_HEIGHT)
                {
                    int objType = _objectMap.GetObjectAt(x, z);
                    if (ObjectTypes.IsLiveObject(objType))
                    {
                        // Destroy the object!
                        _objectMap.SetObjectAt(x, z, (byte)(objType + 12));
                        _state.CurrentScore += FixedPoint.SCORE_PER_DESTROY;
                    }
                }
            }

            // Bounce / splash on ground
            if (y >= terrainAlt)
            {
                if (terrainAlt >= FixedPoint.SEA_LEVEL && (flags & FLAG_SPLASH) != 0)
                {
                    // Splash — delete particle
                    DeleteParticle(idx);
                    continue;
                }
                if ((flags & FLAG_EXPLODE) != 0)
                {
                    // Explosion — delete particle
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

    private void DrawParticle(int x, int y, int z, int flags)
    {
        // Convert to camera-relative
        int cx = x - _state.XCamera;
        int cy = y - _state.YCamera;
        int cz = z - _state.ZCamera + FixedPoint.LANDSCAPE_Z;

        // Visibility culling
        if ((uint)cz >= (uint)FixedPoint.LANDSCAPE_Z_BEYOND) return;
        if (cz < FixedPoint.LANDSCAPE_Z_FRONT) return;
        if (global::System.Math.Abs(cx) >= FixedPoint.LANDSCAPE_X_HALF) return;

        // Project to screen
        if (!Projection.Project(cx, cy, cz, out int screenX, out int screenY))
            return;

        // Clamp to screen
        if (!Projection.IsOnScreen(screenX, screenY))
            return;

        // Determine particle size from z-depth (0-8, larger = closer = bigger)
        int size = global::System.Math.Clamp((int)((uint)cz >> 24), 0, 8);
        byte colour = (byte)(flags & 0xFF);

        // Draw particle
        int bufferIdx = _buffers.GetBufferIndex(cz - FixedPoint.TILE_SIZE);
        _buffers.AddParticle(bufferIdx, size, screenX, screenY, colour);

        // Draw shadow (one buffer back, command 9-17)
        int shadowIdx = _buffers.GetShadowBufferIndex(cz - FixedPoint.TILE_SIZE);
        _buffers.AddParticle(shadowIdx, size + 9, screenX, screenY, 0);
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

    /// <summary>Spawn exhaust particles when the engine is firing.</summary>
    public void SpawnExhaust(int x, int y, int z, int vx, int vy, int vz)
    {
        // Simplified exhaust: small particles near the ship
        for (int i = 0; i < 3; i++)
        {
            int px = x + ((i - 1) << 20);
            int py = y + 0x80000;
            int pz = z;
            int flags = FLAG_GRAVITY | FLAG_BOUNCE | VidcColour.Encode(15, 8, 0);  // Orange
            AddParticle(px, py, pz,
                vx >> 3, vy >> 3, vz >> 3,
                8, flags);
        }
    }
}
