using Relander.Core.Data;
using Relander.Core.Engine;
using Relander.Core.Interfaces;
using Relander.Core.Math;

namespace Relander.Tests;

/// <summary>
/// Integration tests: wire up a real GameEngine with simulated input
/// and check the framebuffer for visible output.
/// </summary>
[TestFixture]
public class GameEngineIntegrationTests
{
    private class TestScreen : IScreen
    {
        private readonly byte[] _framebuffer = new byte[320 * 256];
        public int Width => 320;
        public int Height => 256;
        public Span<byte> GetFramebuffer() => _framebuffer;
        public void Clear(byte color = 0) => Array.Fill(_framebuffer, color);

        /// <summary>Check how many non-zero pixels exist in the play area (rows 16-255).</summary>
        public int CountNonZeroInPlayArea()
        {
            int count = 0;
            for (int y = 16; y < 256; y++)
                for (int x = 0; x < 320; x++)
                    if (_framebuffer[y * 320 + x] != 0)
                        count++;
            return count;
        }

        /// <summary>Get pixel at play-area coordinates (row 0 = top of play area).</summary>
        public byte GetPlayPixel(int x, int y) => _framebuffer[(y + 16) * 320 + x];
    }

    [Test]
    public void Engine_StartNewGame_ProducesNonZeroPixels()
    {
        var random = new Relander.Core.Engine.RandomGenerator(12345);
        var screen = new TestScreen();
        var input = new TestInput();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();

        // Run a few frames to build up some rendering
        for (int i = 0; i < 5; i++)
            engine.Update(input);

        int nonZero = screen.CountNonZeroInPlayArea();
        Assert.That(nonZero, Is.GreaterThan(0),
            "After 5 frames, play area should have some non-black pixels");
    }

    [Test]
    public void Engine_StartNewGame_LandscapeIsDrawn()
    {
        var random = new Relander.Core.Engine.RandomGenerator(42);
        var screen = new TestScreen();
        var input = new TestInput();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();

        // Run one frame
        engine.Update(input);

        // Check if there are pixels in the lower half of the screen (landscape area)
        int bottomPixels = 0;
        for (int y = 120; y < 240; y++)
            for (int x = 0; x < 320; x++)
                if (screen.GetPlayPixel(x, y) != 0)
                    bottomPixels++;

        Assert.That(bottomPixels, Is.GreaterThan(100),
            $"Bottom half (landscape) should have many pixels, got {bottomPixels}");
    }

    [Test]
    public void Engine_PlayerShip_IsAtScreenCenter()
    {
        var random = new Relander.Core.Engine.RandomGenerator(99);
        var screen = new TestScreen();
        var input = new TestInput();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();
        engine.Update(input);

        // The ship should be drawn near screen center (pixels around x=160, y=100 area in play coords)
        // Check there are non-zero pixels near center
        int centerPixels = 0;
        for (int y = 80; y < 140; y++)
            for (int x = 130; x < 190; x++)
                if (screen.GetPlayPixel(x, y) != 0)
                    centerPixels++;

        Assert.That(centerPixels, Is.GreaterThan(0),
            "Ship should be drawn near screen center");
    }

    [Test]
    public void Engine_StateAfterInit_HasCorrectDefaults()
    {
        var random = new Relander.Core.Engine.RandomGenerator(1);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();

        var state = engine.State;
        Assert.That(state.CurrentScore, Is.EqualTo(FixedPoint.INITIAL_SCORE));
        Assert.That(state.RemainingLives, Is.EqualTo(FixedPoint.INITIAL_LIVES));
        Assert.That(state.PlayingGame, Is.EqualTo(-1));  // Playing
        Assert.That(state.Gravity, Is.EqualTo(FixedPoint.BASE_GRAVITY));
    }

