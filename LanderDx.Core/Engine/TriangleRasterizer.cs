namespace LanderDx.Core.Engine;

/// <summary>
/// Flat-shaded triangle rasterizer drawing into a byte[] framebuffer.
/// Based on DrawTriangle from Lander.arm:9278-11502.
/// The framebuffer is playWidth × playHeight bytes (play area; 320×240 in the
/// original), with each byte being a palette index.
/// </summary>
public class TriangleRasterizer
{
    private readonly byte[] _framebuffer;
    private readonly int _width;
    private readonly int _height;

    public TriangleRasterizer(byte[] framebuffer, int width = 320, int height = 240)
    {
        _framebuffer = framebuffer;
        _width = width;
        _height = height;
    }

    /// <summary>Clear the framebuffer to a given colour.</summary>
    public void Clear(byte color = 0)
    {
        Array.Fill(_framebuffer, color);
    }

    /// <summary>
    /// Draw a filled triangle with flat shading.
    /// Clips to screen bounds.
    /// </summary>
    public void DrawTriangle(int x1, int y1, int x2, int y2, int x3, int y3, byte color)
    {
        // Sort vertices by y ascending (y1 <= y2 <= y3)
        if (y1 > y2) { (x1, x2) = (x2, x1); (y1, y2) = (y2, y1); }
        if (y2 > y3) { (x2, x3) = (x3, x2); (y2, y3) = (y3, y2); }
        if (y1 > y2) { (x1, x2) = (x2, x1); (y1, y2) = (y2, y1); }

        // Clip Y to screen bounds
        if (y3 < 0 || y1 >= _height) return;
        int yStart = global::System.Math.Max(y1, 0);
        int yEnd = global::System.Math.Min(y3, _height - 1);

        // Compute slopes: dx/dy for the two long edges
        // Edge 1: (x1,y1) to (x3,y3) — the "long" edge
        float dx13 = (y3 != y1) ? (float)(x3 - x1) / (y3 - y1) : 0;

        if (y2 == y3)
        {
            // Flat-bottomed triangle
            float dx12 = (y2 != y1) ? (float)(x2 - x1) / (y2 - y1) : 0;
            float left = x1, right = x1; // Will be ordered per scanline
            for (int y = yStart; y <= yEnd; y++)
            {
                int rowY = y - y1;
                int xL = (int)(x1 + dx12 * rowY);
                int xR = (int)(x1 + dx13 * rowY);
                if (xL > xR) (xL, xR) = (xR, xL);
                DrawScanline(y, xL, xR, color);
            }
        }
        else if (y1 == y2)
        {
            // Flat-topped triangle
            float dx23 = (y3 != y2) ? (float)(x3 - x2) / (y3 - y2) : 0;
            for (int y = yStart; y <= yEnd; y++)
            {
                int rowY = y - y1;
                int xL = (int)(x1 + dx13 * rowY);
                int xR = (int)(x2 + dx23 * (y - y2));
                if (xL > xR) (xL, xR) = (xR, xL);
                DrawScanline(y, xL, xR, color);
            }
        }
        else
        {
            // General triangle: split at y2
            float dx12 = (y2 != y1) ? (float)(x2 - x1) / (y2 - y1) : 0;
            float dx23 = (y3 != y2) ? (float)(x3 - x2) / (y3 - y2) : 0;

            // Top half: (x1,y1) to (x2,y2)
            int yTopEnd = global::System.Math.Min(y2, _height - 1);
            for (int y = yStart; y <= yTopEnd; y++)
            {
                int rowY = y - y1;
                int xL = (int)(x1 + dx13 * rowY);
                int xR = (int)(x1 + dx12 * rowY);
                if (xL > xR) (xL, xR) = (xR, xL);
                DrawScanline(y, xL, xR, color);
            }

            // Bottom half: (x2,y2) to (x3,y3)
            for (int y = global::System.Math.Max(y2 + 1, 0); y <= yEnd; y++)
            {
                int xL = (int)(x1 + dx13 * (y - y1));
                int xR = (int)(x2 + dx23 * (y - y2));
                if (xL > xR) (xL, xR) = (xR, xL);
                DrawScanline(y, xL, xR, color);
            }
        }
    }

    private void DrawScanline(int y, int xLeft, int xRight, byte color)
    {
        if (y < 0 || y >= _height) return;

        int xStart = global::System.Math.Max(xLeft, 0);
        int xEnd = global::System.Math.Min(xRight, _width - 1);
        if (xStart > xEnd) return;

        int offset = y * _width + xStart;
        int length = xEnd - xStart + 1;
        _framebuffer.AsSpan(offset, length).Fill(color);
    }
}
