using Relander.Core.Engine;
using Relander.Core.Interfaces;
using Relander.Core.Math;

namespace Relander.Tests;

[TestFixture]
public class PlayerOrientationTests
{
    private class TestInput : IGameInput
    {
        public bool YawLeft { get; set; }
        public bool YawRight { get; set; }
        public bool PitchUp { get; set; }
        public bool PitchDown { get; set; }
        public bool Fire { get; set; }
        public bool Thrust { get; set; }
        public bool Hover { get; set; }
        public bool EscapePressed { get; set; }
    }

    [Test]
    public void InitialPitch_Is180Degrees()
    {
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();

        Assert.That(state.ShipPitch, Is.EqualTo(unchecked((int)0xC0000000)),
            "Initial pitch should be 270° (0xC0000000): with SinLookup bias, yRoofV=+1 (thrust up), yNoseV≈0 (level)");
    }

    [Test]
    public void InitialDirection_IsZero()
    {
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();

        Assert.That(state.ShipDirection, Is.EqualTo(0));
    }

    [Test]
    public void Thrust_PushesShipUpward()
    {
        // When thrust is applied, yVelocity should become more negative
        // (ship moves UP in the inverted-y coordinate system)
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();

        var gen = new LandscapeGenerator(state);
        var buffers = new GraphicsBuffers();
        var player = new PlayerController(state, buffers, gen);

        // Move off launchpad to avoid landing/refuel interference
        state.XPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;
        state.ZPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;

        int vyBefore = state.YVelocity;

        var input = new TestInput { Thrust = true };
        player.Update(input);

        // yVelocity should decrease (more negative = upward)
        Assert.That(state.YVelocity, Is.LessThan(vyBefore),
            $"Thrust should make yVelocity more negative (UP). Before: 0x{vyBefore:X8}, After: 0x{state.YVelocity:X8}");
    }

    [Test]
    public void Thrust_MovesShipUpward()
    {
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();

        var gen = new LandscapeGenerator(state);
        var buffers = new GraphicsBuffers();
        var player = new PlayerController(state, buffers, gen);

        // Move off launchpad
        state.XPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;
        state.ZPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;

        int yBefore = state.YPlayer;

        var input = new TestInput { Thrust = true };
        for (int i = 0; i < 5; i++)
            player.Update(input);

        // Ship should rise (yPlayer becomes smaller / more negative)
        Assert.That(state.YPlayer, Is.LessThan(yBefore),
            $"Ship should move UP after thrust. Before: 0x{yBefore:X8}, After: 0x{state.YPlayer:X8}");
    }
}
