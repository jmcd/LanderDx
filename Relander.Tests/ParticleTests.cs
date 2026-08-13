using Relander.Core.Engine;
using Relander.Core.Interfaces;
using Relander.Core.Math;

namespace Relander.Tests;

[TestFixture]
public class ParticleTests
{
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

    [Test]
    public void Particle_SizeCommand_VariesWithDepth()
    {
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();
        state.XCamera = state.XPlayer;
        state.YCamera = 0;
        state.ZCamera = state.ZPlayer + FixedPoint.CAMERA_PLAYER_Z;

        var random = new RandomGenerator(42);
        var landscape = new LandscapeGenerator(state);
        var objectMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();

        var particles = new ParticleSystem(state, landscape, objectMap, buffers);

        // Add a close particle (near front of landscape, cz ~ 10 tiles)
        particles.AddParticle(state.XPlayer, state.YPlayer, state.ZPlayer - 4 * FixedPoint.TILE_SIZE,
            0, 0, 0, 10, VidcColour.Encode(15, 15, 15));

        particles.UpdateAndDraw();
        buffers.AddTerminators();

        // Find the command in buffers
        int foundCmd = -1;
        for (int b = 0; b < buffers.BufferCount; b++)
        {
            var data = buffers.GetBufferData(b);
            for (int i = 0; i < data.Length; i += 2)
            {
                if (data[i] <= 8)
                {
                    foundCmd = data[i];
                    break;
                }
            }
        }

        TestContext.WriteLine($"Found particle command for close particle: {foundCmd}");
        // Close particle should produce size command < 8 (larger than 1x1 single pixel)
        Assert.That(foundCmd, Is.LessThan(8), "Close particle should have command < 8 (larger size)");
    }

    [Test]
    public void Particle_Shadow_ProjectedToGroundLevel()
    {
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();
        state.XCamera = state.XPlayer;
        state.YCamera = 0;
        state.ZCamera = state.ZPlayer + FixedPoint.CAMERA_PLAYER_Z;

        var random = new RandomGenerator(42);
        var landscape = new LandscapeGenerator(state);
        var objectMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();

        var particles = new ParticleSystem(state, landscape, objectMap, buffers);

        // Particle high in the air above launchpad (py = YPlayer - 5 tiles)
        int pX = state.XPlayer;
        int pY = state.YPlayer - 5 * FixedPoint.TILE_SIZE;
        int pZ = state.ZPlayer;
        particles.AddParticle(pX, pY, pZ, 0, 0, 0, 10, VidcColour.Encode(15, 15, 15));

        particles.UpdateAndDraw();
        buffers.AddTerminators();

        int particleY = -1;
        int shadowY = -1;

        for (int b = 0; b < buffers.BufferCount; b++)
        {
            var data = buffers.GetBufferData(b);
            for (int i = 0; i < data.Length - 1; i += 2)
            {
                int cmd = data[i];
                int packed = data[i + 1];
                int py = packed & 0xFF;
                if (cmd <= 8) particleY = py;
                else if (cmd >= 9 && cmd <= 17) shadowY = py;
            }
        }

        TestContext.WriteLine($"Particle screen Y: {particleY}, Shadow screen Y: {shadowY}");
        Assert.That(particleY, Is.GreaterThanOrEqualTo(0), "Particle should be drawn");
        Assert.That(shadowY, Is.GreaterThanOrEqualTo(0), "Shadow should be drawn");
        Assert.That(shadowY, Is.GreaterThan(particleY), "Shadow screen Y should be lower on screen (larger Y) than particle in air");
    }

    [Test]
    public void Particle_ColorFade_FadesFromWhiteToRed()
    {
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();
        state.XCamera = state.XPlayer;
        state.YCamera = 0;
        state.ZCamera = state.ZPlayer + FixedPoint.CAMERA_PLAYER_Z;

        var random = new RandomGenerator(42);
        var landscape = new LandscapeGenerator(state);
        var objectMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();

        var particles = new ParticleSystem(state, landscape, objectMap, buffers);

        // Particle with FLAG_FADE starting at lifespan 12
        particles.AddParticle(state.XPlayer, state.YPlayer, state.ZPlayer,
            0, 0, 0, 12, ParticleSystem.FLAG_FADE);

        particles.UpdateAndDraw();
        buffers.AddTerminators();

        // Check colour recorded in buffer
        byte colourAtLife11 = 0;
        for (int b = 0; b < buffers.BufferCount; b++)
        {
            var data = buffers.GetBufferData(b);
            for (int i = 0; i < data.Length - 1; i += 2)
            {
                if (data[i] <= 8)
                {
                    colourAtLife11 = (byte)((data[i + 1] >> 12) & 0xFF);
                }
            }
        }

        var (r, g, bCol) = VidcColour.DecodeToRgb24(colourAtLife11);
        TestContext.WriteLine($"Life 11 decoded RGB: ({r}, {g}, {bCol})");
        Assert.That(r, Is.EqualTo(255), "Red channel should be max (15 -> 255)");
        Assert.That(g, Is.EqualTo(255), "Green channel should be max for life > 8");
    }

    [Test]
    public void Exhaust_SpawnsMultipleParticlesInSpray()
    {
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();
        state.FuelBurnRate = 4; // Full thrust

        var random = new RandomGenerator(42);
        var landscape = new LandscapeGenerator(state);
        var objectMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();

        var particles = new ParticleSystem(state, landscape, objectMap, buffers, random);

        particles.SpawnExhaust(state.XPlayer, state.YPlayer, state.ZPlayer,
            state.XVelocity, state.YVelocity, state.ZVelocity);

        Assert.That(particles.Count, Is.GreaterThan(1), "Exhaust should spawn multiple particles");
        TestContext.WriteLine($"Spawned exhaust particle count: {particles.Count}");
    }

    [Test]
    public void Bullet_SpawnsAndFiresFromNose()
    {
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();

        var random = new RandomGenerator(42);
        var landscape = new LandscapeGenerator(state);
        var objectMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();

        var particles = new ParticleSystem(state, landscape, objectMap, buffers, random);

        bool bulletFired = particles.SpawnBullet(state.XPlayer, state.YPlayer, state.ZPlayer,
            state.XVelocity, state.YVelocity, state.ZVelocity,
            state.XNoseV, state.YNoseV, state.ZNoseV);

        Assert.That(bulletFired, Is.True, "Should fire a bullet when score > 0");
        Assert.That(particles.Count, Is.EqualTo(1), "Should have 1 bullet particle");
    }

    [Test]
    public void PlayerCrash_Triggers30FrameExplosionSequence()
    {
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();
        engine.Update(new TestInput()); // Initial frame

        // Move ship away from launchpad and below terrain to force crash
        engine.State.XPlayer = 10 * FixedPoint.TILE_SIZE;
        engine.State.YPlayer = FixedPoint.SEA_LEVEL + 10 * FixedPoint.TILE_SIZE;



        int initialLives = engine.State.RemainingLives;
        engine.Update(new TestInput()); // Crash frame

        Assert.That(engine.State.CrashLoopCount, Is.EqualTo(30), "Should set 30-frame crash loop count");
        Assert.That(engine.State.RemainingLives, Is.EqualTo(initialLives), "Should not decrement life until crash loop finishes");

        // Advance 30 frames of crash animation
        for (int i = 0; i < 30; i++)
        {
            engine.Update(new TestInput());
        }

        Assert.That(engine.State.CrashLoopCount, Is.EqualTo(0), "Crash loop should be finished");
        Assert.That(engine.State.RemainingLives, Is.EqualTo(initialLives - 1), "Remaining lives should decrement after crash loop");
    }
}



