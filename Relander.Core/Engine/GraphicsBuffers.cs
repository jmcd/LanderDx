using Relander.Core.Math;

namespace Relander.Core.Engine;

/// <summary>
/// Manages the 12 depth-sorted graphics buffers (Lander.arm:8904-9038).
/// Each buffer holds drawing commands (triangles, particles) that are drawn
/// back-to-front for correct occlusion.
///
/// Command format:
///   Terminator: 1 word = [19]
///   Triangle:   8 words = [18, x1, y1, x2, y2, x3, y3, colour]
///   Particle:   2 words = [cmd(0-17), packed(x|colour|y)]
/// </summary>
public class GraphicsBuffers
{
    public const int COMMAND_TRIANGLE = 18;
    public const int COMMAND_TERMINATOR = 19;

    private readonly int[][] _buffers;
    private readonly int[] _endIndices;  // Current write position in each buffer
    private readonly int[] _startIndices;
    private readonly int _bufferCapacity;

    public int BufferCount { get; }

    public GraphicsBuffers(int count = FixedPoint.GRAPHICS_BUFFER_COUNT, int capacity = FixedPoint.BUFFER_SIZE / 4)
    {
        BufferCount = count;
        _bufferCapacity = capacity;
        _buffers = new int[count][];
        _endIndices = new int[count];
        _startIndices = new int[count];

        for (int i = 0; i < count; i++)
        {
            _buffers[i] = new int[capacity];
            _endIndices[i] = 0;
            _startIndices[i] = 0;
        }
    }

    /// <summary>
    /// Get the buffer number for a given screen-depth z-coordinate.
    /// Uses the original formula: LANDSCAPE_Z - cameraRelativeZ + TILE_SIZE,
    /// where cameraRelativeZ = LANDSCAPE_Z - zObject (= worldZ - zCamera).
    /// This simplifies to zObject + TILE_SIZE, clamped to the depth range.
    /// All objects in the foreground clamp to the same near buffer (buffer 10),
    /// with correct depth order from the back-to-front iteration sequence.
    /// </summary>
    public int GetBufferIndex(int zObject)
    {
        int offset = zObject + FixedPoint.TILE_SIZE;
        if (offset > FixedPoint.LANDSCAPE_Z_BEYOND)
            offset = FixedPoint.LANDSCAPE_Z_DEPTH;
        return (int)((uint)offset >> 24) & 0xFF;
    }

    /// <summary>
    /// Get the buffer number for a shadow (one buffer further back).
    /// </summary>
    public int GetShadowBufferIndex(int zObject)
    {
        int offset = zObject;
        if (offset > FixedPoint.LANDSCAPE_Z_BEYOND)
            offset = FixedPoint.LANDSCAPE_Z_DEPTH;
        return (int)((uint)offset >> 24) & 0xFF;
    }

    /// <summary>
    /// Add a triangle command to the specified buffer.
    /// </summary>
    public void AddTriangle(int bufferIndex, int x1, int y1, int x2, int y2, int x3, int y3, int colour)
    {
        if (bufferIndex < 0 || bufferIndex >= BufferCount) return;
        int end = _endIndices[bufferIndex];
        if (end + 8 > _bufferCapacity) return;  // Buffer overflow protection

        var buf = _buffers[bufferIndex];
        buf[end] = COMMAND_TRIANGLE;
        buf[end + 1] = x1;
        buf[end + 2] = y1;
        buf[end + 3] = x2;
        buf[end + 4] = y2;
        buf[end + 5] = x3;
        buf[end + 6] = y3;
        buf[end + 7] = colour;
        _endIndices[bufferIndex] = end + 8;
    }

    /// <summary>
    /// Add a particle pixel to the specified buffer.
    /// </summary>
    public void AddParticle(int bufferIndex, int command, int x, int y, byte colour)
    {
        if (bufferIndex < 0 || bufferIndex >= BufferCount) return;
        int end = _endIndices[bufferIndex];
        if (end + 2 > _bufferCapacity) return;

        var buf = _buffers[bufferIndex];
        buf[end] = command;
        buf[end + 1] = (x << 20) | (colour << 12) | (y & 0xFF);
        _endIndices[bufferIndex] = end + 2;
    }

    /// <summary>
    /// Add terminators to all buffers and reset end pointers.
    /// </summary>
    public void AddTerminators()
    {
        for (int i = 0; i < BufferCount; i++)
        {
            int end = _endIndices[i];
            if (end < _bufferCapacity)
                _buffers[i][end] = COMMAND_TERMINATOR;
            _endIndices[i] = 0;  // Reset for next frame
        }
    }

    /// <summary>
    /// Get a read-only span over a specific buffer's data (up to the terminator).
    /// </summary>
    public ReadOnlySpan<int> GetBufferData(int index)
    {
        if (index < 0 || index >= BufferCount) return [];
        var buf = _buffers[index];
        // Scan for the terminator
        for (int i = 0; i < buf.Length; i++)
            if (buf[i] == COMMAND_TERMINATOR)
                return buf.AsSpan(0, i);
        return [];  // No terminator found = empty
    }

    public void Clear()
    {
        for (int i = 0; i < BufferCount; i++)
        {
            _endIndices[i] = 0;
            _buffers[i][0] = COMMAND_TERMINATOR;  // Ensure empty read
        }
    }
}
