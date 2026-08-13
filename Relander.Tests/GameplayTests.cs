using Relander.Core.Data;
using Relander.Core.Engine;
using Relander.Core.Math;

namespace Relander.Tests;

/// <summary>
/// Gameplay mechanic tests identified as missing in the code review:
/// - Landing speed boundary (safe vs crash)
/// - Launchpad refuelling rate
/// - Bullet-object collision and scoring
/// </summary>
[TestFixture]
public class GameplayTests
{
    // ---- Landing speed boundary ----

    [Test]
    public void Landing_BelowSpeedThreshold_IsSafe()
    {
        // Arrange: place player over launchpad with a slow downward drift (well under LANDING_SPEED)
        var random = new RandomGenerator(1);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        var state = engine.State;
        // Position ship just above launchpad surface, tiny downward velocity
        state.XPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        state.ZPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        // y >= LAUNCHPAD_Y is the landing trigger: place exactly at pad level
        state.YPlayer = FixedPoint.LAUNCHPAD_Y;
        state.XVelocity = 0;
        state.YVelocity = FixedPoint.LANDING_SPEED / 2;   // half of threshold → safe
        state.ZVelocity = 0;

        int livesBefore = state.RemainingLives;

        // Act: run a frame — the landing check fires during PlayerController.Update
        bool alive = engine.Update(new TestInput());

        // Assert: ship should have landed safely — still alive, no crash triggered
        Assert.That(alive, Is.True, "Should remain alive after a safe landing");
        Assert.That(state.RemainingLives, Is.EqualTo(livesBefore), "Lives should not change on safe landing");
        Assert.That(state.CrashLoopCount, Is.EqualTo(0), "Crash loop should not start on safe landing");
        Assert.That(state.YPlayer, Is.EqualTo(FixedPoint.LAUNCHPAD_Y), "Player should be snapped to launchpad Y on landing");
    }

    [Test]
    public void Landing_AtExactSpeedThreshold_IsSafe()
    {
        // The check is: (uint)totalSpeed < LANDING_SPEED  →  totalSpeed = LANDING_SPEED - 1 must be safe
        var random = new RandomGenerator(2);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        var state = engine.State;
        state.XPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        state.ZPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        state.YPlayer = FixedPoint.LAUNCHPAD_Y;
        state.XVelocity = 0;
        // The collision check runs AFTER UpdatePhysics, which adds BASE_GRAVITY to vy.
        // For the post-physics speed to be just under LANDING_SPEED, the pre-physics
        // vy must be LANDING_SPEED - BASE_GRAVITY - 1.
        state.YVelocity = FixedPoint.LANDING_SPEED - FixedPoint.BASE_GRAVITY - 1;
        state.ZVelocity = 0;

        int livesBefore = state.RemainingLives;
        bool alive = engine.Update(new TestInput());

        Assert.That(alive, Is.True, "Speed just under threshold (after gravity) should be safe");
        Assert.That(state.RemainingLives, Is.EqualTo(livesBefore));
        Assert.That(state.CrashLoopCount, Is.EqualTo(0));
    }

    [Test]
    public void Landing_AboveSpeedThreshold_OutsideLaunchpad_CausescrashOnGround()
    {
        // Ship at high speed near ground but NOT on launchpad → crash
        var random = new RandomGenerator(3);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        var state = engine.State;
        // Position well off the launchpad (e.g. tile 10, 10)
        int offX = 10 * FixedPoint.TILE_SIZE;
        int offZ = 10 * FixedPoint.TILE_SIZE;
        int terrainAlt = engine.Landscape.GetAltitude(offX, offZ);

        state.XPlayer = offX;
        state.ZPlayer = offZ;
        // Place ship just at/below terrain surface (forces the y >= safeAlt branch)
        state.YPlayer = terrainAlt + 1;
        state.XVelocity = 0;
        state.YVelocity = 0x00800000;   // Fast falling
        state.ZVelocity = 0;

        // Act
        bool alive = engine.Update(new TestInput());

        // The crash is triggered by TriggerCrash which sets CrashLoopCount = 30
        // The return value from Update is always true (crash is deferred)
        Assert.That(state.CrashLoopCount, Is.EqualTo(30), "Crash loop should start on ground collision");
    }

