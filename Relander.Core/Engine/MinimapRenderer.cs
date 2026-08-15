using Relander.Core.Data;
using Relander.Core.Math;

namespace Relander.Core.Engine;

/// <summary>
/// Renders a 2D radar mini-map overlay (1px per 4 tiles inset or 1px per tile full screen).
/// Shows terrain heights, sea level, launchpad, 3D objects, falling rocks, and player location.
/// Inspired by Zarch / Virus by David Braben.
///
/// Orientation matches the 3D view: +x to the right, and +z (far, the horizon
/// direction on screen) at the TOP of the map. Coordinates wrap with the
/// 256-tile periodic world, like the terrain cache and the coordinate display.
/// </summary>
public static class MinimapRenderer
{
    private static readonly byte[] _mapCache = new byte[256 * 256];
    private static bool _cacheBuilt = false;

    /// <summary>Pre-renders terrain, sea, launchpad, and object map tiles using exact rendered 3D tile colors.</summary>
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
                    color = VidcColour.Encode(12, 10, 2); // Exact Launchpad grey/gold
                }
                else
                {
                    int objType = objectMap.GetObjectAt(worldX, worldZ);
                    if (ObjectTypes.IsLiveObject(objType))
                    {
                        var bp = ObjectTypes.GetBlueprint(objType);
                        if (bp != null && bp.Faces.Length > 0)
                        {
                            // Match primary blueprint face VIDC color
                            int faceCol = bp.Faces[0].Colour;
                            int r = (faceCol >> 8) & 0xF;
                            int g = (faceCol >> 4) & 0xF;
                            int b = faceCol & 0xF;
                            color = VidcColour.Encode(r, g, b);
                        }
                        else
                        {
                            color = VidcColour.Encode(15, 15, 15);
                        }
                    }
                    else
                    {
                        int alt = landscape.GetAltitude(worldX, worldZ);
                        if (alt >= FixedPoint.SEA_LEVEL)
                        {
                            color = VidcColour.Encode(0, 0, 4); // Exact Sea level VIDC blue (Lander.arm:1696)
                        }
                        else
                        {
                            // Exact landscape green tile color formula (Lander.arm:1545-1710)
                            int g = ((alt >> 3) & 1) * 4 + 4;
                            int r = alt & 4;  // Red = altitude bit 2 (Lander.arm:1608)
                            int b = 0;
                            int brightness = 4; // average mid-distance brightness
                            r = global::System.Math.Min(r + brightness, 15);
                            g = global::System.Math.Min(g + brightness, 15);
                            color = VidcColour.Encode(r, g, b);
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
            // Mode 0: Inset Mini-Map (64x64 pixels in the top-right corner, anchored to the screen width)
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
        int startX = stride - 68;  // 252 at the original 320 width, 388 at 456
        int startY = 22; // 6-pixel gap below top HUD score bar text (top border at y=21)
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
                if (screenX < 0 || screenX >= stride) continue;

                if (x == -1 || x == size || y == -1 || y == size)
                {
                    screenBuf[rowOffset + screenX] = borderCol;
                }
            }
        }

        // Downsample 256x256 map cache to 64x64 (1px per 4x4 tiles).
        // Flip z: far (+z) at the top, matching the 3D view's horizon.
        for (int my = 0; my < size; my++)
        {
            int rowOffset = (startY + my) * stride;
            int tz = (255 - my * 4) & 0xFF;

            for (int mx = 0; mx < size; mx++)
            {
                int tx = (mx * 4) & 0xFF;
                byte color = _mapCache[tz * 256 + tx];
                screenBuf[rowOffset + startX + mx] = color;
            }
        }

        // Overlay Player Ship position (blinking cyan crosshair), wrapped with
        // the periodic world like the terrain — no pinning at the edges — and
        // z-flipped to match the map.
        int px = ((state.XPlayer >> 24) & 0xFF) / 4;
        int pz = 63 - (((state.ZPlayer >> 24) & 0xFF) / 4);
        byte playerCol = (state.MainLoopCount % 8 < 4) ? VidcColour.Encode(0, 15, 15) : VidcColour.Encode(15, 15, 0);

        DrawDot(screenBuf, stride, startX + px, startY + pz, playerCol);
        DrawDot(screenBuf, stride, startX + px - 1, startY + pz, playerCol);
        DrawDot(screenBuf, stride, startX + px + 1, startY + pz, playerCol);
        DrawDot(screenBuf, stride, startX + px, startY + pz - 1, playerCol);
        DrawDot(screenBuf, stride, startX + px, startY + pz + 1, playerCol);
    }

    private static void RenderFullMap(Span<byte> screenBuf, int stride, GameState state)
    {
        int startX = (stride - 256) / 2; // Center the 256px wide map in the screen width (32 at 320, 100 at 456)

        // Draw 1px per tile directly from 256x256 cache.
        // Flip z: far (+z) at the top, matching the 3D view's horizon.
        for (int tz = 0; tz < 256; tz++)
        {
            int rowOffset = (255 - tz) * stride;
            int cacheOffset = tz * 256;

            for (int tx = 0; tx < 256; tx++)
            {
                screenBuf[rowOffset + startX + tx] = _mapCache[cacheOffset + tx];
            }
        }

        // Overlay Player Ship position (blinking cyan crosshair 1px per tile),
        // wrapped with the periodic world and z-flipped to match the map.
        int px = (state.XPlayer >> 24) & 0xFF;
        int pz = 255 - ((state.ZPlayer >> 24) & 0xFF);
        byte playerCol = (state.MainLoopCount % 8 < 4) ? VidcColour.Encode(0, 15, 15) : VidcColour.Encode(15, 15, 0);

        for (int dx = -2; dx <= 2; dx++)
        {
            DrawDot(screenBuf, stride, startX + px + dx, pz, playerCol);
            DrawDot(screenBuf, stride, startX + px, pz + dx, playerCol);
        }
    }

    private static void DrawDot(Span<byte> screenBuf, int stride, int x, int y, byte color)
    {
        if ((uint)x < stride && (uint)y < 256)
        {
            screenBuf[y * stride + x] = color;
        }
    }
}
