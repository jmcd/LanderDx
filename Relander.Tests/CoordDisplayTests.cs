using Relander.Core.Engine;
using Relander.Core.Math;

namespace Relander.Tests;

/// <summary>
/// Tests for the HUD coordinate display (P key) — an opt-in enhancement with no
/// original counterpart. Default mode (off) must leave the frame untouched.
/// </summary>
[TestFixture]
public class CoordDisplayTests
{
    // ---- Formatting ----

    [Test]
    public void FormatCoord_PositiveValue()
    {
        // 4 + 0xA00000 / 0x01000000 = 4.625 -> "4.6"
        Assert.That(CoordDisplay.FormatCoord(0x04A00000), Is.EqualTo("4.6"));
    }

    [Test]
    public void FormatCoord_NegativeValueWrapsToPeriodicWorld()
    {
        // -0.5 tiles: floor -1 + frac 0.5, wrapped into [0, 256) -> 255.5
        Assert.That(CoordDisplay.FormatCoord(-0x00800000), Is.EqualTo("255.5"));
        // -1.0 tiles exactly -> 255.0
        Assert.That(CoordDisplay.FormatCoord(-0x01000000), Is.EqualTo("255.0"));
    }

    [Test]
    public void FormatCoord_ZeroAndMaxFraction()
    {
        Assert.That(CoordDisplay.FormatCoord(0), Is.EqualTo("0.0"));
        Assert.That(CoordDisplay.FormatCoord(0x00FFFFFF), Is.EqualTo("0.9"));
    }

    [Test]
    public void FormatCoord_WrapsAt256Tiles()
    {
        // 256 tiles is 2^32 in fixed point — already 0 in the 32-bit world
        Assert.That(CoordDisplay.FormatCoord(unchecked(256 * FixedPoint.TILE_SIZE)), Is.EqualTo("0.0"));
        Assert.That(CoordDisplay.FormatCoord(unchecked(257 * FixedPoint.TILE_SIZE) + 0x00400000), Is.EqualTo("1.2"));
    }

    [Test]
    public void FormatHud_AllThreeAxes()
    {
        Assert.That(CoordDisplay.FormatHud(0x04A00000, 0x02800000, -0x00800000),
            Is.EqualTo("X 4.6 Y 2.5 Z 255.5"));
    }

    [Test]
    public void FormatAltitude_IsUnwrapped()
    {
        // Positive is down toward terrain, negative is up
        Assert.That(CoordDisplay.FormatAltitude(0x02800000), Is.EqualTo("2.5"));
        // Negative values read as their true magnitude, not the floor+frac form
        Assert.That(CoordDisplay.FormatAltitude(-0x00800000), Is.EqualTo("-0.5"));
        Assert.That(CoordDisplay.FormatAltitude(-0x0CC00000), Is.EqualTo("-12.7"));
        // Altitude is not periodic: a negative value reads negative (the
        // wrapped form would be "156.0" for -100 tiles)
        Assert.That(CoordDisplay.FormatAltitude(-100 * FixedPoint.TILE_SIZE), Is.EqualTo("-100.0"));
    }

    // ---- Integration: toggle changes only the HUD text region ----

    [Test]
    public void ToggleCoords_ChangesOnlyTheTextRegion()
    {
        const int seed = 777;
        var screenOff = new TestScreen();
        var engineOff = new GameEngine(new RandomGenerator(seed), screenOff);
        engineOff.StartNewGame();
        engineOff.Update(new TestInput());

        var screenOn = new TestScreen();
        var engineOn = new GameEngine(new RandomGenerator(seed), screenOn);
        engineOn.StartNewGame();
        engineOn.ToggleCoords();
        Assert.That(engineOn.State.ShowCoords, Is.True);
        engineOn.Update(new TestInput());

        var fbOff = screenOff.GetFramebuffer();
        var fbOn = screenOn.GetFramebuffer();

        // The coords draw at score-bar row 1 (screen y 8..15), x = 48, and the
        // three-axis text is at most 24 chars (192 px, ending just before the
        // lives counter at x=240). Everything outside the box must be
        // byte-identical; inside, the text must actually appear.
        int differingInside = 0;
        for (int y = 0; y < 256; y++)
        {
            int rowStart = y * 320;
            for (int x = 0; x < 320; x++)
            {
                bool inside = x >= 40 && x <= 239 && y >= 8 && y <= 15;
                bool same = fbOn[rowStart + x] == fbOff[rowStart + x];
                if (inside)
                {
                    if (!same) differingInside++;
                }
                else
                {
                    Assert.That(same, $"pixel ({x},{y}) differs outside the coords text region");
                }
            }
        }

        Assert.That(differingInside, Is.GreaterThan(0),
            "toggling the coords display should draw visible text in the HUD");

        // Second toggle returns to the untouched original frame (compare like
        // for like: both engines at their second game tick).
        engineOn.ToggleCoords();
        Assert.That(engineOn.State.ShowCoords, Is.False);
        engineOn.Update(new TestInput());
        engineOff.Update(new TestInput());
        var fbBack = screenOn.GetFramebuffer();
        for (int y = 8; y <= 15; y++)
        {
            int rowStart = y * 320;
            for (int x = 0; x < 320; x++)
            {
                Assert.That(fbBack[rowStart + x], Is.EqualTo(fbOff[rowStart + x]),
                    $"pixel ({x},{y}) differs after toggling coords back off");
            }
        }
    }
}
