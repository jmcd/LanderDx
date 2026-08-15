using System.Diagnostics;

namespace LanderDx.Core.Engine;

/// <summary>
/// VIDC colour encoding/decoding for the Archimedes 256-colour mode.
/// Converts between 12-bit RGB (4 bits per channel) and the 8-bit VIDC format.
/// Based on Lander.arm:3908-3947 and Lander.arm:5559-5602.
/// </summary>
public static class VidcColour
{
    /// <summary>
    /// Encode 12-bit RGB (4 bits per channel) into an 8-bit VIDC colour byte.
    /// VIDC bit layout:
    ///   bit 7 = blue bit 3
    ///   bit 6 = green bit 3
    ///   bit 5 = green bit 2
    ///   bit 4 = red bit 3
    ///   bit 3 = blue bit 2
    ///   bit 2 = red bit 2
    ///   bit 1 = sum of r,g,b bit 1s
    ///   bit 0 = sum of r,g,b bit 0s
    /// </summary>
    public static byte Encode(int r, int g, int b)
    {
        // Guard against out-of-range inputs in debug builds.
        // All callers should pre-clamp; the mask below is kept for release-build safety
        // but silently wraps (e.g. 16 → 0), which can produce unexpected colours.
        Debug.Assert((uint)r <= 15, $"VidcColour.Encode: r={r} out of 0-15 range");
        Debug.Assert((uint)g <= 15, $"VidcColour.Encode: g={g} out of 0-15 range");
        Debug.Assert((uint)b <= 15, $"VidcColour.Encode: b={b} out of 0-15 range");

        r &= 0xF;
        g &= 0xF;
        b &= 0xF;

        int result = 0;

        // Bits 1-0: OR of bottom two bits from each channel
        result |= (r & 1) | (g & 1) | (b & 1);        // bit 0
        int bit1 = ((r >> 1) & 1) | ((g >> 1) & 1) | ((b >> 1) & 1);
        result |= bit1 << 1;                           // bit 1

        // Bit 2: red bit 2
        if ((r & 4) != 0) result |= 1 << 2;

        // Bit 3: blue bit 2
        if ((b & 4) != 0) result |= 1 << 3;

        // Bit 4: red bit 3
        if ((r & 8) != 0) result |= 1 << 4;

        // Bit 5: green bit 2
        if ((g & 4) != 0) result |= 1 << 5;

        // Bit 6: green bit 3
        if ((g & 8) != 0) result |= 1 << 6;

        // Bit 7: blue bit 3
        if ((b & 8) != 0) result |= 1 << 7;

        return (byte)result;
    }

    /// <summary>
    /// Encode 12-bit RGB colour (packed as &rgb) into a VIDC byte.
    /// </summary>
    public static byte EncodeFromPacked(int packedColour)
    {
        int r = (packedColour >> 8) & 0xF;
        int g = (packedColour >> 4) & 0xF;
        int b = packedColour & 0xF;
        return Encode(r, g, b);
    }

    /// <summary>
    /// Replicate a VIDC colour byte 4 times into a 32-bit word,
    /// used for fast 4-pixel drawing in 8bpp mode.
    /// </summary>
    public static int ReplicateQuad(byte colour)
    {
        return colour | (colour << 8) | (colour << 16) | (colour << 24);
    }

    /// <summary>
    /// Decode a VIDC colour byte to 24-bit RGB (8 bits per channel).
    /// Used for display on modern hardware.
    /// </summary>
    public static (byte r, byte g, byte b) DecodeToRgb24(byte vidc)
    {
        // Extract 4-bit channels from VIDC byte
        // Each bit position in VIDC maps to a specific channel bit
        int r4 = ((vidc >> 4) & 1) << 3 | ((vidc >> 2) & 1) << 2 | ((vidc >> 1) & 1) << 1 | (vidc & 1);
        int g4 = ((vidc >> 6) & 1) << 3 | ((vidc >> 5) & 1) << 2 | ((vidc >> 1) & 1) << 1 | (vidc & 1);
        int b4 = ((vidc >> 7) & 1) << 3 | ((vidc >> 3) & 1) << 2 | ((vidc >> 1) & 1) << 1 | (vidc & 1);

        // Scale 4-bit to 8-bit (multiply by 17: n << 4 | n)
        byte r8 = (byte)((r4 << 4) | r4);
        byte g8 = (byte)((g4 << 4) | g4);
        byte b8 = (byte)((b4 << 4) | b4);

        return (r8, g8, b8);
    }

    /// <summary>
    /// Build a full 256-colour palette mapping VIDC indices to 32-bit RGBA.
    /// </summary>
    public static uint[] BuildPalette()
    {
        var palette = new uint[256];
        for (int i = 0; i < 256; i++)
        {
            var (r, g, b) = DecodeToRgb24((byte)i);
            palette[i] = (uint)(0xFF000000 | (r << 16) | (g << 8) | b);
        }
        return palette;
    }
}
