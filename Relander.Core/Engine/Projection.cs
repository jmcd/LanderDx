namespace Relander.Core.Engine;

/// <summary>
/// Vertex projection from 3D camera-relative coordinates to screen space.
/// Based on ProjectVertexOntoScreen from Lander.arm:7119-7492.
/// screenX = 160 + x/z, screenY = 64 + y/z (perspective divide).
/// </summary>
public static class Projection
{
    public const int SCREEN_CENTER_X = 160;
    public const int SCREEN_CENTER_Y = 64;
    public const int SCREEN_MAX_X = 319;
    public const int SCREEN_MAX_Y = 238;  // Play area: rows 16-255, 0-indexed in buffer is 0-238

    /// <summary>
    /// Project a 3D camera-relative point to screen coordinates.
    /// Returns true if the point is in front of the camera and on screen.
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

        // Perspective divide: screenX = 160 + x/z, screenY = 64 + y/z
        // Use 64-bit intermediate to avoid overflow
        long scaledX = (long)x * 256;  // Scale for sub-pixel precision
        long scaledY = (long)y * 256;

        int px = (int)(scaledX / z) + SCREEN_CENTER_X * 256;
        int py = (int)(scaledY / z) + SCREEN_CENTER_Y * 256;

        screenX = px / 256;
        screenY = py / 256;

        return true;
    }

    /// <summary>
    /// Check if a projected point is within the visible screen bounds.
    /// </summary>
    public static bool IsOnScreen(int screenX, int screenY)
    {
        return (uint)screenX <= SCREEN_MAX_X && (uint)screenY <= SCREEN_MAX_Y;
    }

    /// <summary>
    /// Compute the z-depth buffer index for a given camera-relative z coordinate.
    /// Higher z = closer to camera = lower buffer index (drawn later).
    /// </summary>
    public static int GetDepthIndex(int z, int landscapeZ)
    {
        int offset = landscapeZ - z;
        return (int)((uint)offset >> 24) & 0xFF;
    }
}
