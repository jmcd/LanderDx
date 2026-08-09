namespace Relander.Core.Interfaces;

/// <summary>
/// Abstraction for the screen framebuffer output.
/// The Core engine writes pixel data here; the raylib backend uploads it to the GPU.
/// Tests can inspect the framebuffer bytes to verify rendering correctness.
/// </summary>
public interface IScreen
{
    /// <summary>Width of the framebuffer in pixels.</summary>
    int Width { get; }

    /// <summary>Height of the framebuffer in pixels.</summary>
    int Height { get; }

    /// <summary>
    /// Get a span over the raw framebuffer bytes.
    /// Each byte is a palette index (0-255) in the original VIDC colour format.
    /// </summary>
    Span<byte> GetFramebuffer();

    /// <summary>Clear the entire framebuffer to a given palette index (typically 0 = black).</summary>
    void Clear(byte color = 0);
}