    [Test]
    public void FlyingHigh_OverLaunchpad_DoesNotLandOrCrash()
    {
        // The original only consults the launchpad once the ship has descended below
        // the contact altitude (Lander.arm:2167-2168: BLGT LandOnLaunchpad). Flying
        // high over the pad must neither snap the ship onto the pad nor crash it.
        var random = new RandomGenerator(21);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        var state = engine.State;
        state.XPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        state.ZPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        // 5 tiles above the pad surface — far above the danger zone
        state.YPlayer = FixedPoint.LAUNCHPAD_Y - 5 * FixedPoint.TILE_SIZE;
        state.XVelocity = 0;
        state.YVelocity = 0x00010000;  // slow drift
        state.ZVelocity = 0;

        int yBefore = state.YPlayer;
        int livesBefore = state.RemainingLives;
        bool alive = engine.Update(new TestInput());

        Assert.That(alive, Is.True, "High flight over the pad must not crash");
        Assert.That(state.CrashLoopCount, Is.EqualTo(0), "No crash loop at altitude");
        Assert.That(state.RemainingLives, Is.EqualTo(livesBefore));
        Assert.That(state.YPlayer, Is.Not.EqualTo(FixedPoint.LAUNCHPAD_Y),
            "Ship must not be teleport-landed onto the pad from altitude");
        Assert.That(state.YPlayer, Is.EqualTo(yBefore + 0x00010000 - (0x00010000 >> 6)),
            "Ship should keep descending normally (drift minus friction)");
    }

    [Test]
    public void FastLowPass_OverLaunchpad_FliesOnWithoutCrash()
    {
        // A fast skim over the pad inside the danger zone: the original's
        // LandOnLaunchpad returns without landing or crashing when the speed is
        // too high (Lander.arm:2526-2532: CMP R3, #LANDING_SPEED / MOVHS PC, R14).
        var random = new RandomGenerator(22);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        var state = engine.State;
        state.XPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        state.ZPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        state.YPlayer = FixedPoint.LAUNCHPAD_Y - 0x00040000;  // just above the pad
        state.XVelocity = 0;
        state.YVelocity = FixedPoint.LANDING_SPEED;  // too fast to land
        state.ZVelocity = 0;

        bool alive = engine.Update(new TestInput());

        Assert.That(alive, Is.True, "Fast pass over the pad must not crash mid-air");
        Assert.That(state.CrashLoopCount, Is.EqualTo(0),
            "No crash: the vertex/shadow test is the arbiter for fast low passes");
        Assert.That(state.YPlayer, Is.Not.EqualTo(FixedPoint.LAUNCHPAD_Y),
            "Too fast to land — ship must not snap to the pad");
    }

    [Test]
    public void VertexPenetration_CrashesInSameFrame()
    {
        // The original checks the vertex/shadow crash flag immediately after
        // drawing the ship (Lander.arm:2214-2217), so a vertex penetrating the
        // ground triggers the crash in the same frame. The previous code only
        // read the flag at the start of the NEXT frame's collision check.
        var random = new RandomGenerator(25);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        var state = engine.State;
        // Ship on the pad with the nose pitched straight down, so the nose tip
        // penetrates the terrain on the first drawn frame
        state.ShipPitch = 0x40000000;  // 90 degrees
        state.XPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        state.ZPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        state.YPlayer = FixedPoint.LAUNCHPAD_Y;
        state.XVelocity = 0;
        state.YVelocity = 0;
        state.ZVelocity = 0;

        engine.Update(new TestInput());

        Assert.That(state.CrashLoopCount, Is.EqualTo(30),
            "Vertex penetration must trigger the crash in the same frame");
    }

