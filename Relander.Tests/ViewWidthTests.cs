using Relander.Core.Engine;
using Relander.Core.Math;

namespace Relander.Tests;

/// <summary>
/// Tests for the optional extended view width (a deliberate deviation from the
/// original). The default mode (ExtraWidthCols = 0) must reproduce the original
/// constants and rendering exactly.
/// </summary>
[TestFixture]
public class ViewWidthTests
{
    // ---- Config derivation ----

    [Test]
    public void DefaultConfig_WidthMatchesOriginalConstants()
    {
        var config = new ViewConfig(0);

        Assert.Multiple(() =>
        {
            Assert.That(config.ExtraWidthCols, Is.EqualTo(0));
            Assert.That(config.TilesX, Is.EqualTo(FixedPoint.TILES_X));
            Assert.That(config.LandscapeXHalf, Is.EqualTo(FixedPoint.LANDSCAPE_X_HALF));
        });
    }

    [Test]
    public void WidthConfig_DerivesFromOriginalConstants()
    {
        var config = new ViewConfig(0, 3);

        Assert.Multiple(() =>
        {
            Assert.That(config.TilesX, Is.EqualTo(FixedPoint.TILES_X + 6));
            Assert.That(config.LandscapeXHalf, Is.EqualTo(FixedPoint.LANDSCAPE_X_HALF + 3 * FixedPoint.TILE_SIZE));
        });
    }

    [Test]
    public void CombinedConfig_ExtendsDepthAndWidthIndependently()
    {
        var config = new ViewConfig(10, 3);

        Assert.Multiple(() =>
        {
            Assert.That(config.TilesZ, Is.EqualTo(FixedPoint.TILES_Z + 10));
            Assert.That(config.TilesX, Is.EqualTo(FixedPoint.TILES_X + 6));
            Assert.That(config.LandscapeZ, Is.EqualTo(FixedPoint.LANDSCAPE_Z + 10 * FixedPoint.TILE_SIZE));
            Assert.That(config.LandscapeXHalf, Is.EqualTo(FixedPoint.LANDSCAPE_X_HALF + 3 * FixedPoint.TILE_SIZE));
        });
    }

    [Test]
    public void Config_RejectsOutOfRangeWidth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewConfig(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ViewConfig(0, ViewConfig.MAX_EXTRA_WIDTH_COLS + 1));
    }

    // ---- Integration: extended mode adds side-band pixels, original region identical ----

    [Test]
    public void ExtendedWidth_RendersMoreSideBandPixels_AndKeepsOriginalRegionIdentical()
    {
        const int seed = 31337;
        var screenOrig = new TestScreen();
        var engineOrig = new GameEngine(new RandomGenerator(seed), screenOrig);
        engineOrig.StartNewGame();
        engineOrig.Update(new TestInput());

        var screenExt = new TestScreen();
        var engineExt = new GameEngine(new RandomGenerator(seed), screenExt, new ViewConfig(0, 3));
        engineExt.StartNewGame();
        engineExt.Update(new TestInput());

        var fbOrig = screenOrig.GetFramebuffer();
        var fbExt = screenExt.GetFramebuffer();

        // (a) The original columns are bit-identical. Extended-only geometry for
        // +3 columns per side spans every projection row (the near-row tiles
        // reach the bottom of the screen), but stays within these x bounds:
        //   left  intrusions end   at x <= ~121.6 (objects anchored at
        //         worldX <= -3 with 0.9-tile extent, at projZ = 20),
        //   right intrusions start at x >= ~243.2 (first extended terrain tile
        //         corners worldX 6.5..7.5 at projZ = 20; nearer rows project
        //         further right, off-screen).
        // So the vertical strip x 128..240 must be untouched at every row.
        for (int y = 0; y < 240; y++)
        {
            int rowStart = (y + 16) * 320;
            for (int x = 128; x <= 240; x++)
            {
                Assert.That(fbExt[rowStart + x], Is.EqualTo(fbOrig[rowStart + x]),
                    $"pixel ({x},{y}) differs between original and +3-per-side view");
            }
        }

        // (b) The side bands must gain pixels.
        int countOrig = 0, countExt = 0;
        for (int y = 0; y < 240; y++)
        {
            int rowStart = (y + 16) * 320;
            for (int x = 0; x < 320; x++)
            {
                if (x >= 128 && x <= 240) continue;  // identity strip only
                if (fbOrig[rowStart + x] != 0) countOrig++;
                if (fbExt[rowStart + x] != 0) countExt++;
            }
        }
        Assert.That(countExt, Is.GreaterThan(countOrig),
            $"+3-per-side view should add pixels in the side bands (original {countOrig}, extended {countExt})");
    }

    // ---- Left-edge clipping ----

    [Test]
    public void ExtendedWidth_TilesCrossingTheLeftEdgeAreDrawn()
    {
        // With +4 columns per side the front row corners project to x ≈ -186
        // at the left; the original clips such tiles at the screen edge (the
        // missing-corner sentinel is 0x80000000, not a negative x). The port
        // once skipped any tile with a negative-x corner, leaving a hole at
        // the left edge — this region must be filled with landscape.
        var screen = new TestScreen();
        var engine = new GameEngine(new RandomGenerator(4242), screen, new ViewConfig(0, 4));
        engine.StartNewGame();
        engine.Update(new TestInput());

        var fb = screen.GetFramebuffer();
        int filled = 0;
        for (int y = 190; y <= 235; y++)
            for (int x = 0; x <= 18; x++)
                if (fb[(y + 16) * 320 + x] != 0) filled++;

        Assert.That(filled, Is.GreaterThan(0),
            "tiles whose corners cross the left screen edge must be drawn and clipped, not skipped");
    }

    // ---- Baked-in view config (no runtime toggles) ----

    [Test]
    public void CtorConfig_SetsParticleCullingAndCornerStores()
    {
        // The view config is baked in at construction: the particle side-culling
        // bound and the corner stores must follow it without any runtime setter.
        var engine = new GameEngine(new RandomGenerator(1), new TestScreen(), new ViewConfig(10, 3));

        Assert.That(engine.Particles.LandscapeXHalf,
            Is.EqualTo(FixedPoint.LANDSCAPE_X_HALF + 3 * FixedPoint.TILE_SIZE),
            "particle side-culling bound must follow the baked-in width");
    }

    [Test]
    public void MaximumView_RendersAFrame()
    {
        var screen = new TestScreen();
        var engine = new GameEngine(new RandomGenerator(4242), screen, ViewConfig.Maximum);
        engine.StartNewGame();

        Assert.Multiple(() =>
        {
            Assert.That(ViewConfig.Maximum.ExtraDepthTiles, Is.EqualTo(ViewConfig.MAX_EXTRA_DEPTH_TILES));
            Assert.That(ViewConfig.Maximum.ExtraWidthCols, Is.EqualTo(ViewConfig.MAX_EXTRA_WIDTH_COLS));
        });

        engine.Update(new TestInput());  // must not throw; buffers must not overflow (Debug.Assert)

        Assert.That(screen.CountNonZeroInPlayArea(), Is.GreaterThan(0));
    }

    [Test]
    public void DefaultEngine_UsesOriginalView()
    {
        // null config = the original 13x11-corner grid with original culling.
        var engine = new GameEngine(new RandomGenerator(1), new TestScreen());
        Assert.That(engine.Particles.LandscapeXHalf, Is.EqualTo(FixedPoint.LANDSCAPE_X_HALF));
    }
}
