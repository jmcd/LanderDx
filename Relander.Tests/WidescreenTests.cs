using Relander.Core.Engine;
using Relander.Core.Math;

namespace Relander.Tests;

/// <summary>
/// Tests for the opt-in widescreen mode (456×256 framebuffer, 456×240 play
/// area, --widescreen at startup). The default 320×256 mode must stay
/// byte-identical — covered by the untouched rest of the suite.
/// </summary>
[TestFixture]
public class WidescreenTests
{
    [TearDown]
    public void ResetViewport() => Viewport.Configure(320, 240);

    // ---- (i) Projection follows the viewport width ----

    [Test]
    public void WideViewport_ProjectsAtWideCenter()
    {
        // Constructing the engine with a wide screen configures the viewport.
        var engine = new GameEngine(new RandomGenerator(1), new TestScreen(456, 256));

        Assert.Multiple(() =>
        {
            Projection.Project(0, 0, FixedPoint.LANDSCAPE_Z_MID, out int sx, out int sy);
            Assert.That(sx, Is.EqualTo(228), "world x=0 must project to the wide screen centre");
            Assert.That(sy, Is.EqualTo(64), "the horizon row is unchanged");
            Assert.That(Projection.IsOnScreen(455, 10), Is.True);
            Assert.That(Projection.IsOnScreen(456, 10), Is.False);
        });
    }

    // ---- (ii) The title is centred ----

    [Test]
    public void WideFrame_TitleIsCentred()
    {
        var screen = new TestScreen(456, 256);
        var engine = new GameEngine(new RandomGenerator(1), screen);
        engine.StartNewGame();
        engine.Update(new TestInput());

        var fb = screen.GetFramebuffer();

        // The title is exactly 40 chars = 320 px, so centring gives
        // x = (456 - 320) / 2 = 68; the left margin must be empty.
        for (int x = 4; x <= 63; x++)
        {
            Assert.That(fb[0 * 456 + x], Is.EqualTo((byte)0),
                $"pixel ({x},0) should be empty left margin of the centred title");
        }
        int titlePixels = 0;
        for (int x = 68; x <= 76; x++)
            if (fb[0 * 456 + x] != 0) titlePixels++;
        Assert.That(titlePixels, Is.GreaterThan(0),
            "the title text should start at the centred position x=68");
    }

    // ---- (iii) The wide frame renders beyond x = 319 ----

    [Test]
    public void WideFrame_RendersBeyondOriginalWidth()
    {
        var screen = new TestScreen(456, 256);
        var engine = new GameEngine(new RandomGenerator(1), screen);
        engine.StartNewGame();
        engine.State.FuelLevel = 0x4000;  // >> 4 = 1024 px, capped at the play width
        engine.Update(new TestInput());

        var fb = screen.GetFramebuffer();
        for (int y = 17; y <= 19; y++)
            for (int x = 320; x < 456; x++)
            {
                Assert.That(fb[y * 456 + x], Is.EqualTo((byte)0x37),
                    $"fuel bar pixel ({x},{y}) should span the full wide screen");
            }
    }

    // ---- (iii) HUD anchors follow the width ----

    [Test]
    public void WideFrame_HudAnchorsAtRightEdge()
    {
        var screen = new TestScreen(456, 256);
        var engine = new GameEngine(new RandomGenerator(1), screen);
        engine.StartNewGame();
        engine.Update(new TestInput());

        var fb = screen.GetFramebuffer();

        // Lives ("3") at width-80 = 376, high score ("500") at width-40 = 416
        int livesPixels = 0, highPixels = 0;
        for (int y = 8; y <= 15; y++)
            for (int x = 0; x < 456; x++)
            {
                if (fb[y * 456 + x] == 0) continue;
                if (x >= 376 && x <= 383) livesPixels++;
                if (x >= 416 && x <= 423) highPixels++;
            }
        Assert.That(livesPixels, Is.GreaterThan(0), "lives digits should sit at the right edge");
        Assert.That(highPixels, Is.GreaterThan(0), "high-score digits should sit at the right edge");

        // The old positions (240/280) must be empty gap now (coords display off;
        // the score is at 0..23, the title is row 0, the minimap starts at row 21).
        for (int y = 8; y <= 15; y++)
            for (int x = 240; x <= 300; x++)
            {
                Assert.That(fb[y * 456 + x], Is.EqualTo((byte)0),
                    $"pixel ({x},{y}) should be empty gap between score and right-anchored digits");
            }
    }

    // ---- (iv) Minimap placement follows the width ----

    [Test]
    public void WideFrame_MinimapAnchorsAtRightEdge()
    {
        var screen = new TestScreen(456, 256);
        var engine = new GameEngine(new RandomGenerator(1), screen);
        engine.StartNewGame();
        engine.Update(new TestInput());

        var fb = screen.GetFramebuffer();
        byte borderCol = VidcColour.Encode(15, 15, 15);

        // Inset border starts at startX - 1 = 387 (stride - 68 - 1)
        Assert.That(fb[21 * 456 + 387], Is.EqualTo(borderCol),
            "inset minimap border should sit at the right edge");
        // The old position is now landscape (NOT the border, and not black —
        // row 21 is inside the play area)
        Assert.That(fb[21 * 456 + 251], Is.Not.EqualTo(borderCol),
            "inset minimap must not stay at the original 320 position");
    }

    [Test]
    public void WideFrame_FullMapIsCentered()
    {
        var screen = new TestScreen(456, 256);
        var engine = new GameEngine(new RandomGenerator(1), screen);
        engine.StartNewGame();
        engine.State.MapMode = 1;
        engine.Update(new TestInput());

        var fb = screen.GetFramebuffer();
        byte padCol = VidcColour.Encode(12, 10, 2);

        // Full map starts at (456-256)/2 = 100; pad tiles (0,0) and (1,0) sit
        // at the bottom row 255 (z flipped so z=0 is at the bottom)
        Assert.That(fb[255 * 456 + 100], Is.EqualTo(padCol),
            "full map should be centred in the wide screen");
        Assert.That(fb[255 * 456 + 101], Is.EqualTo(padCol),
            "full map should be centred in the wide screen");
    }
}