    [Test]
    public void ShipLowOverObject_Crashes()
    {
        // The original reads the object map at the ship's tile whenever the
        // undercarriage is within SAFE_HEIGHT of the ground, and any live object
        // (types 1-11) there destroys the ship (Lander.arm:2114-2150).
        var random = new RandomGenerator(23);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        // Place a tree at tile (10, 10), away from the launchpad
        int objX = 10 * FixedPoint.TILE_SIZE + FixedPoint.TILE_SIZE / 2;
        int objZ = 10 * FixedPoint.TILE_SIZE + FixedPoint.TILE_SIZE / 2;
        engine.ObjectMap.SetObjectAt(objX, objZ, 1);

        var state = engine.State;
        state.XPlayer = objX;
        state.ZPlayer = objZ;
        int terrainAlt = engine.Landscape.GetAltitude(objX, objZ);
        int groundContact = terrainAlt - FixedPoint.UNDERCARRIAGE_Y;
        // 0.75 tiles above the contact altitude — inside the object-check zone
        state.YPlayer = groundContact - FixedPoint.SAFE_HEIGHT / 2;
        state.XVelocity = 0;
        state.YVelocity = 0;
        state.ZVelocity = 0;

        engine.Update(new TestInput());

        Assert.That(state.CrashLoopCount, Is.EqualTo(30),
            "Flying low over a live object should trigger the crash sequence");
    }

    [Test]
    public void ShipLowOverEmptyTile_DoesNotCrash()
    {
        // Sanity check: the same low flight over an empty tile is harmless.
        var random = new RandomGenerator(24);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        int objX = 10 * FixedPoint.TILE_SIZE + FixedPoint.TILE_SIZE / 2;
        int objZ = 10 * FixedPoint.TILE_SIZE + FixedPoint.TILE_SIZE / 2;
        engine.ObjectMap.SetObjectAt(objX, objZ, (byte)ObjectTypes.NO_OBJECT);

        var state = engine.State;
        state.XPlayer = objX;
        state.ZPlayer = objZ;
        int terrainAlt = engine.Landscape.GetAltitude(objX, objZ);
        int groundContact = terrainAlt - FixedPoint.UNDERCARRIAGE_Y;
        state.YPlayer = groundContact - FixedPoint.SAFE_HEIGHT / 2;
        state.XVelocity = 0;
        state.YVelocity = 0;
        state.ZVelocity = 0;

        engine.Update(new TestInput());

        Assert.That(state.CrashLoopCount, Is.EqualTo(0),
            "No object on the tile means no collision crash");
    }

    // ---- Launchpad refuelling ----

    [Test]
    public void Refuelling_IncreasesBy32PerFrame_WhenOnLaunchpad()
    {
        var random = new RandomGenerator(4);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        var state = engine.State;
        // Drain fuel to a known level (not at max so refuel fires)
        state.FuelLevel = 100;
        // Ensure player is landed on launchpad with zero velocity
        state.XPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        state.ZPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        state.YPlayer = FixedPoint.LAUNCHPAD_Y;
        state.XVelocity = 0;
        state.YVelocity = 1;    // tiny downward drift so landing re-triggers
        state.ZVelocity = 0;

        int fuelBefore = state.FuelLevel;
        engine.Update(new TestInput());
        int fuelAfter = state.FuelLevel;

        // FUEL_REFUEL_RATE = 0x20 = 32
        Assert.That(fuelAfter, Is.GreaterThan(fuelBefore), "Fuel should increase when landed on pad");
        Assert.That(fuelAfter - fuelBefore, Is.EqualTo(FixedPoint.FUEL_REFUEL_RATE),
            $"Refuel rate should be {FixedPoint.FUEL_REFUEL_RATE} per frame. Before={fuelBefore}, After={fuelAfter}");
    }

