using Relander.Core.Interfaces;

namespace Relander;

/// <summary>
/// Implements IScreen with a byte[] framebuffer sized to the constructor
/// arguments (default 320×256 — the original Mode 13 screen; the --widescreen
/// frontend passes 456×256). Each byte is a palette index (0-255) in the
/// original VIDC colour format.
/// </summary>
public class RaylibScreen : IScreen
{
    private readonly byte[] _framebuffer;

    public int Width { get; }
    public int Height { get; }

    public RaylibScreen(int width = 320, int height = 256)
    {
        Width = width;
        Height = height;
        _framebuffer = new byte[width * height];
    }

    public Span<byte> GetFramebuffer() => _framebuffer;

    public void Clear(byte color = 0)
    {
        Array.Fill(_framebuffer, color);
    }

    /// <summary>
    /// Get a read-only span over the framebuffer for texture upload.
    /// </summary>
    public ReadOnlySpan<byte> Framebuffer => _framebuffer;
}
