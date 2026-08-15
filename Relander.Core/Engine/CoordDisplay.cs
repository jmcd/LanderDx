using Relander.Core.Math;

namespace Relander.Core.Engine;

/// <summary>
/// Formats fixed-point world coordinates for the HUD coordinate display —
/// an opt-in enhancement (P key) with no original counterpart. The launchpad
/// occupies tiles 0-8 on both axes and the world wraps every 256 tiles.
/// </summary>
public static class CoordDisplay
{
    /// <summary>
    /// Format a fixed-point coordinate as tile numbers with one decimal place,
    /// e.g. "12.3". The tile part is the floor (arithmetic shift right by 24,
    /// as the original's tile addressing) wrapped to the 256-tile periodic
    /// world, matching the minimap — so the launchpad always reads as 0..8.
    /// </summary>
    public static string FormatCoord(int value)
    {
        int tile = (value >> 24) & 0xFF;            // floor for negative values, wrapped to 0..255
        int frac = value & 0x00FFFFFF;              // lower 24 bits, always positive
        int tenths = frac * 10 / FixedPoint.TILE_SIZE;
        return $"{tile}.{tenths}";
    }

    /// <summary>Full HUD line: ship tile coordinates on both ground axes.</summary>
    public static string FormatHud(int xFixed, int zFixed) =>
        $"X {FormatCoord(xFixed)}  Z {FormatCoord(zFixed)}";
}