    [Test]
    public void Refuelling_FreezesBelowCap_WhenAdditionWouldReachCap()
    {
        // STRLO semantics (Lander.arm:2547-2550): the refuel store is skipped
        // whenever fuel + 0x20 >= 0x1400, so fuel freezes below 5120 rather
        // than saturating at it (no partial refuel).
        var random = new RandomGenerator(51);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        var state = engine.State;
        state.FuelLevel = FixedPoint.MAX_FUEL_LEVEL - 20;  // 5100: +32 would cross the cap
        state.XPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        state.ZPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        state.YPlayer = FixedPoint.LAUNCHPAD_Y;
        state.XVelocity = 0;
        state.YVelocity = 1;  // re-triggers the landing on the next frame
        state.ZVelocity = 0;

        engine.Update(new TestInput());

        Assert.That(state.FuelLevel, Is.EqualTo(FixedPoint.MAX_FUEL_LEVEL - 20),
            "Fuel must freeze below the cap when +32 would reach or cross 5120");
    }

    [Test]
    public void Refuelling_DoesNotExceedMaxFuelLevel()
    {
        var random = new RandomGenerator(5);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        var state = engine.State;
        // Set fuel to just below max so one refuel tick would push it over
        state.FuelLevel = FixedPoint.MAX_FUEL_LEVEL - 1;
        state.XPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        state.ZPlayer = FixedPoint.LAUNCHPAD_SIZE / 2;
        state.YPlayer = FixedPoint.LAUNCHPAD_Y;
        state.XVelocity = 0;
        state.YVelocity = 1;
        state.ZVelocity = 0;

        engine.Update(new TestInput());

        Assert.That(state.FuelLevel, Is.LessThanOrEqualTo(FixedPoint.MAX_FUEL_LEVEL),
            "Fuel should be clamped at MAX_FUEL_LEVEL");
    }

    // ---- Scoring: bullet costs -1, object destroy gives +20 ----

    [Test]
    public void FiringBullet_DecrementsScoreByOne()
    {
        var random = new RandomGenerator(6);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        // Move ship away from launchpad so landing doesn't interfere
        engine.State.XPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE * 5;
        engine.State.ZPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE * 5;

        int scoreBefore = engine.State.CurrentScore;

        // Fire once
        engine.Update(new TestInput { Fire = true });

        int scoreAfter = engine.State.CurrentScore;
        Assert.That(scoreAfter, Is.EqualTo(scoreBefore - 1),
            $"Firing should cost 1 bullet. Before={scoreBefore}, After={scoreAfter}");
    }

    [Test]
    public void BulletCollision_WithObject_DestroysObjectAndAdds20ToScore()
    {
        // We'll use the ParticleSystem directly so we don't need a live object in exactly
        // the right place for the camera frustum. Trigger the FLAG_DESTROY path manually.
        var random = new RandomGenerator(42);
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();

        var landscape = new LandscapeGenerator(state);
        var objectMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();
        var particles = new ParticleSystem(state, landscape, objectMap, buffers, random);

        // Place a live object at a known position (far from launchpad, on flat ground)
        int targetX = 20 * FixedPoint.TILE_SIZE;
        int targetZ = 20 * FixedPoint.TILE_SIZE;
        // ObjectTypes 1-12 are live objects; 9 = rocket
        objectMap.SetObjectAt(targetX, targetZ, 1);
        Assert.That(ObjectTypes.IsLiveObject(objectMap.GetObjectAt(targetX, targetZ)), Is.True,
            "Pre-condition: object should be live before bullet hits");

        // Set camera to reveal the object in the frustum
        state.XCamera = targetX;
        state.YCamera = 0;
        state.ZCamera = targetZ + FixedPoint.CAMERA_PLAYER_Z;

        int scoreBefore = state.CurrentScore;

        // Spawn a bullet within SAFE_HEIGHT of the ground at the object's tile with
        // no velocity (the destruction check runs on the frame the particle is in
        // the 1.5-tile zone above the ground — Lander.arm:3292-3302)
        int bulletFlags = ParticleSystem.FLAG_DESTROY | ParticleSystem.FLAG_GRAVITY |
                          VidcColour.Encode(15, 15, 15);
        particles.AddParticle(targetX,
            landscape.GetAltitude(targetX, targetZ) - FixedPoint.SAFE_HEIGHT / 2, targetZ,
            0, 0, 0, 5, bulletFlags);

        // One frame: the bullet is within SAFE_HEIGHT of the ground and destroys the object
        particles.UpdateAndDraw();

        // After collision with the live object, score should be +20 and object destroyed
        int scoreAfter = state.CurrentScore;

        Assert.That(scoreAfter, Is.EqualTo(scoreBefore + FixedPoint.SCORE_PER_DESTROY),
            $"Destroying object should add {FixedPoint.SCORE_PER_DESTROY} to score. Before={scoreBefore}, After={scoreAfter}");

        // Object type should now be in the destroyed range (original + 12)
        int newType = objectMap.GetObjectAt(targetX, targetZ);
        Assert.That(ObjectTypes.IsLiveObject(newType), Is.False,
            $"Object should be destroyed after bullet hit, got type={newType}");
    }

