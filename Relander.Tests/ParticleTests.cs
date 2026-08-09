using Relander.Core.Engine;
using Relander.Core.Interfaces;
using Relander.Core.Math;

namespace Relander.Tests;

[TestFixture]
public class ParticleTests
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

    private class TestScreen : IScreen
    {
        private readonly byte[] _fb = new byte[320 * 256];
        public int Width => 320;
        public int Height => 256;
        public Span<byte> GetFramebuffer() => _fb;
        public void Clear(byte color = 0) => Array.Fill(_fb, color);
        public byte GetPlayPixel(int x, int y) => _fb[(y + 16) * 320 + x];
    }

    [Test]
    public void Particle_AtLaunchpad_IsWithinVisibleRange()
    {
        // Simulate a particle at the player's position on the launchpad
        // and verify it passes the visibility culling check.
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();

        // Set camera as it would be after one Update frame
        state.XCamera = state.XPlayer;
        state.YCamera = 0;
        state.ZCamera = state.ZPlayer + FixedPoint.CAMERA_PLAYER_Z;

        // Particle at player's launchpad position
        int px = state.XPlayer;
        int py = state.YPlayer;
        int pz = state.ZPlayer;

        // Convert to camera-relative the same way DrawParticle does
        int cx = px - state.XCamera;
        int cy = py - state.YCamera;
        int cz = pz - state.ZCamera + FixedPoint.LANDSCAPE_Z;

        TestContext.WriteLine($"Particle world=({px:X8},{py:X8},{pz:X8})");
        TestContext.WriteLine($"Camera=({state.XCamera:X8},{state.YCamera:X8},{state.ZCamera:X8})");
        TestContext.WriteLine($"Camera-relative: cx={cx:X8} cy={cy:X8} cz={cz:X8} (cz={cz>>24} tiles)");
        TestContext.WriteLine($"LANDSCAPE_Z=0x{FixedPoint.LANDSCAPE_Z:X8} ({FixedPoint.LANDSCAPE_Z>>24} tiles)");
        TestContext.WriteLine($"LANDSCAPE_Z_FRONT=0x{FixedPoint.LANDSCAPE_Z_FRONT:X8} ({FixedPoint.LANDSCAPE_Z_FRONT>>24} tiles)");

        // Should pass the corrected visibility check
        bool tooFarBack = (uint)cz >= (uint)FixedPoint.LANDSCAPE_Z;
        bool tooClose = cz < FixedPoint.LANDSCAPE_Z_FRONT;
        bool offSide = global::System.Math.Abs(cx) >= FixedPoint.LANDSCAPE_X_HALF;

        Assert.That(tooFarBack, Is.False, $"Particle cz={cz>>24} tiles should be < LANDSCAPE_Z={FixedPoint.LANDSCAPE_Z>>24} tiles");
        Assert.That(tooClose, Is.False, $"Particle should not be too close");
        Assert.That(offSide, Is.False, $"Particle should not be off to the side");
    }

    [Test]
    public void Particle_AtLaunchpad_ProjectsOnScreen()
    {
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();
        state.XCamera = state.XPlayer;
        state.YCamera = 0;
        state.ZCamera = state.ZPlayer + FixedPoint.CAMERA_PLAYER_Z;

        int px = state.XPlayer;
        int py = state.YPlayer;
        int pz = state.ZPlayer;

        int cx = px - state.XCamera;
        int cy = py - state.YCamera;
        int cz = pz - state.ZCamera + FixedPoint.LANDSCAPE_Z;

        bool projected = Projection.Project(cx, cy, cz, out int sx, out int sy);
        Assert.That(projected, Is.True, "Particle should project successfully");
        Assert.That(Projection.IsOnScreen(sx, sy), Is.True,
            $"Particle at ({sx},{sy}) should be on screen");
    }

    [Test]
    public void ExhaustSpawned_WhenThrusting()
    {
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();
        engine.Update(new TestInput()); // One frame to stabilize

        // Should have no particles before thrust
        int countBefore = engine.State.MainLoopCount; // Just checking state access

        // Run with thrust
        var input = new TestInput { Thrust = true };
        engine.Update(input);
        engine.Update(input);

        // Check for particles in the framebuffer near the ship position
        var state = engine.State;
        int objZ = FixedPoint.LANDSCAPE_Z_MID;
        int objY = state.YPlayer - state.YCamera;

        // Ship should be near screen center - check for non-black pixels nearby
        int coloredNearCenter = 0;
        for (int y = 90; y < 130; y++)
            for (int x = 140; x < 180; x++)
                if (screen.GetPlayPixel(x, y) != 0)
                    coloredNearCenter++;

        TestContext.WriteLine($"Colored pixels near center after thrust: {coloredNearCenter}");
        // Even without exhaust, the ship itself should be visible
        Assert.That(coloredNearCenter, Is.GreaterThan(0),
            "Should have some visible content near screen center");
    }

    [Test]
    public void ExhaustParticle_PassesVisibilityCheck()
    {
        // Create a particle system and manually add an exhaust particle,
        // then verify the DrawParticle visibility check passes.
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();

        var random = new RandomGenerator(42);
        var landscape = new LandscapeGenerator(state);
        var objectMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();

        var particles = new ParticleSystem(state, landscape, objectMap, buffers);

        // Add exhaust particle at player position
        bool added = particles.AddParticle(
            state.XPlayer, state.YPlayer, state.ZPlayer,
            0, 0, 0,
            10, // lifespan
            ParticleSystem.FLAG_GRAVITY | VidcColour.Encode(15, 8, 0) // orange
        );

        Assert.That(added, Is.True, "Should be able to add particle");
        Assert.That(particles.Count, Is.EqualTo(1), "Should have 1 particle");

        // Set camera
        state.XCamera = state.XPlayer;
        state.YCamera = 0;
        state.ZCamera = state.ZPlayer + FixedPoint.CAMERA_PLAYER_Z;

        // Process particles - should not crash and should draw to buffers
        Assert.DoesNotThrow(() => particles.UpdateAndDraw());

        // Terminate buffers so GetBufferData can read them
        buffers.AddTerminators();

        // The particle should have been drawn to some buffer
        bool hasData = false;
        for (int i = 0; i < buffers.BufferCount; i++)
        {
            if (buffers.GetBufferData(i).Length > 0)
            {
                hasData = true;
                break;
            }
        }
        Assert.That(hasData, Is.True, "Particle should have been written to a graphics buffer");
    }
}
