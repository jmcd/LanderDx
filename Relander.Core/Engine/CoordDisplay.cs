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

    /// <summary>
    /// Full HUD line in player-facing terms: X is the east-west ground axis
    /// (world X), Y is the other ground axis (world Z — displayed as Y since
    /// players read ground axes as X/Y), and Alt is the height above the
    /// terrain directly below the ship (ground altitude minus ship Y — the
    /// world's Y grows downward, so this reads positive when airborne).
    /// The ground axes wrap with the periodic world; Alt never wraps.
    /// </summary>
    public static string FormatHud(int groundX, int groundZ, int yFixed, int groundAltitude) =>
        $"X {FormatCoord(groundX)} Y {FormatCoord(groundZ)} Alt {FormatAltitude(groundAltitude - yFixed)}";

    private static string FormatTileAndTenths(int tile, int value)
    {
        int tenths = (value & 0x00FFFFFF) * 10 / FixedPoint.TILE_SIZE;  // lower 24 bits, always positive
        return $"{tile}.{tenths}";
    }

    // ---- Landing status (opt-in HUD panel, P key) ----

    /// <summary>Format a fixed-point speed in tiles per tick with two decimals (e.g. "0.12").</summary>
    public static string FormatSpeed(int speed)
    {
        int hundredths = (int)((long)speed * 100 / FixedPoint.TILE_SIZE);
        return $"{hundredths / 100}.{hundredths % 100:D2}";
    }

    /// <summary>True when the total speed is below the landing limit — the same
    /// check the landing code makes (PlayerController.CheckCollisionAndLanding:
    /// (uint)totalSpeed &lt; LANDING_SPEED).</summary>
    public static bool IsLandingSpeed(int totalSpeed) =>
        (uint)totalSpeed < FixedPoint.LANDING_SPEED;

    /// <summary>True when the ship is inside the 8×8-tile launchpad acceptance box —
    /// the same check the landing code makes.</summary>
    public static bool IsOverPad(int x, int z) =>
        (uint)x < FixedPoint.LAUNCHPAD_SIZE && (uint)z < FixedPoint.LAUNCHPAD_SIZE;

    /// <summary>The landing panel's speed line, e.g. "SPD 0.08 OK" or "SPD 0.20 !".</summary>
    public static string FormatSpeedLine(int totalSpeed) =>
        $"SPD {FormatSpeed(totalSpeed)} {(IsLandingSpeed(totalSpeed) ? "OK" : "!")}";

    /// <summary>The landing panel's pad line, e.g. "PAD IN" or "PAD OUT".</summary>
    public static string FormatPadLine(bool overPad) => overPad ? "PAD IN" : "PAD OUT";

    /// <summary>The landing panel's cue line: "LAND OK" when the descent would
    /// land safely (over the pad and below the speed limit), "LAND -" otherwise.</summary>
    public static string FormatLandCue(bool overPad, int totalSpeed) =>
        overPad && IsLandingSpeed(totalSpeed) ? "LAND OK" : "LAND -";
}
