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

    // ---- Runtime toggle API ----

    [Test]
    public void CycleViewWidth_TogglesPresets()
    {
        var engine = new GameEngine(new RandomGenerator(1), new TestScreen());

        Assert.That(engine.ExtraWidthCols, Is.EqualTo(0));

        engine.CycleViewWidth();
        Assert.That(engine.ExtraWidthCols, Is.EqualTo(2));
        engine.CycleViewWidth();
        Assert.That(engine.ExtraWidthCols, Is.EqualTo(4));
        engine.CycleViewWidth();
        Assert.That(engine.ExtraWidthCols, Is.EqualTo(6));
        engine.CycleViewWidth();
        Assert.That(engine.ExtraWidthCols, Is.EqualTo(0));
    }

    [Test]
    public void SetExtraWidth_KeepsDepthAndUpdatesParticleCulling()
    {
        var engine = new GameEngine(new RandomGenerator(1), new TestScreen());
        engine.SetExtraDepth(10);

        engine.SetExtraWidth(3);
        Assert.That(engine.ExtraWidthCols, Is.EqualTo(3));
        Assert.That(engine.ExtraDepthTiles, Is.EqualTo(10), "width toggle must not disturb depth");
        Assert.That(engine.Particles.LandscapeXHalf,
            Is.EqualTo(FixedPoint.LANDSCAPE_X_HALF + 3 * FixedPoint.TILE_SIZE),
            "particle side-culling bound must follow the extended width");

        // And a depth toggle keeps the width.
        engine.SetExtraDepth(0);
        Assert.That(engine.ExtraWidthCols, Is.EqualTo(3), "depth toggle must not disturb width");
        Assert.That(engine.Particles.LandscapeXHalf,
            Is.EqualTo(FixedPoint.LANDSCAPE_X_HALF + 3 * FixedPoint.TILE_SIZE));
    }

    [Test]
    public void CombinedDepthAndWidth_RendersAFrame()
    {
        var screen = new TestScreen();
        var engine = new GameEngine(new RandomGenerator(4242), screen);
        engine.StartNewGame();

        engine.SetExtraDepth(10);
        engine.SetExtraWidth(2);
        Assert.That(engine.ExtraDepthTiles, Is.EqualTo(10));
        Assert.That(engine.ExtraWidthCols, Is.EqualTo(2));

        engine.Update(new TestInput());  // must not throw; buffers must not overflow (Debug.Assert)

        Assert.That(screen.CountNonZeroInPlayArea(), Is.GreaterThan(0));
    }

    [Test]
    public void SetExtraWidth_RejectsOutOfRange()
    {
        var engine = new GameEngine(new RandomGenerator(1), new TestScreen());
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.SetExtraWidth(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.SetExtraWidth(ViewConfig.MAX_EXTRA_WIDTH_COLS + 1));
    }
}