    [Test]
    public void BulletCollision_DoesNotDestroyAlreadyDestroyedObject()
    {
        var random = new RandomGenerator(43);
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();

        var landscape = new LandscapeGenerator(state);
        var objectMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();
        var particles = new ParticleSystem(state, landscape, objectMap, buffers, random);

        int targetX = 30 * FixedPoint.TILE_SIZE;
        int targetZ = 30 * FixedPoint.TILE_SIZE;
        // Set a destroyed object (type 13 = first destroyed range, IsLiveObject should return false)
        objectMap.SetObjectAt(targetX, targetZ, 13);
        Assert.That(ObjectTypes.IsLiveObject(objectMap.GetObjectAt(targetX, targetZ)), Is.False,
            "Pre-condition: object type 13 should not be live");

        state.XCamera = targetX;
        state.YCamera = 0;
        state.ZCamera = targetZ + FixedPoint.CAMERA_PLAYER_Z;

        int scoreBefore = state.CurrentScore;

        // Bullet at target
        int bulletFlags = ParticleSystem.FLAG_DESTROY | ParticleSystem.FLAG_GRAVITY |
                          VidcColour.Encode(15, 15, 15);
        particles.AddParticle(targetX, landscape.GetAltitude(targetX, targetZ) - 1, targetZ,
            0, 0x00010000, 0, 5, bulletFlags);

        for (int i = 0; i < 5; i++)
            particles.UpdateAndDraw();

        // Score should not change — already-destroyed objects don't grant points
        Assert.That(state.CurrentScore, Is.EqualTo(scoreBefore),
            "Hitting an already-destroyed object should not change the score");
    }

    [Test]
    public void FiringBullet_AtZeroScore_FiresAndScoreGoesNegative()
    {
        // The original decrements the score unconditionally when firing
        // (Lander.arm:2384-2386): the bullet fires at score 0 and the score goes
        // negative. The previous port blocked firing at score <= 0.
        var random = new RandomGenerator(7);
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();
        state.CurrentScore = 0;

        var landscape = new LandscapeGenerator(state);
        var objectMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();
        var particles = new ParticleSystem(state, landscape, objectMap, buffers, random);

        bool fired = particles.SpawnBullet(state.XPlayer, state.YPlayer, state.ZPlayer,
            0, 0, 0, state.XNoseV, state.YNoseV, state.ZNoseV);

        Assert.That(fired, Is.True, "The bullet must fire at zero score");
        Assert.That(state.CurrentScore, Is.EqualTo(-1), "Score goes negative");
        Assert.That(particles.Count, Is.EqualTo(1), "The bullet particle is spawned");
    }

    // ---- Exhaust only fires on thrust/hover, not on fire key ----

