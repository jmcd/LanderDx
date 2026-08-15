using LanderDx.Core.Engine;
using LanderDx.Core.Interfaces;
using LanderDx.Core.Math;
using LanderDx.Core.Data;

namespace LanderDx.Tests;

/// <summary>
/// Diagnostic tests to uncover why the player ship, objects, and scene updates are not rendering.
/// </summary>
[TestFixture]
public class DiagnosticTests
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

    // ---- Player ship rendering ----

    [Test]
    public void ShipVertices_ProjectToScreen_AfterUpdate()
    {
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var input = new TestInput();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();
        engine.Update(input);

        var state = engine.State;
        // Ship is at z = LANDSCAPE_Z_MID = 15 tiles
        int objZ = FixedPoint.LANDSCAPE_Z_MID;
        int objX = 0;
        int objY = state.YPlayer - state.YCamera;

        // Project each vertex of the ship
        var ship = ObjectBlueprints.PlayerShip;
        int onScreenVertices = 0;
        int totalVertices = ship.VertexCount;

        for (int v = 0; v < totalVertices; v++)
        {
            var vert = ship.Vertices[v];

            // Rotate vertex by ship's rotation matrix
            int rvx = (int)(((long)vert.X * state.XNoseV + (long)vert.Y * state.YNoseV + (long)vert.Z * state.ZNoseV) >> 31);
            int rvy = (int)(((long)vert.X * state.XRoofV + (long)vert.Y * state.YRoofV + (long)vert.Z * state.ZRoofV) >> 31);
            int rvz = (int)(((long)vert.X * state.XSideV + (long)vert.Y * state.YSideV + (long)vert.Z * state.ZSideV) >> 31);

            int wx = rvx + objX;
            int wy = rvy + objY;
            int wz = rvz + objZ;

            if (Projection.Project(wx, wy, wz, out int sx, out int sy))
            {
                if (Projection.IsOnScreen(sx, sy))
                {
                    onScreenVertices++;
                    TestContext.WriteLine($"Vertex {v}: world=({wx:X8},{wy:X8},{wz:X8}) screen=({sx},{sy})");
                }
                else
                {
                    TestContext.WriteLine($"Vertex {v}: OFF SCREEN screen=({sx},{sy})");
                }
            }
            else
            {
                TestContext.WriteLine($"Vertex {v}: BEHIND CAMERA world=({wx:X8},{wy:X8},{wz:X8})");
            }
        }

        Assert.That(onScreenVertices, Is.GreaterThan(0),
            $"No ship vertices on screen! {totalVertices} total, 0 visible");
        Assert.That(onScreenVertices, Is.GreaterThan(2),
            $"Only {onScreenVertices} of {totalVertices} ship vertices visible — not enough for a triangle");
    }

    [Test]
    public void ShipRotationMatrix_IsSet_AfterUpdate()
    {
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var input = new TestInput { YawRight = true, PitchUp = true }; // Off-center mouse
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();
        engine.Update(input);

        var state = engine.State;
        // Rotation matrix should be non-trivial after mouse input
        bool hasNonZero = state.XNoseV != 0 || state.XRoofV != 0 || state.XSideV != 0
                       || state.YNoseV != 0 || state.YRoofV != 0 || state.YSideV != 0
                       || state.ZNoseV != 0 || state.ZRoofV != 0 || state.ZSideV != 0;

        Assert.That(hasNonZero, Is.True, "Rotation matrix is all zeros");
        TestContext.WriteLine($"Matrix: [{state.XNoseV:X8} {state.XRoofV:X8} {state.XSideV:X8}]");
        TestContext.WriteLine($"        [{state.YNoseV:X8} {state.YRoofV:X8} {state.YSideV:X8}]");
        TestContext.WriteLine($"        [{state.ZNoseV:X8} {state.ZRoofV:X8} {state.ZSideV:X8}]");
    }

    // ---- Graphics buffer inspection ----

    [Test]
    public void GraphicsBuffers_ContainData_AfterFrame()
    {
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var input = new TestInput();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();

        // Access the private buffers field via reflection or make it internal
        // For now, we'll verify indirectly: if DrawLandscapeAndBuffers ran,
        // the framebuffer should have non-black pixels
        engine.Update(input);

        // Check landscape in lower portion of screen
        int landscapePixels = 0;
        for (int y = 150; y < 238; y++)
            for (int x = 10; x < 310; x++)
                if (screen.GetPlayPixel(x, y) != 0)
                    landscapePixels++;

        TestContext.WriteLine($"Landscape pixels (rows 150-237): {landscapePixels}");

        // Check center of screen for ship
        int centerPixels = 0;
        for (int y = 80; y < 140; y++)
            for (int x = 140; x < 180; x++)
                if (screen.GetPlayPixel(x, y) != 0)
                    centerPixels++;

        TestContext.WriteLine($"Center pixels (area 140-180, 80-140): {centerPixels}");

        Assert.That(landscapePixels, Is.GreaterThan(100),
            $"Landscape should have visible pixels, got {landscapePixels}");
    }

    // ---- Object map visibility ----

    [Test]
    public void ObjectsExist_SomewhereOnMap()
    {
        var random = new RandomGenerator(12345);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();

        // Scan the whole 256×256 map for objects
        int objectsFound = 0;
        var typesFound = new HashSet<int>();
        for (int tz = 0; tz < 256; tz++)
        {
            for (int tx = 0; tx < 256; tx++)
            {
                int worldX = tx * FixedPoint.TILE_SIZE;
                int worldZ = tz * FixedPoint.TILE_SIZE;
                int objType = engine.ObjectMap.GetObjectAt(worldX, worldZ);
                if (objType != ObjectTypes.NO_OBJECT)
                {
                    objectsFound++;
                    typesFound.Add(objType);
                    if (objectsFound <= 5)
                        TestContext.WriteLine($"Object type {objType} at tile ({tx},{tz})");
                }
            }
        }

        TestContext.WriteLine($"Total objects on map: {objectsFound}");
        TestContext.WriteLine($"Unique types: {string.Join(", ", typesFound)}");
        Assert.That(objectsFound, Is.GreaterThan(0),
            "No objects found anywhere on the 256×256 map");
        Assert.That(objectsFound, Is.GreaterThan(10),
            $"Only {objectsFound} objects — PRNG cycles every ~64 values, so ~34 non-sea objects expected");
    }

    [Test]
    public void ObjectBlueprint_HasVertices_ForEachType()
    {
        // Verify every object type that could appear has valid vertex data
        for (int t = 1; t <= 12; t++)
        {
            var bp = ObjectTypes.GetBlueprint(t);
            Assert.That(bp, Is.Not.Null, $"Type {t} has no blueprint");
            Assert.That(bp.VertexCount, Is.GreaterThan(0), $"Type {t} has 0 vertices");
            Assert.That(bp.FaceCount, Is.GreaterThan(0), $"Type {t} has 0 faces");
        }
    }

    [Test]
    public void ObjectVertices_ProjectToScreen_ForAnyObject()
    {
        var random = new RandomGenerator(12345);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();

        var state = engine.State;
        int projectedObjects = 0;
        int projectedFaces = 0;

        // Scan the whole map for the first few objects we can find
        for (int tz = 0; tz < 256 && projectedObjects < 5; tz++)
        {
            for (int tx = 0; tx < 256 && projectedObjects < 5; tx++)
            {
                int worldX = tx * FixedPoint.TILE_SIZE;
                int worldZ = tz * FixedPoint.TILE_SIZE;
                int objType = engine.ObjectMap.GetObjectAt(worldX, worldZ);
                if (objType == ObjectTypes.NO_OBJECT) continue;

                var bp = ObjectTypes.GetBlueprint(objType);
                if (bp == null) continue;

                // Move camera to object position so it is in view
                state.XCamera = worldX;
                state.ZCamera = worldZ + FixedPoint.CAMERA_PLAYER_Z;

                int objX = worldX - state.XCamera;
                int objZ = FixedPoint.LANDSCAPE_Z - state.ZCamera + worldZ;
                int objY = engine.Landscape.GetAltitude(worldX, worldZ) - state.YCamera;

                projectedObjects++;

                foreach (var face in bp.Faces)
                {
                    var v1 = bp.Vertices[face.V1];
                    var v2 = bp.Vertices[face.V2];
                    var v3 = bp.Vertices[face.V3];

                    bool v1ok = Projection.Project(objX + v1.X, objY + v1.Y, objZ + v1.Z, out int sx1, out int sy1);
                    bool v2ok = Projection.Project(objX + v2.X, objY + v2.Y, objZ + v2.Z, out int sx2, out int sy2);
                    bool v3ok = Projection.Project(objX + v3.X, objY + v3.Y, objZ + v3.Z, out int sx3, out int sy3);

                    if (v1ok && v2ok && v3ok && Projection.IsOnScreen(sx1, sy1))
                    {
                        projectedFaces++;
                    }
                }
            }
        }

        TestContext.WriteLine($"Projected objects: {projectedObjects}, projected faces: {projectedFaces}");
        Assert.That(projectedFaces, Is.GreaterThan(0),
            $"No object faces projected. Found {projectedObjects} objects but 0 faces on screen.");
    }

    // ---- Game loop state ----

    [Test]
    public void MainLoopCount_Increments()
    {
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var input = new TestInput();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();
        int count1 = engine.State.MainLoopCount;
        engine.Update(input);
        int count2 = engine.State.MainLoopCount;

        Assert.That(count2, Is.EqualTo(count1 + 1), "MainLoopCount should increment each frame");
    }

    [Test]
    public void PlayerPosition_Changes_WithThrustInput()
    {
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var input = new TestInput { Thrust = true, PitchUp = true };  // Thrust + pitch up
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();
        // Move off launchpad so we don't get stuck landing
        engine.State.XPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;
        engine.State.ZPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;

        int yBefore = engine.State.YPlayer;
        int zBefore = engine.State.ZPlayer;
        int xBefore = engine.State.XPlayer;

        // Run several frames with thrust
        for (int i = 0; i < 10; i++)
            engine.Update(input);

        int yAfter = engine.State.YPlayer;
        int zAfter = engine.State.ZPlayer;
        int xAfter = engine.State.XPlayer;

        TestContext.WriteLine($"Y: {yBefore:X8} → {yAfter:X8}");
        TestContext.WriteLine($"X: {xBefore:X8} → {xAfter:X8}");
        TestContext.WriteLine($"Z: {zBefore:X8} → {zAfter:X8}");

        // At least one coordinate should change (thrust + gravity + friction)
        bool moved = yAfter != yBefore || xAfter != xBefore || zAfter != zBefore;
        Assert.That(moved, Is.True, "Ship did not move at all after 10 frames of thrust");
    }

    // ---- Fuel bar color ----

    [Test]
    public void FuelBar_VisiblePixels_HaveOrangeTint()
    {
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var input = new TestInput();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();
        engine.Update(input);

        // The fuel bar now sits at the top of the play area: rows 17-19 in
        // fuelBarColour &37373737 (Lander.arm:5884-5954, 5829-5831)
        var fb = screen.GetFramebuffer();
        var colorCounts = new Dictionary<byte, int>();
        for (int y = 17; y <= 19; y++)
            for (int x = 0; x < 320; x++)
            {
                byte c = fb[y * 320 + x];
                if (c != 0)
                {
                    colorCounts.TryGetValue(c, out int cnt);
                    colorCounts[c] = cnt + 1;
                }
            }

        TestContext.WriteLine($"Colors in fuel bar rows: {string.Join(", ", colorCounts.Select(kv => $"0x{kv.Key:X2}={kv.Value}"))}");

        int totalPixels = colorCounts.Values.Sum();
        Assert.That(totalPixels, Is.GreaterThan(0), "Fuel bar rows should have non-black pixels");
        Assert.That(colorCounts.ContainsKey(0x37), Is.True,
            "The bar must use the ROM's &37373737 colour byte");
    }

    [Test]
    public void VidcColour_Orange_EncodesCorrectly()
    {
        // Fuel bar orange: R=12, G=8, B=0
        byte vidc = VidcColour.Encode(12, 8, 0);
        var (r, g, b) = VidcColour.DecodeToRgb24(vidc);
        TestContext.WriteLine($"Orange(12,8,0): VIDC=0x{vidc:X2}, decoded R={r} G={g} B={b}");

        // Should be distinctly orange (red-ish, some green, no blue)
        Assert.That(r, Is.GreaterThan(g), "Orange should have more red than green");
        Assert.That(g, Is.GreaterThan(b), "Orange should have some green, no blue");

        // Green bar: R=0, G=12, B=0
        byte vidcGreen = VidcColour.Encode(0, 12, 0);
        var (rg, gg, bg) = VidcColour.DecodeToRgb24(vidcGreen);
        TestContext.WriteLine($"Green(0,12,0): VIDC=0x{vidcGreen:X2}, decoded R={rg} G={gg} B={bg}");
        Assert.That(gg, Is.GreaterThan(rg), "Green should be primarily green");
    }

    // ---- Render coverage ----

    [Test]
    public void Framebuffer_HasContent_InMultipleRegions()
    {
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var input = new TestInput();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();
        engine.Update(input);

        // Sample different regions and report pixel counts
        var regions = new Dictionary<string, (int x1, int y1, int x2, int y2)>
        {
            ["Top bar (0-15px)"] = (0, 0, 319, 3),
            ["Top-center (80-160px)"] = (120, 20, 200, 100),
            ["Center (ship area)"] = (130, 80, 190, 140),
            ["Bottom-left (landscape)"] = (10, 150, 150, 235),
            ["Bottom-right (landscape)"] = (160, 150, 310, 235),
        };

        foreach (var (name, rect) in regions)
        {
            int pixels = 0;
            byte minColor = 255, maxColor = 0;
            for (int y = rect.y1; y <= rect.y2; y++)
            {
                for (int x = rect.x1; x <= rect.x2; x++)
                {
                    byte c = screen.GetPlayPixel(x, y);
                    if (c != 0)
                    {
                        pixels++;
                        if (c < minColor) minColor = c;
                        if (c > maxColor) maxColor = c;
                    }
                }
            }
            TestContext.WriteLine($"{name}: {pixels} non-black pixels (colors 0x{minColor:X2}-0x{maxColor:X2})");
        }
    }
}
