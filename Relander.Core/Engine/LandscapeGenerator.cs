using Relander.Core.Math;

namespace Relander.Core.Engine;

/// <summary>
/// Procedural landscape generation using Fourier synthesis (Lander.arm:1285-1465)
/// and tile colour computation (Lander.arm:1531-1724).
/// </summary>
public class LandscapeGenerator
{
    private readonly GameState _state;

    public LandscapeGenerator(GameState state)
    {
        _state = state;
    }

    /// <summary>
    /// Look up sin(2π * index / 1024) * (2^31 - 1) from the sine table.
    /// Uses unsigned (logical) shift to match ARM LSR behaviour.
    /// </summary>
    private static int Sin(int value)
    {
        int index = (int)((uint)value >> 22) & 0x3FF;
        return SineTable.Data[index];
    }

    /// <summary>
    /// Calculate the landscape altitude at world coordinates (x, z).
    /// Returns altitude in fixed-point format.
    /// </summary>
    public int GetAltitude(int x, int z)
    {
        _state.PrevAltitude = _state.Altitude;

        // Term 1: sin(x - 2z) / 128
        int r0 = Sin(x - (z << 1)) >> 7;

        // Term 2: sin(4x + 3z) / 128
        int r1 = z + (x << 1);
        r1 = z + (r1 << 1);
        int r3 = r1 + x;  // 5x + 3z (saved)
        r0 += Sin(r1) >> 7;

        // Term 3: sin(3z - 5x) / 128
        r1 = z - (x << 1);
        r1 = (r1 << 1) - x;
        r1 += z;
        r0 += Sin(r1) >> 7;

        // Term 4: sin(7x + 5z) / 128
        r1 = z + (x << 1);
        r1 = z + (r1 << 2);
        r1 -= x;
        r0 += Sin(r1) >> 7;

        // Term 5: sin(5x + 11z) / 256
        r1 = r3 + (z << 3);
        r0 += Sin(r1) >> 8;

        // Term 6: sin(10x + 7z) / 256
        r1 = z + (r3 << 1);
        r0 += Sin(r1) >> 8;

        r0 = FixedPoint.LAND_MID_HEIGHT - r0;

        if (r0 > FixedPoint.SEA_LEVEL)
            r0 = FixedPoint.SEA_LEVEL;

        if ((uint)x < FixedPoint.LAUNCHPAD_SIZE && (uint)z < FixedPoint.LAUNCHPAD_SIZE)
            r0 = FixedPoint.LAUNCHPAD_ALTITUDE;

        _state.Altitude = r0;
        return r0;
    }

    /// <summary>
    /// Calculate landscape altitude below a camera-relative vertex.
    /// </summary>
    public int GetAltitudeBelowVertex(int vertexX, int vertexZ)
    {
        int worldX = vertexX + _state.XCamera;
        int worldZ = vertexZ + _state.ZCamera - FixedPoint.LANDSCAPE_Z;
        return GetAltitude(worldX, worldZ) - _state.YCamera;
    }

    /// <summary>
    /// Compute the colour for a landscape tile at the current altitude,
    /// based on slope and distance (Lander.arm:1531-1724).
    /// Returns a 4-pixel VIDC colour word.
    /// </summary>
    public int GetTileColour(int tileCornerRow)
    {
        int alt = _state.Altitude;
        int prevAlt = _state.PrevAltitude;

        // Slope: max(0, prevAltitude - altitude) — left-facing tiles are brighter
        int slope = prevAlt - alt;
        if (slope < 0) slope = 0;

        // Base colour channels from altitude bits
        // Green: bit 3 of altitude → 4 or 8
        int g = ((alt >> 3) & 1) * 4 + 4;
        // Red: bit 2 of altitude → 0 or 4 (Lander.arm:1608: AND R0, R4, #%00000100)
        int r = alt & 4;
        int b = 0;  // Blue only for sea

        // Launchpad → grey
        if (alt == FixedPoint.LAUNCHPAD_ALTITUDE)
        {
            r = 4; g = 4; b = 4;
        }

        // Sea level (both current AND previous) → blue
        if (alt == FixedPoint.SEA_LEVEL && prevAlt == FixedPoint.SEA_LEVEL)
        {
            r = 0; g = 0; b = 4;
        }

        // Brightness: row number (1-10, back to front) + slope component
        int brightness = tileCornerRow + (int)((uint)slope >> 22);

        r = global::System.Math.Min(r + brightness, 15);
        g = global::System.Math.Min(g + brightness, 15);
        b = global::System.Math.Min(b + brightness, 15);

        byte vidc = VidcColour.Encode(r, g, b);
        return VidcColour.ReplicateQuad(vidc);
    }
}