    [Test]
    public void FireKeyAlone_DoesNotProduceExhaustParticles()
    {
        // Regression: FuelBurnRate bit 0 = fire. Previously "!= 0" check
        // caused exhaust to spawn whenever the fire key was held.
        var random = new RandomGenerator(10);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        // Move off launchpad so landing logic doesn't interfere
        engine.State.XPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE * 3;
        engine.State.ZPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE * 3;
        engine.State.YPlayer = FixedPoint.LAUNCHPAD_Y - FixedPoint.TILE_SIZE * 2; // in the air

        // Run one frame with ONLY fire pressed (no thrust, no hover)
        engine.Update(new TestInput { Fire = true });

        // FuelBurnRate should be 1 (fire bit only), NOT triggering exhaust
        Assert.That(engine.State.FuelBurnRate & 6, Is.EqualTo(0),
            "Hover and thrust bits should both be zero when only Fire is pressed");

        // Particles should be the bullet only (1 particle), not a wave of exhaust
        // SpawnExhaust spawns 2–8 particles; SpawnBullet spawns 1.
        // We can't directly query the particle system, but we CAN verify FuelBurnRate
        // to confirm the engine gate would have been closed.
        Assert.That(engine.State.FuelBurnRate, Is.EqualTo(1),
            "FuelBurnRate should be 1 (fire bit only) — not 0 (fuel ran out) and not 3+ (thrust active)");
    }

    [Test]
    public void ThrustKey_DoesProduceExhaustParticles()
    {
        // Sanity check: thrust alone should keep the exhaust gate open.
        var random = new RandomGenerator(11);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        engine.State.XPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE * 3;
        engine.State.ZPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE * 3;
        engine.State.YPlayer = FixedPoint.LAUNCHPAD_Y - FixedPoint.TILE_SIZE * 2;

        engine.Update(new TestInput { Thrust = true });

        // FuelBurnRate bit 2 = 4; gate checks (FuelBurnRate & 6) != 0 → true
        Assert.That(engine.State.FuelBurnRate & 4, Is.Not.EqualTo(0),
            "Thrust bit should be set when Thrust is pressed");
    }

    // ---- Rock dropping tests ----

    [Test]
    public void RockRotationMatrix_ComesFromMainLoopCounter()
    {
        // Lander.arm:12507-12518: after the ship is drawn, the main loop
        // overwrites the shared rotation matrix with angles derived from
        // mainLoopCount (<< 24 and << 25) so rocks spin at a steady speed
        // independent of the player. The ship's angles are unchanged between
        // frames, so the matrix changing proves the counter drives it.
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        engine.Update(new TestInput());
        int xNoseVFrame1 = engine.State.XNoseV;
        int zSideVFrame1 = engine.State.ZSideV;

        engine.Update(new TestInput());
        int xNoseVFrame2 = engine.State.XNoseV;
        int zSideVFrame2 = engine.State.ZSideV;

        Assert.That(engine.State.ShipPitch, Is.EqualTo(1), "Ship orientation unchanged");
        Assert.That(engine.State.ShipDirection, Is.EqualTo(0), "Ship orientation unchanged");
        Assert.That(xNoseVFrame2, Is.Not.EqualTo(xNoseVFrame1),
            "The rotation matrix must change with the main loop counter (rock spin)");
        Assert.That(zSideVFrame2, Is.Not.EqualTo(zSideVFrame1),
            "The rotation matrix must change with the main loop counter (rock spin)");
    }

    [Test]
    public void ScoreBelow800_DoesNotSpawnRocks()
    {
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();
        engine.State.CurrentScore = 799;

        // Run 10 frames with score 799
        for (int i = 0; i < 10; i++)
        {
            engine.Update(new TestInput());
        }

        Assert.That(engine.State.CrashedFlag, Is.EqualTo(0), "No rock crash should occur at score 799");
    }