    [Test]
    public void Engine_PlayerPosition_IsOnLaunchpad()
    {
        var random = new Relander.Core.Engine.RandomGenerator(7);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();

        var state = engine.State;
        int padHalf = FixedPoint.LAUNCHPAD_SIZE / 2;
        Assert.That(state.XPlayer, Is.EqualTo(padHalf));
        Assert.That(state.YPlayer, Is.EqualTo(FixedPoint.LAUNCHPAD_Y));
        Assert.That(state.ZPlayer, Is.EqualTo(padHalf));
    }

    [Test]
    public void Engine_ThrustAboveAltitudeCeiling_CutsEnginesAndExhaust()
    {
        // Above the ceiling the original clears the hover/thrust bits and stores
        // the result back to fuelBurnRate (Lander.arm:1904-1907: STRLTB), so the
        // exhaust gate (fuelBurnRate & 6) also closes and no plume is drawn.
        var random = new Relander.Core.Engine.RandomGenerator(31);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        // Fly high: -y exceeds HIGHEST_ALTITUDE (52 tiles)
        engine.State.YPlayer = -(FixedPoint.HIGHEST_ALTITUDE + FixedPoint.TILE_SIZE);
        engine.State.XPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;
        engine.State.ZPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;

        engine.Update(new TestInput { Thrust = true });

        Assert.That(engine.State.FuelBurnRate & 6, Is.EqualTo(0),
            "Hover and thrust bits must be cleared (and persisted) above the altitude ceiling");
    }

    [Test]
    public void Engine_FuelDecreases_WhenThrustingOffLaunchpad()
    {
        var random = new Relander.Core.Engine.RandomGenerator(3);
        var screen = new TestScreen();
        var input = new TestInput { Thrust = true };  // Full thrust
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();
        // Move player off launchpad so refueling doesn't mask the burn
        engine.State.XPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;
        engine.State.ZPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;
        int fuelBefore = engine.State.FuelLevel;

        engine.Update(input);
        int fuelAfter = engine.State.FuelLevel;

        Assert.That(fuelAfter, Is.LessThan(fuelBefore),
            $"Fuel should decrease when thrusting off launchpad. Before={fuelBefore}, After={fuelAfter}");
    }

    [Test]
    public void Engine_ZeroFuel_PreventsThrust()
    {
        var random = new Relander.Core.Engine.RandomGenerator(5);
        var screen = new TestScreen();
        var input = new TestInput { Thrust = true };
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();
        // Force fuel to zero
        engine.State.FuelLevel = 0;

        int yBefore = engine.State.YPlayer;
        engine.Update(input);

        // Without thrust and with gravity, ship should not rise
        Assert.That(engine.State.YPlayer, Is.GreaterThanOrEqualTo(yBefore),
            "Without fuel, ship should not rise with thrust");
    }

    [Test]
    public void Engine_ObjectsArePlaced()
    {
        var random = new Relander.Core.Engine.RandomGenerator(13);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();

        // Object map should have non-empty entries
        var map = engine.ObjectMap.Map;
        int nonEmpty = map.Count(b => b != 0xFF);
        Assert.That(nonEmpty, Is.GreaterThan(0),
            "Object map should have placed objects");
    }

    [Test]
    public void Engine_RenderLandscape_CoversBottomOfScreen()
    {
        var random = new Relander.Core.Engine.RandomGenerator(17);
        var screen = new TestScreen();
        var input = new TestInput();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();
        engine.Update(input);

        // Sample several columns at the bottom of the screen — should find landscape
        bool foundLandscape = false;
        for (int x = 20; x < 300; x += 40)
        {
            // Check rows near bottom of play area
            for (int y = 200; y < 238; y++)
            {
                if (screen.GetPlayPixel(x, y) != 0)
                {
                    foundLandscape = true;
                    break;
                }
            }
            if (foundLandscape) break;
        }
        Assert.That(foundLandscape, Is.True,
            "Landscape should draw some non-black pixels near the bottom of the screen");
    }

