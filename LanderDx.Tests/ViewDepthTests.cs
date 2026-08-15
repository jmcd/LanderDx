using LanderDx.Core.Engine;
using LanderDx.Core.Math;

namespace LanderDx.Tests;

/// <summary>
/// Tests for the optional extended view depth (a deliberate deviation from the
/// original). The default mode (ExtraDepthTiles = 0) must reproduce the original
/// constants and rendering exactly.
/// </summary>
[TestFixture]
public class ViewDepthTests
{
    // ---- (i) Default config identity with the original constants ----

    [Test]
    public void DefaultConfig_MatchesOriginalConstants()
    {
        var config = new ViewConfig(0);

        Assert.Multiple(() =>
        {
            Assert.That(config.ExtraDepthTiles, Is.EqualTo(0));
            Assert.That(config.TilesZ, Is.EqualTo(FixedPoint.TILES_Z));
            Assert.That(config.LandscapeZ, Is.EqualTo(FixedPoint.LANDSCAPE_Z));
            Assert.That(config.LandscapeZDepth, Is.EqualTo(FixedPoint.LANDSCAPE_Z_DEPTH));
            Assert.That(config.LandscapeZBeyond, Is.EqualTo(FixedPoint.LANDSCAPE_Z_BEYOND));
            Assert.That(config.GraphicsBufferCount, Is.EqualTo(FixedPoint.GRAPHICS_BUFFER_COUNT));
        });
    }

    [Test]
    public void ExtendedConfig_DerivesFromOriginalConstants()
    {
        var config = new ViewConfig(10);

        Assert.Multiple(() =>
        {
            Assert.That(config.TilesZ, Is.EqualTo(FixedPoint.TILES_Z + 10));
            Assert.That(config.LandscapeZ, Is.EqualTo(FixedPoint.LANDSCAPE_Z + 10 * FixedPoint.TILE_SIZE));
            Assert.That(config.LandscapeZDepth, Is.EqualTo(FixedPoint.LANDSCAPE_Z_DEPTH + 10 * FixedPoint.TILE_SIZE));
            Assert.That(config.LandscapeZBeyond, Is.EqualTo(FixedPoint.LANDSCAPE_Z_BEYOND + 10 * FixedPoint.TILE_SIZE));
            Assert.That(config.GraphicsBufferCount, Is.EqualTo(FixedPoint.GRAPHICS_BUFFER_COUNT + 10));
        });
    }