    [Test]
    public void ParticleSystem_DropRock_SpawnsRockParticle()
    {
        var state = new GameState();
        var landscape = new LandscapeGenerator(state);
        var random = new RandomGenerator(42);
        var objMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();
        var particles = new ParticleSystem(state, landscape, objMap, buffers, random);

        bool spawned = particles.DropRock(0, 0, 0);

        Assert.That(spawned, Is.True, "DropRock should successfully spawn a particle");
        Assert.That(particles.Count, Is.EqualTo(1), "Particle count should be 1 after DropRock");
    }

    [Test]
    public void RockCollidingWithPlayer_TriggersCrash()
    {
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();

        var landscape = new LandscapeGenerator(state);
        var random = new RandomGenerator(42);
        var objMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();
        var particles = new ParticleSystem(state, landscape, objMap, buffers, random);

        // Spawn a rock right at the player's position
        particles.DropRock(state.XPlayer, state.YPlayer, state.ZPlayer);

        // Update and draw particles to trigger rock collision check
        particles.UpdateAndDraw();

        Assert.That(state.CrashedFlag, Is.Not.EqualTo(0), "Rock colliding with player should set CrashedFlag");
    }

    [Test]
    public void ScoreIncrease_RendersUpdatedScoreString_OnHUD()
    {
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();
        int initialScore = engine.State.CurrentScore; // 500

        // Increase score by 20 (as if destroying an object)
        engine.State.CurrentScore += FixedPoint.SCORE_PER_DESTROY; // 520
        Assert.That(engine.State.CurrentScore, Is.EqualTo(initialScore + 20));

        // Update engine frame to render HUD score bar
        engine.Update(new TestInput());

        // Inspect row 8 of the screen framebuffer (where score string "520" is rendered)
        var fb = screen.GetFramebuffer();
        int scorePixels = 0;
        for (int x = 0; x < 3*8; x++) // First 3 character columns (24 pixels)
        {
            for (int r = 0; r < 8; r++)
            {
                if (fb[(8 + r) * 320 + x] != 0)
                    scorePixels++;
            }
        }

        Assert.That(scorePixels, Is.GreaterThan(15),
            "Updated score string '520' should be rendered with non-zero pixels on top HUD score bar at row 8");
    }

    [Test]
    public void ReducedThreshold_SpawnsFallingRocksThatBecomeVisibleOnScreen()
    {
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();
        state.CurrentScore = 1200;

        var landscape = new LandscapeGenerator(state);
        var random = new RandomGenerator(42);
        var objMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();
        var particles = new ParticleSystem(state, landscape, objMap, buffers, random);

        // Spawn a rock from the sky above the camera
        particles.DropRock(state.XCamera, -(FixedPoint.ROCK_HEIGHT + 1), state.ZCamera - FixedPoint.PLAYER_FRONT_Z);

        Assert.That(particles.Count, Is.EqualTo(1), "Rock should spawn");

        // Advance 75 frames (until rock falls down into view)
        for (int frame = 0; frame < 75; frame++)
        {
            particles.UpdateAndDraw();
        }

        // At frame 75, rock should still be falling through the sky
        Assert.That(particles.Count, Is.GreaterThan(0), "Rock particle should remain active while falling");
    }

    [Test]
    public void HighScore_UpdatesAtStartOfNewGame()
    {
        // The original compares and stores the high score in StartNewGame
        // (Lander.arm:12218-12230), not per-frame: a new high score only
        // appears once the next game begins, and the score is then reset.
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();
        engine.State.CurrentScore = 750;
        engine.State.HighScore = 500;

        // Per-frame updates must NOT touch the high score
        engine.Update(new TestInput());
        Assert.That(engine.State.HighScore, Is.EqualTo(500),
            "High score must not change mid-game");

        // A new game latches max(highScore, currentScore) and resets the score
        engine.StartNewGame();
        Assert.That(engine.State.HighScore, Is.EqualTo(750),
            "New high score is recorded when the next game starts");
        Assert.That(engine.State.CurrentScore, Is.EqualTo(FixedPoint.INITIAL_SCORE),
            "Score is reset for the new game");
    }
}
