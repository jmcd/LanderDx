using Relander.Core.Math;

namespace Relander.Core.Engine;

/// <summary>
/// Optional view-depth configuration — a deliberate deviation from the original.
///
/// The original draws 11 tile-corner rows (Lander.arm: TILES_Z = 11), spanning
/// projection z from 10 tiles (front) to 20 tiles (back). Setting ExtraDepthTiles
/// to N adds N extra corner rows at the far end of the grid, sampling terrain
/// behind the camera tile (worldZ = zCameraTile + k, k = 1..N). The original
/// rows keep their exact projection z, world z and colours, so
/// ExtraDepthTiles = 0 reproduces the original bit-for-bit.
///
/// The far-band brightness uses the same fix as the original's BigLander variant
/// (Lander.arm big-landscape branch: SUBS R8, R8, #TILES_Z-11 / MOVLT R8, #0):
/// the brightness row counter is the grid row minus the extension, clamped at 0,
/// so the original rows keep their 0-10 brightness ramp and the new far rows
/// use the darkest shade.
/// </summary>
public sealed class ViewConfig
{
    /// <summary>Maximum extra rows: the back edge (20 + N tiles) must stay below
    /// 128 tiles, the projection's "too far away" rejection bound.</summary>
    public const int MAX_EXTRA_DEPTH_TILES = 100;

    /// <summary>Maximum extra tile columns per side (bounds corner-store size and
    /// per-frame triangle cost; the rasterizer clips anything off-screen anyway).</summary>
    public const int MAX_EXTRA_WIDTH_COLS = 16;

    /// <summary>Extra tile-corner rows added at the far end of the grid (0 = original view).</summary>
    public int ExtraDepthTiles { get; }

    /// <summary>Extra tile columns added on each side of the grid (0 = original view).</summary>
    public int ExtraWidthCols { get; }

    public ViewConfig(int extraDepthTiles, int extraWidthCols = 0)
    {
        if (extraDepthTiles < 0 || extraDepthTiles > MAX_EXTRA_DEPTH_TILES)
            throw new ArgumentOutOfRangeException(nameof(extraDepthTiles), extraDepthTiles,
                $"Extra depth rows must be in [0, {MAX_EXTRA_DEPTH_TILES}] " +
                "(back edge 20 + N tiles must stay below the projection's 128-tile rejection bound).");
        if (extraWidthCols < 0 || extraWidthCols > MAX_EXTRA_WIDTH_COLS)
            throw new ArgumentOutOfRangeException(nameof(extraWidthCols), extraWidthCols,
                $"Extra width columns must be in [0, {MAX_EXTRA_WIDTH_COLS}] per side.");
        ExtraDepthTiles = extraDepthTiles;
        ExtraWidthCols = extraWidthCols;
    }

    /// <summary>Number of tile corners per row from back to front (11 + N).</summary>
    public int TilesZ => FixedPoint.TILES_Z + ExtraDepthTiles;

    /// <summary>Number of tile corners per row from left to right (13 + 2M, M per side).</summary>
    public int TilesX => FixedPoint.TILES_X + 2 * ExtraWidthCols;

    /// <summary>Half-width of the visible landscape (6 + M tiles), used by the
    /// particle/rock side culling.</summary>
    public int LandscapeXHalf => FixedPoint.LANDSCAPE_X_HALF + ExtraWidthCols * FixedPoint.TILE_SIZE;

    /// <summary>Projection z of the back row (20 + N tiles).</summary>
    public int LandscapeZ => FixedPoint.LANDSCAPE_Z + ExtraDepthTiles * FixedPoint.TILE_SIZE;

    /// <summary>Depth of the visible landscape (10 + N tiles).</summary>
    public int LandscapeZDepth => FixedPoint.LANDSCAPE_Z_DEPTH + ExtraDepthTiles * FixedPoint.TILE_SIZE;

    /// <summary>Depth of the visible landscape plus one tile (11 + N tiles).</summary>
    public int LandscapeZBeyond => FixedPoint.LANDSCAPE_Z_BEYOND + ExtraDepthTiles * FixedPoint.TILE_SIZE;

    /// <summary>Number of graphics buffers (12 + N).</summary>
    public int GraphicsBufferCount => FixedPoint.GRAPHICS_BUFFER_COUNT + ExtraDepthTiles;

    /// <summary>
    /// Map a grid row counter (0 = new back edge) to the brightness row counter
    /// used by GetTileColour. Original rows keep their 0-10 ramp; the extra far
    /// rows get the darkest shade (0).
    /// </summary>
    public int MapTileCornerRow(int row) => row < ExtraDepthTiles ? 0 : row - ExtraDepthTiles;
}
