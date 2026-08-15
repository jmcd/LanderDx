namespace LanderDx.Core.Engine;

/// <summary>
/// Static play-area viewport (the 240-row 3D view below the 16-row score bar).
/// Defaults reproduce the original 320×240 exactly. GameEngine configures it
/// from its IScreen at construction, so every test engine self-initialises it.
/// </summary>
public static class Viewport
{
    /// <summary>Play-area width in pixels (320 = original).</summary>
    public static int Width { get; private set; } = 320;

    /// <summary>Play-area height in pixels (240; fixed — no mode changes it).</summary>
    public static int Height { get; private set; } = 240;

    /// <summary>Horizon row of the play area (unchanged while height stays 240).</summary>
    public const int CENTER_Y = 64;

    /// <summary>Screen-space centre column (160 at 320, 228 at 456).</summary>
    public static int CenterX => Width / 2;

    /// <summary>Largest valid column (319 at 320, 455 at 456).</summary>
    public static int MaxX => Width - 1;

    /// <summary>Largest valid row (239).</summary>
    public static int MaxY => Height - 1;

    /// <summary>
    /// Configure the play-area viewport: (screen.Width, screen.Height - 16).
    /// Width must be even and in [256, 4096] (4095 is the packed-particle
    /// 12-bit x limit); height is fixed at 240.
    /// </summary>
    public static void Configure(int playWidth, int playHeight)
    {
        if (playWidth < 256 || playWidth > 4096 || (playWidth & 1) != 0)
            throw new ArgumentOutOfRangeException(nameof(playWidth), playWidth,
                "Play-area width must be even and in [256, 4096].");
        if (playHeight != 240)
            throw new ArgumentOutOfRangeException(nameof(playHeight), playHeight,
                "Play-area height is fixed at 240 (16-row score bar over a 256-row screen).");
        Width = playWidth;
        Height = playHeight;
    }
}
