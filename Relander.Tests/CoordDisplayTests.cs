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
        // Player-facing labels: X = ground east-west, Y = ground north-south
        // (world Z), Alt = height above terrain (ground altitude minus ship Y).
        // Ship on the pad: Y = LAUNCHPAD_Y, ground = LAUNCHPAD_ALTITUDE, so
        // Alt = the undercarriage height (0.39 tiles -> "0.3").
        Assert.That(CoordDisplay.FormatHud(0x04A00000, -0x00800000,
                FixedPoint.LAUNCHPAD_Y, FixedPoint.LAUNCHPAD_ALTITUDE),
            Is.EqualTo("X 4.6 Y 255.5 Alt 0.3"));

        // Flying 50 tiles above the pad: Alt is positive and grows upward
        Assert.That(CoordDisplay.FormatHud(0x04000000, 0x04000000,
                FixedPoint.LAUNCHPAD_Y - unchecked(50 * FixedPoint.TILE_SIZE),
                FixedPoint.LAUNCHPAD_ALTITUDE),
            Is.EqualTo("X 4.0 Y 4.0 Alt 50.3"));
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

    // ---- Landing status formatters ----

    [Test]
    public void FormatSpeed_TwoDecimalsInTilesPerTick()
    {
        Assert.That(CoordDisplay.FormatSpeed(0), Is.EqualTo("0.00"));
        Assert.That(CoordDisplay.FormatSpeed(0x00200000), Is.EqualTo("0.12"));  // LANDING_SPEED
        Assert.That(CoordDisplay.FormatSpeed(0x01A00000), Is.EqualTo("1.62"));
    }

    [Test]
    public void LandingChecks_MatchTheLandingCode()
    {
        // Same semantics as PlayerController.CheckCollisionAndLanding
        Assert.That(CoordDisplay.IsLandingSpeed(FixedPoint.LANDING_SPEED - 1), Is.True);
        Assert.That(CoordDisplay.IsLandingSpeed(FixedPoint.LANDING_SPEED), Is.False);
        Assert.That(CoordDisplay.IsOverPad(4, 4), Is.True);
        Assert.That(CoordDisplay.IsOverPad(FixedPoint.LAUNCHPAD_SIZE, 4), Is.False);
        Assert.That(CoordDisplay.IsOverPad(4, -1), Is.False);

        Assert.That(CoordDisplay.FormatSpeedLine(FixedPoint.LANDING_SPEED - 1), Is.EqualTo("SPD 0.12 OK"));
        Assert.That(CoordDisplay.FormatSpeedLine(FixedPoint.LANDING_SPEED), Is.EqualTo("SPD 0.12 !"));
        Assert.That(CoordDisplay.FormatPadLine(true), Is.EqualTo("PAD IN"));
        Assert.That(CoordDisplay.FormatPadLine(false), Is.EqualTo("PAD OUT"));
        Assert.That(CoordDisplay.FormatLandCue(true, FixedPoint.LANDING_SPEED - 1), Is.EqualTo("LAND OK"));
        Assert.That(CoordDisplay.FormatLandCue(true, FixedPoint.LANDING_SPEED), Is.EqualTo("LAND -"));
        Assert.That(CoordDisplay.FormatLandCue(false, 0), Is.EqualTo("LAND -"));
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

        // The P display has two regions: the coords text at score-bar row 1
        // (screen y 8..15, x 40..239 — at most 25 chars ending before the
        // lives counter) and the landing panel at the top-left of the play
        // area (border box x 0..103, screen y 20..59). Everything outside
        // both must be byte-identical; inside, content must actually appear.
        static bool InPanel(int x, int y) => x <= 104 && y >= 20 && y <= 60;
        static bool InText(int x, int y) => x >= 40 && x <= 239 && y >= 8 && y <= 15;

        int textPixels = 0, panelPixels = 0;
        for (int y = 0; y < 256; y++)
        {
            int rowStart = y * 320;
            for (int x = 0; x < 320; x++)
            {
                bool same = fbOn[rowStart + x] == fbOff[rowStart + x];
                if (InText(x, y))
                {
                    if (!same) textPixels++;
                }
                else if (InPanel(x, y))
                {
                    if (!same) panelPixels++;
                }
                else
                {
                    Assert.That(same, $"pixel ({x},{y}) differs outside the P-display regions");
                }
            }
        }

        Assert.That(textPixels, Is.GreaterThan(0), "toggling P should draw the coords text");
        Assert.That(panelPixels, Is.GreaterThan(0), "toggling P should draw the landing panel");

        // The panel has a white border frame on its box edges
        byte white = VidcColour.Encode(15, 15, 15);
        Assert.That(fbOn[20 * 320 + 0], Is.EqualTo(white), "panel border left edge");
        Assert.That(fbOn[20 * 320 + 103], Is.EqualTo(white), "panel border right edge");

        // Second toggle returns to the untouched original frame (compare like
        // for like: both engines at their second game tick).
        engineOn.ToggleCoords();
        Assert.That(engineOn.State.ShowCoords, Is.False);
        engineOn.Update(new TestInput());
        engineOff.Update(new TestInput());
        var fbBack = screenOn.GetFramebuffer();
        for (int y = 0; y < 256; y++)
        {
            int rowStart = y * 320;
            for (int x = 0; x < 320; x++)
            {
                Assert.That(fbBack[rowStart + x], Is.EqualTo(fbOff[rowStart + x]),
                    $"pixel ({x},{y}) differs after toggling P back off");
            }
        }
    }
}
