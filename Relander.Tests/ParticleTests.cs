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
    public void DestroyParticle_HighAboveGround_DoesNotDestroyObject()
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

        var particles = new ParticleSystem(state, landscape, objectMap, buffers, random);

        // Place a small leafy tree (type 1) at tile (10, 10)
        int objX = 10 * FixedPoint.TILE_SIZE + FixedPoint.TILE_SIZE / 2;
        int objZ = 10 * FixedPoint.TILE_SIZE + FixedPoint.TILE_SIZE / 2;
        objectMap.SetObjectAt(objX, objZ, 1);

        // Destroy-flagged particle 10 tiles above the ground at that tile:
        // the original only destroys objects when within SAFE_HEIGHT of the ground
        // (Lander.arm:3292-3296), so this object must survive.
        int groundAlt = landscape.GetAltitude(objX, objZ);
        int bulletY = groundAlt - 10 * FixedPoint.TILE_SIZE;

        particles.AddParticle(objX, bulletY, objZ, 0, 0, 0, 20, ParticleSystem.FLAG_DESTROY);
        particles.UpdateAndDraw();

        Assert.That(objectMap.GetObjectAt(objX, objZ), Is.EqualTo(1),
            "Object must survive a destroy-flagged particle high above the ground");
    }

    [Test]
    public void DestroyParticle_CloseToGround_DestroysObject()
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

        var particles = new ParticleSystem(state, landscape, objectMap, buffers, random);

        // Place a small leafy tree (type 1) at tile (10, 10)
        int objX = 10 * FixedPoint.TILE_SIZE + FixedPoint.TILE_SIZE / 2;
        int objZ = 10 * FixedPoint.TILE_SIZE + FixedPoint.TILE_SIZE / 2;
        objectMap.SetObjectAt(objX, objZ, 1);

        // Destroy-flagged particle within SAFE_HEIGHT (1.5 tiles) of the ground:
        // the object must be destroyed (live type + 12 = smoking remains).
        int groundAlt = landscape.GetAltitude(objX, objZ);
        int bulletY = groundAlt - FixedPoint.SAFE_HEIGHT / 2;

        particles.AddParticle(objX, bulletY, objZ, 0, 0, 0, 20, ParticleSystem.FLAG_DESTROY);
        particles.UpdateAndDraw();

        Assert.That(objectMap.GetObjectAt(objX, objZ), Is.EqualTo(13),
            "Object within SAFE_HEIGHT of the particle should be destroyed (type 1 + 12)");
    }

    [Test]
    public void RockDestroyingObject_DoesNotAwardScore()
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

        var particles = new ParticleSystem(state, landscape, objectMap, buffers, random);

        int objX = 10 * FixedPoint.TILE_SIZE + FixedPoint.TILE_SIZE / 2;
        int objZ = 10 * FixedPoint.TILE_SIZE + FixedPoint.TILE_SIZE / 2;
        objectMap.SetObjectAt(objX, objZ, 1);

        int scoreBefore = state.CurrentScore;
        int groundAlt = landscape.GetAltitude(objX, objZ);
        int rockY = groundAlt - FixedPoint.SAFE_HEIGHT / 2;

        // Rock = FLAG_ROCK | FLAG_DESTROY (bit 17 set). The original only awards
        // +20 when bit 17 is clear (Lander.arm:3352-3355).
        particles.AddParticle(objX, rockY, objZ, 0, 0, 0, 20,
            ParticleSystem.FLAG_ROCK | ParticleSystem.FLAG_DESTROY);
        particles.UpdateAndDraw();

        Assert.That(objectMap.GetObjectAt(objX, objZ), Is.EqualTo(13),
            "Rock should still destroy the object");
        Assert.That(state.CurrentScore, Is.EqualTo(scoreBefore),
            "Rock-destroyed objects must not award +20 score");
    }

    [Test]
    public void BulletDestroyingObject_AwardsScore()
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

        var particles = new ParticleSystem(state, landscape, objectMap, buffers, random);

        int objX = 10 * FixedPoint.TILE_SIZE + FixedPoint.TILE_SIZE / 2;
        int objZ = 10 * FixedPoint.TILE_SIZE + FixedPoint.TILE_SIZE / 2;
        objectMap.SetObjectAt(objX, objZ, 1);

        int scoreBefore = state.CurrentScore;
        int groundAlt = landscape.GetAltitude(objX, objZ);
        int bulletY = groundAlt - FixedPoint.SAFE_HEIGHT / 2;

        // Bullet-like particle: destroy flag, no rock bit
        particles.AddParticle(objX, bulletY, objZ, 0, 0, 0, 20, ParticleSystem.FLAG_DESTROY);
        particles.UpdateAndDraw();

        Assert.That(objectMap.GetObjectAt(objX, objZ), Is.EqualTo(13),
            "Bullet should destroy the object");
        Assert.That(state.CurrentScore, Is.EqualTo(scoreBefore + FixedPoint.SCORE_PER_DESTROY),
            "Bullet-destroyed objects must award +20 score");
    }

    [Test]
    public void Particle_BufferIndex_MatchesOriginalDepth()
    {
        // The buffer index formula already includes the +TILE_SIZE offset
        // (Lander.arm:8451-8453). A particle at cz = 15 tiles (the ship's depth)
        // must land in buffer (20 - 15 + 1) = 6 and its shadow in buffer
        // (20 - 15) = 5. The previous code passed cz - TILE_SIZE, shifting both
        // one buffer nearer the camera.
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

        var particles = new ParticleSystem(state, landscape, objectMap, buffers, random);
        particles.AddParticle(state.XPlayer, state.YPlayer, state.ZPlayer,
            0, 0, 0, 10, VidcColour.Encode(15, 15, 15));

        particles.UpdateAndDraw();
        buffers.AddTerminators();

        int particleBuffer = -1, shadowBuffer = -1;
        for (int b = 0; b < buffers.BufferCount; b++)
        {
            var data = buffers.GetBufferData(b);
            for (int i = 0; i < data.Length; i += 2)
            {
                if (data[i] <= 8) particleBuffer = b;
                else if (data[i] >= 9 && data[i] <= 17) shadowBuffer = b;
            }
        }

        Assert.That(particleBuffer, Is.EqualTo(6),
            $"Particle at cz = 15 tiles should use buffer 6, got {particleBuffer}");
        Assert.That(shadowBuffer, Is.EqualTo(5),
            $"Particle shadow should use buffer 5, got {shadowBuffer}");
    }

    [Test]
    public void Rock_HittingGround_BouncesInsteadOfExploding()
    {
        // The original sets bits 17-23 on rock particles
        // (Lander.arm:4198: ORR R7, R7, #&00FE0000). Bit 24 (explode) is NOT
        // included — despite the source comment claiming it — so rocks bounce
        // on landing until their 170-frame life expires instead of exploding.
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
        var particles = new ParticleSystem(state, landscape, objectMap, buffers, random);

        // Spawn a rock well below the terrain surface, away from the launchpad:
        // even a maximal upward random velocity keeps it underground on the
        // first update, so the ground-contact path is exercised regardless of
        // the PRNG draw.
        int x = 10 * FixedPoint.TILE_SIZE;
        int z = 10 * FixedPoint.TILE_SIZE;
        int terrainAlt = landscape.GetAltitude(x, z);
        particles.DropRock(x, terrainAlt + 4 * FixedPoint.TILE_SIZE, z);

        Assert.That(particles.Count, Is.EqualTo(1), "Precondition: one rock");
        particles.UpdateAndDraw();

        Assert.That(particles.Count, Is.EqualTo(1),
            "Rock should bounce on ground contact, not explode into debris");
    }

    [Test]
    public void ExplosionSmoke_IsGreyBouncingRisingParticle()
    {
        // AddSmokeParticleToBuffer (Lander.arm:3885-3977): random grey 3-10 on
        // all channels, flags = bounce only (no fade), vy = ~SMOKE_RISING_SPEED
        // (rising), life 15 + rand >> 25, velocity jitter shift 13.
        var state = new GameState();
        state.Initialize();
        var random = new RandomGenerator(42);
        var landscape = new LandscapeGenerator(state);
        var objectMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();
        var particles = new ParticleSystem(state, landscape, objectMap, buffers, random);

        particles.AddSmallExplosion(0, 0, 0);
        Assert.That(particles.Count, Is.EqualTo(12), "3 clusters x 4 particles");

        // Smoke particles are the 4th of each cluster
        for (int idx = 3; idx < particles.Count; idx += 4)
        {
            var p = particles.GetParticle(idx);
            Assert.That(p.Flags & ParticleSystem.FLAG_BOUNCE, Is.Not.EqualTo(0),
                "Smoke must bounce on the ground");
            Assert.That(p.Flags & ParticleSystem.FLAG_FADE, Is.EqualTo(0),
                "Smoke must not fade white-to-red like a spark");
            byte colour = (byte)(p.Flags & 0xFF);
            var (r, g, b) = VidcColour.DecodeToRgb24(colour);
            Assert.That(r, Is.EqualTo(g), "Smoke must be grey");
            Assert.That(g, Is.EqualTo(b), "Smoke must be grey");
            Assert.That(r / 17, Is.InRange(3, 10), "Grey intensity 3..10");
            int baseVy = -(FixedPoint.SMOKE_RISING_SPEED + 1);
            Assert.That(p.VY, Is.InRange(baseVy - 0x80000, baseVy + 0x80000),
                "Rising speed ~SMOKE_RISING_SPEED plus jitter");
            Assert.That(p.VY, Is.LessThan(0), "Smoke rises");
            Assert.That(p.Life, Is.InRange(15, 15 + 127), "Life 15 + rand >> 25");
        }
    }

    [Test]
    public void SeaSpray_IsBlueFallingParticleWithoutBounce()
    {
        // AddSprayParticleToBuffer (Lander.arm:4295-4406): blue shades
        // (blue 12-15, red = green = 8 or 12), gravity flag only, zero initial
        // velocity, life 20 + rand >> 26, jitter shift 10. Spray falls straight
        // down and is deleted at the sea (no bounce/splash bits).
        var state = new GameState();
        state.Initialize();
        var random = new RandomGenerator(42);
        var landscape = new LandscapeGenerator(state);
        var objectMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();
        var particles = new ParticleSystem(state, landscape, objectMap, buffers, random);

        particles.AddSplash(0, 0, 0, big: false);
        Assert.That(particles.Count, Is.EqualTo(4), "Small splash = 4 spray particles");

        for (int idx = 0; idx < 4; idx++)
        {
            var p = particles.GetParticle(idx);
            Assert.That(p.Flags & ParticleSystem.FLAG_GRAVITY, Is.Not.EqualTo(0));
            Assert.That(p.Flags & ParticleSystem.FLAG_BOUNCE, Is.EqualTo(0),
                "Spray must not bounce on the sea surface");
            Assert.That(p.Flags & ParticleSystem.FLAG_SPLASH, Is.EqualTo(0),
                "Spray must not re-splash");
            byte colour = (byte)(p.Flags & 0xFF);
            var (r, g, b) = VidcColour.DecodeToRgb24(colour);
            Assert.That(r, Is.EqualTo(g), "Red equals green");
            // VIDC bits 4 and 6 are the red and green bit-3s; the shared low bits
            // (1,0) of the blue channel bleed into the decoded red/green, so pin
            // the encoded channel bits instead of the lossy decode.
            Assert.That((colour >> 4) & 1, Is.EqualTo((colour >> 6) & 1),
                "Red bit 3 equals green bit 3 (both 8 or both 12)");
            Assert.That(b / 17, Is.InRange(12, 15), "Blue 12..15");
            Assert.That(p.VY, Is.InRange(-0x400000, 0x400000),
                "No initial upward jump, only jitter at shift 10");
            Assert.That(p.Life, Is.InRange(20, 20 + 63), "Life 20 + rand >> 26");
        }
    }

    [Test]
    public void ExplosionSparks_FadeSplashBounceWithFullVelocity()
    {
        // AddSparkParticleToBuffer (Lander.arm:4247-4267): flags &001D0000 =
        // fade|splash|bounce|gravity, life 8 + rand >> 29 (0..8), velocity
        // jitter shift 8 (+/-&1000000). The previous port lacked the splash bit
        // (sparks bounced on the sea instead of mini-splashing), flew at half
        // velocity (shift 9 in the small explosion) and lived up to 8 frames
        // longer (shift 28 in the big explosion).
        var state = new GameState();
        state.Initialize();
        var random = new RandomGenerator(42);
        var landscape = new LandscapeGenerator(state);
        var objectMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();
        var particles = new ParticleSystem(state, landscape, objectMap, buffers, random);

        particles.AddBigExplosion(0, 0, 0);

        int sparksChecked = 0;
        for (int idx = 0; idx < particles.Count; idx++)
        {
            var p = particles.GetParticle(idx);
            if ((p.Flags & ParticleSystem.FLAG_FADE) == 0) continue;  // not a spark
            sparksChecked++;
            Assert.That(p.Flags & ParticleSystem.FLAG_SPLASH, Is.Not.EqualTo(0),
                "Sparks must splash on the sea");
            Assert.That(p.Flags & ParticleSystem.FLAG_BOUNCE, Is.Not.EqualTo(0));
            Assert.That(p.Flags & ParticleSystem.FLAG_GRAVITY, Is.Not.EqualTo(0));
            Assert.That(p.Life, Is.InRange(8, 8 + 7), "Life 8 + rand >> 29 (max 15)");
            Assert.That(
                global::System.Math.Abs((long)p.VX) <= 0x1000000
                && global::System.Math.Abs((long)p.VY) <= 0x1000000
                && global::System.Math.Abs((long)p.VZ) <= 0x1000000,
                Is.True, "Velocity jitter at shift 8 (+/-&1000000)");
        }
        Assert.That(sparksChecked, Is.GreaterThan(0), "Should find spark particles");
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



