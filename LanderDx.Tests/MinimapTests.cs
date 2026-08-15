using LanderDx.Core.Engine;
using LanderDx.Core.Math;

namespace LanderDx.Tests;

/// <summary>
/// Tests for the minimap orientation: the map must match the 3D view's
/// up/down convention (far = +z at the TOP, like the horizon on screen) and
/// the player dot must wrap with the 256-tile periodic world like the terrain
/// cache does — not pin at the map edges.
/// </summary>
[TestFixture]
public class MinimapTests
{
    private static byte BlinkColour(GameEngine engine) =>
        engine.State.MainLoopCount % 8 < 4 ? VidcColour.Encode(0, 15, 15) : VidcColour.Encode(15, 15, 0);

    [Test]
    public void FullMap_PadRendersAtBottomRows()
    {
        // Pad tiles are world z 0..7; with far (+z) at the top of the map they
        // must appear at screen rows 247..255 (1px per tile, z=0 is the bottom).
        var screen = new TestScreen();
        var engine = new GameEngine(new RandomGenerator(1), screen);
        engine.StartNewGame();
        engine.State.MapMode = 1;
        engine.Update(new TestInput());

        var fb = screen.GetFramebuffer();
        byte padCol = VidcColour.Encode(12, 10, 2);

        // Pad tile (1, 0): z=0 maps to the bottom screen row 255, x=1 to column
        // 33 (map starts at x=32). Sample away from the player dot's 5x5
        // crosshair (the ship sits at pad centre 4,4 -> crosshair at 34..38).
        Assert.That(fb[255 * 320 + 33], Is.EqualTo(padCol),
            "pad tile (1,0) should render at the bottom of the map");
        // The old top position (row 4) must NOT be the pad
        Assert.That(fb[4 * 320 + 36], Is.Not.EqualTo(padCol),
            "pad must not render at the top of the map");
    }

    [Test]
    public void FullMap_PlayerDotWrapsNegativeCoordinates()
    {
        var screen = new TestScreen();
        var engine = new GameEngine(new RandomGenerator(1), screen);
        engine.StartNewGame();
        engine.State.MapMode = 1;

        // One tile west of the origin and two tiles north: the wrapped position
        // is (255, 254); flipped, the dot must be at map x=287, screen y=1.
        engine.State.XPlayer = -1 * FixedPoint.TILE_SIZE;
        engine.State.ZPlayer = -2 * FixedPoint.TILE_SIZE;
        engine.Update(new TestInput());

        var fb = screen.GetFramebuffer();
        Assert.That(fb[1 * 320 + 287], Is.EqualTo(BlinkColour(engine)),
            "player dot should wrap to the wrapped map position, not pin at the edge");
    }

    [Test]
    public void FullMap_PlayerDotSitsOnThePadAtLaunchpad()
    {
        // The ship starts at the pad centre (4,4); the dot must coincide with
        // the pad's map position: x=36, screen y=251 (z flipped).
        var screen = new TestScreen();
        var engine = new GameEngine(new RandomGenerator(1), screen);
        engine.StartNewGame();
        engine.State.MapMode = 1;
        engine.Update(new TestInput());

        var fb = screen.GetFramebuffer();
        Assert.That(fb[251 * 320 + 36], Is.EqualTo(BlinkColour(engine)),
            "player dot should sit on the pad at the bottom of the map");
    }

    [Test]
    public void InsetMap_PlayerDotWrapsAndFlips()
    {
        // Inset: 1px per 4 tiles at x=252..315, y=22..85. At the pad (4,4):
        // px = 4/4 = 1 -> x = 253; flipped pz = 63 - 1 = 62 -> y = 84.
        var screen = new TestScreen();
        var engine = new GameEngine(new RandomGenerator(1), screen);
        engine.StartNewGame();
        engine.State.MapMode = 0;
        engine.Update(new TestInput());

        var fb = screen.GetFramebuffer();
        Assert.That(fb[84 * 320 + 253], Is.EqualTo(BlinkColour(engine)),
            "inset player dot should sit at the bottom of the map near the pad");
    }
}