    [Test]
    public void Config_RejectsOutOfRangeDepth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewConfig(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewConfig(ViewConfig.MAX_EXTRA_DEPTH_TILES + 1));
    }

    // ---- (ii) Extended buffer arithmetic ----

    [Test]
    public void ExtendedBuffers_BufferIndexSpansExtendedRange()
    {
        var config = new ViewConfig(10);
        var buffers = new GraphicsBuffers(config.GraphicsBufferCount, FixedPoint.BUFFER_SIZE / 4,
            config.LandscapeZ, config.LandscapeZDepth, config.LandscapeZBeyond);

        Assert.Multiple(() =>
        {
            // Back edge (20 + 10 = 30 tiles) -> farthest buffer
            Assert.That(buffers.GetBufferIndex(30 * FixedPoint.TILE_SIZE), Is.EqualTo(1));
            // Far band (extension rows)
            Assert.That(buffers.GetBufferIndex(25 * FixedPoint.TILE_SIZE), Is.EqualTo(6));
            // Original back edge -> buffer 10 + N = 11
            Assert.That(buffers.GetBufferIndex(20 * FixedPoint.TILE_SIZE), Is.EqualTo(11));
            // Ship depth (15 tiles) -> buffer 6 + N = 16
            Assert.That(buffers.GetBufferIndex(15 * FixedPoint.TILE_SIZE), Is.EqualTo(16));
            // Front edge -> clamped to LANDSCAPE_Z_DEPTH (10 + N = 20)
            Assert.That(buffers.GetBufferIndex(10 * FixedPoint.TILE_SIZE), Is.EqualTo(20));
        });
    }

    [Test]
    public void ExtendedBuffers_ShadowBufferIndexSpansExtendedRange()
    {
        var config = new ViewConfig(10);
        var buffers = new GraphicsBuffers(config.GraphicsBufferCount, FixedPoint.BUFFER_SIZE / 4,
            config.LandscapeZ, config.LandscapeZDepth, config.LandscapeZBeyond);

        Assert.Multiple(() =>
        {
            Assert.That(buffers.GetShadowBufferIndex(30 * FixedPoint.TILE_SIZE), Is.EqualTo(0));
            Assert.That(buffers.GetShadowBufferIndex(20 * FixedPoint.TILE_SIZE), Is.EqualTo(10));
            Assert.That(buffers.GetShadowBufferIndex(10 * FixedPoint.TILE_SIZE), Is.EqualTo(20));
        });
    }

    [Test]
    public void DefaultBuffers_UnchangedIndexArithmetic()
    {
        var buffers = new GraphicsBuffers();

        Assert.Multiple(() =>
        {
            Assert.That(buffers.GetBufferIndex(FixedPoint.LANDSCAPE_Z), Is.EqualTo(1));
            Assert.That(buffers.GetBufferIndex(FixedPoint.LANDSCAPE_Z_MID), Is.EqualTo(6));
            Assert.That(buffers.GetBufferIndex(FixedPoint.LANDSCAPE_Z_FRONT), Is.EqualTo(10));
            Assert.That(buffers.GetShadowBufferIndex(FixedPoint.LANDSCAPE_Z), Is.EqualTo(0));
        });
    }

    // ---- (iii) Brightness row remap ----

    [Test]
    public void MapTileCornerRow_ClampsExtensionRowsToDarkestAndKeepsOriginalRamp()
    {
        var config = new ViewConfig(10);

        Assert.Multiple(() =>
        {
            for (int row = 0; row < 10; row++)
                Assert.That(config.MapTileCornerRow(row), Is.EqualTo(0), $"extension row {row}");
            for (int row = 0; row <= 10; row++)
                Assert.That(config.MapTileCornerRow(10 + row), Is.EqualTo(row), $"original row {row}");
        });

        Assert.That(new ViewConfig(0).MapTileCornerRow(5), Is.EqualTo(5));
    }

    // ---- (iv) Integration: extended mode adds far-band pixels, near rows identical ----

    [Test]
    public void ExtendedDepth_RendersMoreFarBandPixels_AndKeepsNearRowsIdentical()
    {
        const int seed = 4242;
        var screenOrig = new TestScreen();
        var engineOrig = new GameEngine(new RandomGenerator(seed), screenOrig);
        engineOrig.StartNewGame();
        engineOrig.Update(new TestInput());

        var screenExt = new TestScreen();
        var engineExt = new GameEngine(new RandomGenerator(seed), screenExt, new ViewConfig(10));
        engineExt.StartNewGame();
        engineExt.Update(new TestInput());

        var fbOrig = screenOrig.GetFramebuffer();
        var fbExt = screenExt.GetFramebuffer();

        // (a) Near rows must be byte-identical. Far-band geometry tops out at
        // relY = SEA_LEVEL (5.3125 tiles) + tallest blueprint vertex (2.55 tiles)
        // at projZ > 20 -> play row < ~165, so rows 170..239 are safe.
        for (int y = 170; y < 240; y++)
        {
            int rowStart = (y + 16) * 320;
            for (int x = 0; x < 320; x++)
            {
                Assert.That(fbExt[rowStart + x], Is.EqualTo(fbOrig[rowStart + x]),
                    $"pixel ({x},{y}) differs between original and +10 view");
            }
        }

        // (b) The far band (above the original back edge) must gain pixels.
        int countOrig = 0, countExt = 0;
        for (int y = 0; y < 170; y++)
        {
            int rowStart = (y + 16) * 320;
            for (int x = 0; x < 320; x++)
            {
                if (fbOrig[rowStart + x] != 0) countOrig++;
                if (fbExt[rowStart + x] != 0) countExt++;
            }
        }
        Assert.That(countExt, Is.GreaterThan(countOrig),
            $"+10 view should add pixels in the far band (original {countOrig}, extended {countExt})");
    }

    // ---- (v) Integration: far-band tiles render at the darkest brightness ----

    /// <summary>
    /// Count pixels equal to <paramref name="expected"/> in the far band scan
    /// region (play rows 80-104, x 190-229 — above the original back edge and
    /// clear of the ship at screen centre).
    /// </summary>
    private static int CountExpectedByteInFarBand(TestScreen screen, byte expected)
    {
        var fb = screen.GetFramebuffer();
        int matches = 0;
        for (int y = 80; y <= 104; y++)
            for (int x = 190; x < 230; x++)
                if (fb[(y + 16) * 320 + x] == expected)
                    matches++;
        return matches;
    }

    [Test]
    public void ExtendedDepth_FarBandTilesUseDarkestBrightness()
    {
        const int seed = 777;
        var screen = new TestScreen();
        var engine = new GameEngine(new RandomGenerator(seed), screen, new ViewConfig(10));
        engine.StartNewGame();

        // Put the camera's far band over the launchpad: the camera is rewritten
        // from the player position every Update (XPlayer + 0, ZPlayer + 5 tiles),
        // so set the player to the pad corner (0.5, 0.5). Camera tile z = 5 puts
        // extension rows 8-9 at worldZ 7..6 — forced-flat pad terrain (altitude
        // LAUNCHPAD_ALTITUDE, zero slope). Pad tiles at brightness 0 render as
        // VIDC (4,4,4); any other brightness or terrain base gives a different
        // byte, so the scan below discriminates the BigLander brightness remap.
        engine.State.XPlayer = 0;
        engine.State.ZPlayer = 0x00800000;

        engine.Update(new TestInput());

        // Expected byte: pad tile at the darkest brightness (brightness row 0).
        engine.Landscape.GetAltitude(4 * FixedPoint.TILE_SIZE, 5 * FixedPoint.TILE_SIZE);
        engine.Landscape.GetAltitude(4 * FixedPoint.TILE_SIZE, 5 * FixedPoint.TILE_SIZE);
        byte expected = (byte)(engine.Landscape.GetTileColour(0) & 0xFF);

        int matches = CountExpectedByteInFarBand(screen, expected);
        Assert.That(matches, Is.GreaterThanOrEqualTo(10),
            $"far-band pad tiles should render at the darkest brightness ({matches} matching pixels)");
    }

    // ---- (vi) ViewConfig.Maximum — the baked-in widescreen view ----

    [Test]
    public void MaximumView_RendersFarBandAtConstruction()
    {
        var screen = new TestScreen();
        var engine = new GameEngine(new RandomGenerator(777), screen, ViewConfig.Maximum);
        engine.StartNewGame();

        Assert.Multiple(() =>
        {
            Assert.That(ViewConfig.Maximum.ExtraDepthTiles, Is.EqualTo(ViewConfig.MAX_EXTRA_DEPTH_TILES));
            Assert.That(ViewConfig.Maximum.ExtraWidthCols, Is.EqualTo(ViewConfig.MAX_EXTRA_WIDTH_COLS));
        });

        // The far band renders over the launchpad (same setup and
        // discriminator as the test above).
        engine.State.XPlayer = 0;
        engine.State.ZPlayer = 0x00800000;
        engine.Update(new TestInput());

        engine.Landscape.GetAltitude(4 * FixedPoint.TILE_SIZE, 5 * FixedPoint.TILE_SIZE);
        engine.Landscape.GetAltitude(4 * FixedPoint.TILE_SIZE, 5 * FixedPoint.TILE_SIZE);
        byte expected = (byte)(engine.Landscape.GetTileColour(0) & 0xFF);

        Assert.That(CountExpectedByteInFarBand(screen, expected), Is.GreaterThanOrEqualTo(10),
            "the maximum view should render the far band without any runtime toggling");
    }

    [Test]
    public void ViewConfig_RejectsOutOfRangeDepth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewConfig(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewConfig(ViewConfig.MAX_EXTRA_DEPTH_TILES + 1));
    }
}
