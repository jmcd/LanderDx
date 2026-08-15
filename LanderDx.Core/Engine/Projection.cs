namespace LanderDx.Core.Engine;

/// <summary>
/// Vertex projection from 3D camera-relative coordinates to screen space.
/// Based on ProjectVertexOntoScreen from Lander.arm:7119-7492.
/// screenX = centerX + x/z, screenY = 64 + y/z (perspective divide), where
/// centerX follows the Viewport (160 at the original 320 width, 228 at 456).
/// </summary>
public static class Projection
{
    /// <summary>
    /// Project a 3D camera-relative point to screen coordinates.
    /// Returns true if the point is in front of the camera and on screen.
    ///
    /// Uses the same math as the original: the 10-bit ratio from shift-and-subtract
    /// division is (x/z) * 1024, and pixel offset = ratio >> 2 = (x/z) * 256.
    /// So: screenX = centerX + x * 256 / z, screenY = 64 + y * 256 / z
    /// </summary>
    public static bool Project(int x, int y, int z, out int screenX, out int screenY)
    {
        screenX = 0;
        screenY = 0;

        // Check if behind camera (z >= 0x80000000 in unsigned interpretation)
        if ((uint)z >= 0x80000000)
            return false;

        // Ensure z is positive
        if (z <= 0)
            return false;

        // Pixel offset from center: (x/z) * 256 pixels directly
        // Use 64-bit intermediate to avoid overflow
        int offsetX = (int)((long)x * 256 / z);
        int offsetY = (int)((long)y * 256 / z);

        screenX = Viewport.CenterX + offsetX;
        screenY = Viewport.CENTER_Y + offsetY;

        return true;
    }

    /// <summary>
    /// Check if a projected point is within the visible screen bounds.
    /// </summary>
    public static bool IsOnScreen(int screenX, int screenY)
    {
        return (uint)screenX <= Viewport.MaxX && (uint)screenY <= Viewport.MaxY;
    }

}
