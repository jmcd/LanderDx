using Relander.Core.Data;
using Relander.Core.Math;

namespace Relander.Core.Engine;

/// <summary>
/// Renders a 2D radar mini-map overlay (1px per 4 tiles inset or 1px per tile full screen).
/// Shows terrain heights, sea level, launchpad, 3D objects, falling rocks, and player location.
/// Inspired by Zarch / Virus by David Braben.
/// </summary>
public static class MinimapRenderer
{
    private static readonly byte[] _mapCache = new byte[256 * 256];
    private static bool _cacheBuilt = false;

    /// <summary>Pre-renders static terrain, sea, launchpad, and object map tiles into cache.</summary>
    public static void PrecacheMap(LandscapeGenerator landscape, ObjectMap objectMap)
    {
        for (int tz = 0; tz < 256; tz++)
        {
            for (int tx = 0; tx < 256; tx++)
            {
                int worldX = tx * FixedPoint.TILE_SIZE;
                int worldZ = tz * FixedPoint.TILE_SIZE;

                byte color;
                // 1. Launchpad (tiles 0..7, 0..7)
                if (tx <= 7 && tz <= 7)
                {
                    color = VidcColour.Encode(15, 12, 0); // Yellow/Gold
                }
                else
                {
                    int objType = objectMap.GetObjectAt(worldX, worldZ);
                    if (ObjectTypes.IsLiveObject(objType))
                    {
                        color = VidcColour.Encode(15, 15, 15); // Bright white object
                    }
                    else
                    {
                        int alt = landscape.GetAltitude(worldX, worldZ);
                        if (alt >= FixedPoint.SEA_LEVEL)
                        {
                            color = VidcColour.Encode(0, 2, 8); // Dark blue sea
                        }
                        else
                        {
                            // Green gradient based on altitude (-alt, higher altitude = lighter green)
                            int heightIndex = global::System.Math.Clamp((-alt >> 23), 2, 14);
                            color = VidcColour.Encode(0, heightIndex, 0);
                        }
                    }
                }
                _mapCache[tz * 256 + tx] = color;
            }
        }
        _cacheBuilt = true;
    }

    /// <summary>Invalidate map cache when objects are destroyed.</summary>
    public static void InvalidateCache() => _cacheBuilt = false;

    /// <summary>Render minimap onto screen framebuffer according to state.MapMode.</summary>
    public static void Render(Span<byte> screenBuf, int stride, GameState state,
        LandscapeGenerator landscape, ObjectMap objectMap, ParticleSystem particles)
    {
        if (state.MapMode == 2) return; // Mode 2: Hidden

        if (!_cacheBuilt)
        {
            PrecacheMap(landscape, objectMap);
        }

        if (state.MapMode == 0)
        {
            // Mode 0: Inset Mini-Map (64x64 pixels in top-right corner, x=252..315, y=16..79)
            RenderInsetMap(screenBuf, stride, state);
        }
        else if (state.MapMode == 1)
        {
            // Mode 1: Full 256x256 Overlay (1px per tile, centered at x=32..287, y=0..255)
            RenderFullMap(screenBuf, stride, state);
        }
    }

    private static void RenderInsetMap(Span<byte> screenBuf, int stride, GameState state)
    {
        int startX = 252;
        int startY = 16;
        int size = 64;

        byte borderCol = VidcColour.Encode(15, 15, 15);

        // Draw 66x66 border frame around inset map
        for (int y = -1; y <= size; y++)
        {
            int screenY = startY + y;
            if (screenY < 0 || screenY >= 256) continue;
            int rowOffset = screenY * stride;

            for (int x = -1; x <= size; x++)
            {
                int screenX = startX + x;
                if (screenX < 0 || screenX >= 320) continue;

                if (x == -1 || x == size || y == -1 || y == size)
                {
                    screenBuf[rowOffset + screenX] = borderCol;
                }
            }
        }

        // Downsample 256x256 map cache to 64x64 (1px per 4x4 tiles)
        for (int my = 0; my < size; my++)
        {
            int rowOffset = (startY + my) * stride;
            int tz = (my * 4) & 0xFF;

            for (int mx = 0; mx < size; mx++)
            {
                int tx = (mx * 4) & 0xFF;
                byte color = _mapCache[tz * 256 + tx];
                screenBuf[rowOffset + startX + mx] = color;
            }
        }

        // Overlay Player Ship position (blinking cyan crosshair)
        int px = global::System.Math.Clamp((state.XPlayer >> 24) / 4, 0, 63);
        int pz = global::System.Math.Clamp((state.ZPlayer >> 24) / 4, 0, 63);
        byte playerCol = (state.MainLoopCount % 8 < 4) ? VidcColour.Encode(0, 15, 15) : VidcColour.Encode(15, 15, 0);

        DrawDot(screenBuf, stride, startX + px, startY + pz, playerCol);
        DrawDot(screenBuf, stride, startX + px - 1, startY + pz, playerCol);
        DrawDot(screenBuf, stride, startX + px + 1, startY + pz, playerCol);
        DrawDot(screenBuf, stride, startX + px, startY + pz - 1, playerCol);
        DrawDot(screenBuf, stride, startX + px, startY + pz + 1, playerCol);
    }

    private static void RenderFullMap(Span<byte> screenBuf, int stride, GameState state)
    {
        int startX = 32; // Center 256px wide map in 320px screen width

        // Draw 1px per tile directly from 256x256 cache
        for (int tz = 0; tz < 256; tz++)
        {
            int rowOffset = tz * stride;
            int cacheOffset = tz * 256;

            for (int tx = 0; tx < 256; tx++)
            {
                screenBuf[rowOffset + startX + tx] = _mapCache[cacheOffset + tx];
            }
        }

        // Overlay Player Ship position (blinking cyan crosshair 1px per tile)
        int px = global::System.Math.Clamp(state.XPlayer >> 24, 0, 255);
        int pz = global::System.Math.Clamp(state.ZPlayer >> 24, 0, 255);
        byte playerCol = (state.MainLoopCount % 8 < 4) ? VidcColour.Encode(0, 15, 15) : VidcColour.Encode(15, 15, 0);

        for (int dx = -2; dx <= 2; dx++)
        {
            DrawDot(screenBuf, stride, startX + px + dx, pz, playerCol);
            DrawDot(screenBuf, stride, startX + px, pz + dx, playerCol);
        }
    }

    private static void DrawDot(Span<byte> screenBuf, int stride, int x, int y, byte color)
    {
        if ((uint)x < 320 && (uint)y < 256)
        {
            screenBuf[y * stride + x] = color;
        }
    }
}
