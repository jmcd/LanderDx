using Relander.Core.Interfaces;

namespace Relander.Tests;

/// <summary>
/// Shared test doubles used across the test suite.
/// Previously duplicated in DiagnosticTests, GameEngineIntegrationTests, ParticleTests,
/// PlayerOrientationTests, RenderingTests, and ShadingDiagnosticTests.
/// </summary>

/// <summary>
/// Configurable stub for IGameInput.  All buttons default to false; set properties to
/// simulate held keys for the frame under test.
/// </summary>
internal class TestInput : IGameInput
{
    public bool YawLeft { get; set; }
    public bool YawRight { get; set; }
    public bool PitchUp { get; set; }
    public bool PitchDown { get; set; }
    public bool Fire { get; set; }
    public bool Thrust { get; set; }
    public bool Hover { get; set; }
    public bool ToggleMap { get; set; }
    public bool EscapePressed { get; set; }
    public bool AnyKeyPressed { get; set; }
}

/// <summary>
/// Simple IScreen backed by a 320×256 byte framebuffer (16-row score bar + 240-row play area).
/// Provides helpers for inspecting rendered output in tests.
/// </summary>
internal class TestScreen : IScreen
{
    private readonly byte[] _fb = new byte[320 * 256];

    public int Width => 320;
    public int Height => 256;

    public Span<byte> GetFramebuffer() => _fb;

    public void Clear(byte color = 0) => Array.Fill(_fb, color);

    /// <summary>
    /// Read a pixel at play-area coordinates where (0, 0) is the top-left of the play area
    /// (i.e. framebuffer row 16 offset by the 16-pixel score bar).
    /// </summary>
    public byte GetPlayPixel(int x, int y) => _fb[(y + 16) * 320 + x];

    /// <summary>Count non-zero pixels in the entire play area (rows 16-255).</summary>
    public int CountNonZeroInPlayArea()
    {
        int count = 0;
        for (int y = 16; y < 256; y++)
            for (int x = 0; x < 320; x++)
                if (_fb[y * 320 + x] != 0) count++;
        return count;
    }
}