    [Test]
    public void Engine_CameraFollowsPlayer()
    {
        var random = new Relander.Core.Engine.RandomGenerator(23);
        var screen = new TestScreen();
        var input = new TestInput();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();
        engine.Update(input);

        var state = engine.State;
        // Camera should be at player's x, capped y, and z + CAMERA_PLAYER_Z behind
        Assert.That(state.XCamera, Is.EqualTo(state.XPlayer));
        Assert.That(state.YCamera, Is.LessThanOrEqualTo(0));  // Capped at 0
        Assert.That(state.ZCamera, Is.EqualTo(state.ZPlayer + FixedPoint.CAMERA_PLAYER_Z));
    }

    [Test]
    public void Engine_CornersAreProjected_ToValidScreenCoords()
    {
        var random = new Relander.Core.Engine.RandomGenerator(29);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();

        // Manually project landscape corners to verify they fall on-screen
        var state = engine.State;
        int zRow = FixedPoint.LANDSCAPE_Z - (state.ZCamera & unchecked((int)0xFF000000));

        for (int row = 0; row < FixedPoint.TILES_Z; row++)
        {
            int projZ = zRow - row * FixedPoint.TILE_SIZE;
            int worldZ = (state.ZCamera & unchecked((int)0xFF000000)) - row * FixedPoint.TILE_SIZE;

            for (int col = 0; col < FixedPoint.TILES_X; col++)
            {
                int xCameraTile = state.XCamera & unchecked((int)0xFF000000);
                int worldX = xCameraTile - FixedPoint.LANDSCAPE_X + col * FixedPoint.TILE_SIZE;
                int relX = worldX - state.XCamera;

                // Use the landscape generator's altitude
                int alt = engine.Landscape.GetAltitude(worldX, worldZ);
                int relY = alt - state.YCamera;

                bool projected = Projection.Project(relX, relY, projZ, out int sx, out int sy);

                if (projected)
                {
                    Assert.That(sx, Is.InRange(-1000, 2000),
                        $"Row {row} Col {col}: screenX={sx} is way off screen");
                    Assert.That(sy, Is.InRange(-1000, 2000),
                        $"Row {row} Col {col}: screenY={sy} is way off screen");
                }
            }
        }
    }


    [Test]
    public void ObjectOnNinthZRow_IsVisible()
    {
        // Objects on the 9th z-row (tz = 8) map to graphics buffer 9. The main
        // loop must draw buffers 9 and 10 after the landscape
        // (Lander.arm:1216-1222); drawing 10 and 11 instead left row-9 objects
        // invisible (buffer indices clamp at 10, so 11 is never populated).
        var random = new Relander.Core.Engine.RandomGenerator(42);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        // Settle the camera first (it is set during the first update frame)
        engine.Update(new TestInput());

        var state = engine.State;
        int camTileX = state.XCamera & unchecked((int)0xFF000000);
        int camTileZ = state.ZCamera & unchecked((int)0xFF000000);
        // Tile containing the camera x — projects to screen centre (sx ~ 160)
        int worldX = camTileX;
        int worldZ = (camTileZ - 8 * FixedPoint.TILE_SIZE) & unchecked((int)0xFF000000);

        // Place a building (type 8) on the 9th z-row, horizontally centered
        engine.ObjectMap.SetObjectAt(worldX, worldZ, 8);

        screen.Clear(0);
        engine.Update(new TestInput());
        int withObject = CountNonZero(screen, 60, 40, 240, 200);

        // Remove the object and re-render; the landscape and ship are identical,
        // so any extra pixels in the region come from the object. (The region
        // excludes the inset minimap at x >= 252, which also shows objects.)
        engine.ObjectMap.SetObjectAt(worldX, worldZ, (byte)ObjectTypes.NO_OBJECT);
        screen.Clear(0);
        engine.Update(new TestInput());
        int withoutObject = CountNonZero(screen, 60, 40, 240, 200);

        Assert.That(withObject, Is.GreaterThan(withoutObject),
            $"Object on the 9th z-row must be drawn (with={withObject}, without={withoutObject})");
    }

    private static int CountNonZero(TestScreen screen, int x0, int y0, int x1, int y1)
    {
        int count = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (screen.GetPlayPixel(x, y) != 0)
                    count++;
        return count;
    }

}
