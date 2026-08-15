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
    public static string FormatCoord(int value) =>
        FormatTileAndTenths((value >> 24) & 0xFF, value);

    /// <summary>
    /// Format the ship's world Y coordinate with one decimal place — NOT
    /// wrapped: altitude is not periodic. Positive is down toward terrain,
    /// negative is up.
    /// </summary>
    public static string FormatAltitude(int value)
    {
        if (value >= 0)
            return FormatTileAndTenths(value >> 24, value);

        // Negative: format the magnitude with a leading sign — the floor+frac
        // form used by FormatCoord would misrepresent negatives (e.g. -0.5
        // would read as "-1.5").
        uint mag = unchecked((uint)(-value));
        int tile = (int)(mag >> 24);
        int tenths = (int)((mag & 0x00FFFFFF) * 10 / FixedPoint.TILE_SIZE);
        return $"-{tile}.{tenths}";
    }

    /// <summary>Full HUD line: ship coordinates on all three axes (Y is the raw, unwrapped altitude).</summary>
    public static string FormatHud(int xFixed, int yFixed, int zFixed) =>
        $"X {FormatCoord(xFixed)} Y {FormatAltitude(yFixed)} Z {FormatCoord(zFixed)}";

    private static string FormatTileAndTenths(int tile, int value)
    {
        int tenths = (value & 0x00FFFFFF) * 10 / FixedPoint.TILE_SIZE;  // lower 24 bits, always positive
        return $"{tile}.{tenths}";
    }
}
