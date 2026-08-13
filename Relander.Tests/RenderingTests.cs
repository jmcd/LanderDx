using Relander.Core.Data;
using Relander.Core.Engine;
using Relander.Core.Math;

namespace Relander.Tests;

[TestFixture]
public class RenderingTests
{
    private byte[] _framebuffer = null!;
    private TriangleRasterizer _rasterizer = null!;

    [SetUp]
    public void Setup()
    {
        _framebuffer = new byte[320 * 240];
        _rasterizer = new TriangleRasterizer(_framebuffer);
    }

    // ---- Triangle rasterizer tests ----

    [Test]
    public void Clear_FillsWithColor()
    {
        _rasterizer.Clear(42);
        Assert.That(_framebuffer[0], Is.EqualTo(42));
        Assert.That(_framebuffer[^1], Is.EqualTo(42));
        Assert.That(_framebuffer.All(b => b == 42), Is.True);
    }

    [Test]
    public void DrawTriangle_DrawsPixels()
    {
        // A big triangle in the center of the screen
        _rasterizer.Clear(0);
        _rasterizer.DrawTriangle(160, 20, 60, 220, 260, 220, 128);

        // Center pixel should be filled
        int centerIdx = 120 * 320 + 160;
        Assert.That(_framebuffer[centerIdx], Is.EqualTo(128),
            "Center pixel should be colored");

        // Count colored pixels
        int colored = _framebuffer.Count(b => b == 128);
        Assert.That(colored, Is.GreaterThan(100),
            $"Should have >100 colored pixels, got {colored}");
    }

    [Test]
    public void DrawTriangle_ClipsToBounds()
    {
        _rasterizer.Clear(0);
        // Triangle partially off-screen
        _rasterizer.DrawTriangle(-10, -10, 400, 50, 160, 300, 64);

        // Should not crash and should draw within bounds
        for (int x = 0; x < 320; x++)
        {
            for (int y = 0; y < 240; y++)
            {
                byte b = _framebuffer[y * 320 + x];
                Assert.That(b, Is.AnyOf((byte)0, (byte)64),
                    $"Pixel at ({x},{y}) = {b}, expected 0 or 64");
            }
        }
    }

    [Test]
    public void DrawTriangle_OffScreen_DoesNotCrash()
    {
        // Entirely off-screen triangle
        Assert.DoesNotThrow(() =>
            _rasterizer.DrawTriangle(-100, -100, -50, -50, -75, -25, 255));
    }

    [Test]
    public void DrawTriangle_Degenerate_DoesNotCrash()
    {
        // All three vertices at same point
        Assert.DoesNotThrow(() =>
            _rasterizer.DrawTriangle(100, 100, 100, 100, 100, 100, 255));
    }

    [Test]
    public void DrawTriangle_HorizontalEdge()
    {
        _rasterizer.Clear(0);
        // Flat-bottomed triangle
        _rasterizer.DrawTriangle(100, 100, 200, 100, 150, 50, 200);

        // Top vertex pixel
        Assert.That(_framebuffer[50 * 320 + 150], Is.EqualTo(200));
    }

    // ---- Graphics buffer tests ----

    [Test]
    public void GraphicsBuffer_AddTriangle_CanRetrieve()
    {
        var buffers = new GraphicsBuffers(count: 4, capacity: 1024);
        buffers.AddTriangle(0, 10, 20, 30, 40, 50, 60, 0x12345678);
        buffers.AddTerminators();

        var data = buffers.GetBufferData(0);
        Assert.That(data.Length, Is.GreaterThanOrEqualTo(8));
        Assert.That(data[0], Is.EqualTo(GraphicsBuffers.COMMAND_TRIANGLE));
        Assert.That(data[1], Is.EqualTo(10));  // x1
        Assert.That(data[2], Is.EqualTo(20));  // y1
        Assert.That(data[3], Is.EqualTo(30));  // x2
        Assert.That(data[4], Is.EqualTo(40));  // y2
        Assert.That(data[5], Is.EqualTo(50));  // x3
        Assert.That(data[6], Is.EqualTo(60));  // y3
        Assert.That(data[7], Is.EqualTo(0x12345678));  // colour
    }

    [Test]
    public void GraphicsBuffer_AddTerminator_StopsReading()
    {
        var buffers = new GraphicsBuffers(count: 4, capacity: 1024);
        buffers.AddTriangle(0, 0, 0, 0, 0, 0, 0, 0);
        buffers.AddTerminators(); // Writes terminator, resets end for next frame

        // Data is readable up to the terminator
        var data = buffers.GetBufferData(0);
        Assert.That(data.Length, Is.EqualTo(8), "Should read triangle data up to terminator");

        // After clearing, buffer should be empty
        buffers.Clear();
        var empty = buffers.GetBufferData(0);
        Assert.That(empty.Length, Is.EqualTo(0), "Buffer should be empty after Clear");
    }

    [Test]
    public void GraphicsBuffer_AddParticle_FormatsCorrectly()
    {
        var buffers = new GraphicsBuffers(count: 4, capacity: 1024);
        buffers.AddParticle(2, 3, 100, 200, 0xAB); // cmd=3, x=100, y=200, colour=0xAB
        buffers.AddTerminators();

        var data = buffers.GetBufferData(2);
        Assert.That(data.Length, Is.GreaterThanOrEqualTo(2));
        Assert.That(data[0], Is.EqualTo(3));  // command

        int packed = data[1];
        int px = (packed >> 20) & 0xFFF;
        int py = packed & 0xFF;
        int col = (packed >> 12) & 0xFF;
        Assert.That(px, Is.EqualTo(100));
        Assert.That(py, Is.EqualTo(200));
        Assert.That(col, Is.EqualTo(0xAB));
    }

    [Test]
    public void GraphicsBuffer_MultipleBuffers_Independent()
    {
        var buffers = new GraphicsBuffers(count: 4, capacity: 1024);
        buffers.AddTriangle(0, 1, 2, 3, 4, 5, 6, 0x11111111);
        buffers.AddTriangle(2, 7, 8, 9, 10, 11, 12, 0x22222222);
        buffers.AddTerminators();

        var d0 = buffers.GetBufferData(0);
        var d2 = buffers.GetBufferData(2);
        Assert.That(d0.Length, Is.GreaterThan(0), "Buffer 0 should have data");
        Assert.That(d2.Length, Is.GreaterThan(0), "Buffer 2 should have data");
        Assert.That(d0[7], Is.EqualTo(0x11111111), "Buffer 0 colour");
        Assert.That(d2[7], Is.EqualTo(0x22222222), "Buffer 2 colour");
    }

    // ---- VIDC colour tests ----

    [Test]
    public void VidcColour_EncodeDecode_UpperBitsRoundtrip()
    {
        // VIDC bits 0-1 are shared (OR of all channels) — lossy for individual channels.
        // Bits 2-3 are per-channel and should survive encoding/decoding.
        for (int r = 0; r < 16; r++)
        {
            for (int g = 0; g < 16; g++)
            {
                for (int b = 0; b < 16; b++)
                {
                    byte vidc = VidcColour.Encode(r, g, b);
                    var (dr, dg, db) = VidcColour.DecodeToRgb24(vidc);

                    // Check upper 2 bits (bits 2-3) which are per-channel
                    int rUpper = (r >> 2) & 3;
                    int gUpper = (g >> 2) & 3;
                    int bUpper = (b >> 2) & 3;
                    int drUpper = (dr >> 6) & 3;
                    int dgUpper = (dg >> 6) & 3;
                    int dbUpper = (db >> 6) & 3;

                    Assert.That(drUpper, Is.EqualTo(rUpper),
                        $"Red upper bits: enc({r},{g},{b}) → R upper={drUpper}, expected {rUpper}");
                    Assert.That(dgUpper, Is.EqualTo(gUpper),
                        $"Green upper bits: enc({r},{g},{b}) → G upper={dgUpper}, expected {gUpper}");
                    Assert.That(dbUpper, Is.EqualTo(bUpper),
                        $"Blue upper bits: enc({r},{g},{b}) → B upper={dbUpper}, expected {bUpper}");

                    // Lower 2 bits may be combined from all channels — just verify non-negative
                    Assert.That(dr, Is.InRange(0, 255));
                    Assert.That(dg, Is.InRange(0, 255));
                    Assert.That(db, Is.InRange(0, 255));
                }
            }
        }
    }

    [Test]
    public void VidcColour_ReplicateQuad_FillsWord()
    {
        byte vidc = VidcColour.Encode(8, 4, 2);
        int word = VidcColour.ReplicateQuad(vidc);

        byte b0 = (byte)(word & 0xFF);
        byte b1 = (byte)((word >> 8) & 0xFF);
        byte b2 = (byte)((word >> 16) & 0xFF);
        byte b3 = (byte)((word >> 24) & 0xFF);

        Assert.That(b0, Is.EqualTo(vidc));
        Assert.That(b1, Is.EqualTo(vidc));
        Assert.That(b2, Is.EqualTo(vidc));
        Assert.That(b3, Is.EqualTo(vidc));
    }

    [Test]
    public void VidcColour_BuildPalette_256Entries()
    {
        var palette = VidcColour.BuildPalette();
        Assert.That(palette, Has.Length.EqualTo(256));

        // All entries should have alpha = 0xFF (opaque)
        for (int i = 0; i < 256; i++)
        {
            byte alpha = (byte)(palette[i] >> 24);
            Assert.That(alpha, Is.EqualTo(0xFF), $"Palette[{i}] alpha should be 0xFF");
        }
    }

    [Test]
    public void VidcColour_Black_IsZero()
    {
        byte vidc = VidcColour.Encode(0, 0, 0);
        Assert.That(vidc, Is.EqualTo(0));
    }

    [Test]
    public void VidcColour_White_IsFF()
    {
        byte vidc = VidcColour.Encode(15, 15, 15);
        Assert.That(vidc, Is.EqualTo(0xFF));
    }

    // ---- SystemFont tests ----

    [Test]
    public void SystemFont_DrawChar_RendersGlyphPixels()
    {
        Array.Clear(_framebuffer);
        SystemFont.DrawChar(_framebuffer, 320, 10, 10, 'A', 255);

        int coloredCount = 0;
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                if (_framebuffer[(10 + r) * 320 + (10 + c)] == 255)
                    coloredCount++;
            }
        }

        Assert.That(coloredCount, Is.GreaterThan(5), "Character 'A' should render pixels");
    }

    [Test]
    public void SystemFont_DrawString_RendersMultipleChars()
    {
        Array.Clear(_framebuffer);
        SystemFont.DrawString(_framebuffer, 320, 0, 8, "1000", 255);

        int coloredCount = _framebuffer.Count(b => b == 255);
        Assert.That(coloredCount, Is.GreaterThan(20), "String '1000' should render font pixels");
    }

    [Test]
    public void SystemFont_DrawString_ClipsToBounds_DoesNotCrash()
    {
        Array.Clear(_framebuffer);
        Assert.DoesNotThrow(() =>
            SystemFont.DrawString(_framebuffer, 320, 315, 235, "GAME OVER", 255));
    }

    [Test]
    public void GameEngine_RenderScoreBar_RendersHeaderAndFuelBar()
    {
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();
        engine.Update(new TestInput());

        // Check text row 8 of top HUD score bar for bullet score text, lives, and high score
        int textPixels = 0;
        var fb = screen.GetFramebuffer();
        for (int x = 0; x < 320; x++)
        {
            if (fb[8 * 320 + x] != 0) textPixels++;
        }

        Assert.That(textPixels, Is.GreaterThan(10), "Score bar text header should render non-zero text pixels in top HUD area");
    }

    private class TestScreen : Relander.Core.Interfaces.IScreen
    {
        private readonly byte[] _fb = new byte[320 * 256];
        public int Width => 320;
        public int Height => 240;
        public Span<byte> GetFramebuffer() => _fb;
        public void Present() { }
        public void Clear(byte color = 0) => Array.Clear(_fb);
        public byte GetPlayPixel(int x, int y) => _fb[(y + 16) * 320 + x];
    }

    [Test]
    public void MinimapRenderer_RenderInset_DrawsRadarBoxInTopRight()
    {
        MinimapRenderer.InvalidateCache();
        var state = new GameState();
        state.Initialize();
        state.MapMode = 0; // Inset Mini-Map Mode

        var landscape = new LandscapeGenerator(state);
        var random = new RandomGenerator(42);
        var objMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();
        var particles = new ParticleSystem(state, landscape, objMap, buffers, random);

        byte[] fb = new byte[320 * 256];
        MinimapRenderer.Render(fb, 320, state, landscape, objMap, particles);

        byte borderPixel = fb[21 * 320 + 251]; // Top-left corner of border frame (x=251, y=21)
        Assert.That(borderPixel, Is.EqualTo(VidcColour.Encode(15, 15, 15)),
            "Minimap border pixel should be rendered in top-right corner");

        // Verify center map pixel inside inset at x=280, y=54
        byte mapPixel = fb[54 * 320 + 280];
        Assert.That(mapPixel, Is.Not.EqualTo(0),
            "Minimap interior pixel should contain downsampled terrain color");
    }

    [Test]
    public void MinimapRenderer_RenderFull_Draws256MapOverlay()
    {
        var state = new GameState();
        state.Initialize();
        state.MapMode = 1; // Full 256x256 Overlay Mode

        var landscape = new LandscapeGenerator(state);
        var random = new RandomGenerator(42);
        var objMap = new ObjectMap(landscape, random);
        var buffers = new GraphicsBuffers();
        var particles = new ParticleSystem(state, landscape, objMap, buffers, random);

        byte[] fb = new byte[320 * 256];
        MinimapRenderer.Render(fb, 320, state, landscape, objMap, particles);

        // Center map starts at x=32, spans 256 pixels
        byte mapPixel = fb[100 * 320 + (32 + 100)];
        Assert.That(mapPixel, Is.Not.EqualTo(0),
            "Full 256x256 map overlay pixel should contain 1px-per-tile terrain color");
    }

    [Test]
    public void GameEngine_ToggleMapInput_CyclesMapModes()
    {
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);

        engine.StartNewGame();
        Assert.That(engine.State.MapMode, Is.EqualTo(0), "Default MapMode should be 0 (Inset Mini-Map)");

        // Press Tab key (ToggleMap)
        engine.Update(new TestInput { ToggleMap = true });
        Assert.That(engine.State.MapMode, Is.EqualTo(1), "Pressing ToggleMap should switch to Mode 1 (Full Map)");

        // Press Tab key again
        engine.Update(new TestInput { ToggleMap = true });
        Assert.That(engine.State.MapMode, Is.EqualTo(2), "Pressing ToggleMap should switch to Mode 2 (Hidden)");

        // Press Tab key again
        engine.Update(new TestInput { ToggleMap = true });
        Assert.That(engine.State.MapMode, Is.EqualTo(0), "Pressing ToggleMap should wrap back to Mode 0");
    }






    [Test]
    public void StaticObject_ShadowsOnlyForUpFacingNormals()
    {
        // The original draws shadows BEFORE the face-visibility test and only
        // for faces whose rotated normal points up, y < 0 (Lander.arm:5385-5418:
        // CMP R1, #0 / BPL dobj4). The previous code drew a shadow for every
        // drawn face, so down-pointing faces wrongly cast shadows.
        var state = new GameState();
        state.Initialize();
        state.XCamera = 0;
        state.YCamera = 0;
        state.ZCamera = 0;

        var landscape = new LandscapeGenerator(state);
        var buffers = new GraphicsBuffers();

        // SmallLeafyTree: static, has shadow, 5 faces
        var tree = ObjectBlueprints.SmallLeafyTree;
        int upFacing = tree.Faces.Count(f => f.Normal.Y < 0);
        Assert.That(upFacing, Is.InRange(1, tree.FaceCount - 1),
            "Precondition: a mix of up- and down-facing faces");

        ObjectRenderer.DrawObject(tree, 0, 0, FixedPoint.LANDSCAPE_Z_MID, 0, 0, state, buffers, landscape);
        buffers.AddTerminators();

        // Shadow triangles have colour word 0 and live in the shadow buffer
        // (one buffer behind the face buffer). Count triangles with colour 0.
        int faceBuffer = buffers.GetBufferIndex(FixedPoint.LANDSCAPE_Z_MID);
        int shadowBuffer = buffers.GetShadowBufferIndex(FixedPoint.LANDSCAPE_Z_MID);
        var shadowData = buffers.GetBufferData(shadowBuffer);
        int shadowTriangles = 0;
        for (int i = 0; i < shadowData.Length; i += 8)
        {
            if (shadowData[i] != GraphicsBuffers.COMMAND_TRIANGLE) break;
            shadowTriangles++;
        }

        Assert.That(shadowTriangles, Is.EqualTo(upFacing),
            $"Only up-facing faces cast shadows: {upFacing} expected, {shadowTriangles} drawn");
        Assert.That(buffers.GetBufferData(faceBuffer).Length, Is.GreaterThan(0),
            "The tree's faces themselves are still drawn");
    }

    [Test]
    public void FaceShading_IsNotClampedToThree()
    {
        // The original's brightness is (0x80000000 - ny) >> 28 + (nx < 0) - 5
        // with only a lower clamp (Lander.arm:5510-5533: SUBS R1, R1, #5 /
        // MOVMI R1, #0). The gazebo roof normal is 107 tiles up, giving
        // brightness 9: with face colour &400 the roof renders red=13 in the
        // original. A clamp at 3 renders red=7 instead — the port's most
        // visible fidelity break.
        var state = new GameState();
        state.Initialize();
        state.XCamera = 0;
        state.YCamera = 0;
        state.ZCamera = 0;

        var landscape = new LandscapeGenerator(state);
        var buffers = new GraphicsBuffers();

        ObjectRenderer.DrawObject(ObjectBlueprints.Gazebo, 0, 0,
            FixedPoint.LANDSCAPE_Z_MID, 0, 0, state, buffers, landscape);
        buffers.AddTerminators();

        // The roof face points straight up
        var roof = ObjectBlueprints.Gazebo.Faces
            .OrderBy(f => f.Normal.Y).First();
        Assert.That(roof.Normal.Y, Is.LessThan(0), "Roof normal should point up");

        int expectedShade = (int)((0x80000000u - (uint)roof.Normal.Y) >> 28)
            + (roof.Normal.X < 0 ? 1 : 0) - 5;
        int expectedR = global::System.Math.Min(((roof.Colour >> 8) & 0xF) + expectedShade, 15);
        Assert.That(expectedR, Is.GreaterThan(7),
            "Precondition: the roof's true brightness must exceed the old clamp");

        // Find a drawn triangle with the roof's expected red channel
        bool foundBrightRed = false;
        for (int b = 0; b < buffers.BufferCount; b++)
        {
            var data = buffers.GetBufferData(b);
            for (int i = 0; i < data.Length; i += 8)
            {
                if (data[i] != GraphicsBuffers.COMMAND_TRIANGLE) break;
                byte vidc = (byte)(data[i + 7] & 0xFF);
                var (r8, _, _) = VidcColour.DecodeToRgb24(vidc);
                if (r8 / 17 == expectedR)
                {
                    foundBrightRed = true;
                    break;
                }
            }
        }

        Assert.That(foundBrightRed, Is.True,
            $"Roof must render at red={expectedR} (shade {expectedShade}), not clamped at 7");
    }

    [Test]
    public void RotatingObject_Culling_HandlesLargeCoordinates()
    {
        // Back-face culling must use the exact 64-bit dot product sign: with
        // camera-relative coordinates around 1e9 and normals around 1.8e8 the
        // products are ~1e17, far past int32 (Lander.arm:5024-5081 pre-scales
        // coordinates for the same reason). The previous int arithmetic wrapped
        // the sign and culled faces at random.
        //
        // Chosen so the wrap provably flips the sign: objX * nx =
        // 0x3B9ACA00 * -0x50000000 = -0x12A05F2000000000, whose low 32 bits are
        // zero — the exact dot is negative (face must be drawn) but the int32
        // wrap gives 0 (face culled).
        var state = new GameState();
        state.Initialize();
        state.XCamera = 0;
        state.YCamera = 0;
        state.ZCamera = 0;

        // Identity rotation matrix (shipPitch = shipDirection = 0)
        state.XNoseV = 0x7FFFFFFF; state.XRoofV = 0; state.XSideV = 0;
        state.YNoseV = 0; state.YRoofV = 0x7FFFFFFF; state.YSideV = 0;
        state.ZNoseV = 0; state.ZRoofV = 0; state.ZSideV = 0x7FFFFFFF;

        var landscape = new LandscapeGenerator(state);
        var buffers = new GraphicsBuffers();

        var blueprint = new ObjectBlueprint(
            "OverflowProbe",
            vertexCount: 3,
            faceCount: 1,
            flags: 0b00000001,  // rotates
            vertices:
            [
                new Vector3Int(0, 0, 0),
                new Vector3Int(FixedPoint.TILE_SIZE / 4, 0, 0),
                new Vector3Int(0, FixedPoint.TILE_SIZE / 4, 0),
            ],
            faces:
            [
                new Face(new Vector3Int(-0x50000000, 0, 0), 0, 1, 2, 0x444),
            ]);

        int objX = 0x3B9ACA00;  // ~1e9: product with the normal overflows int32
        int objY = 0;
        int objZ = FixedPoint.LANDSCAPE_Z_MID;

        ObjectRenderer.DrawObject(blueprint, objX, objY, objZ, 0, 0, state, buffers, landscape);
        buffers.AddTerminators();

        int triangles = 0;
        for (int b = 0; b < buffers.BufferCount; b++)
        {
            var data = buffers.GetBufferData(b);
            for (int i = 0; i < data.Length; i++)
                if (data[i] == GraphicsBuffers.COMMAND_TRIANGLE) triangles++;
        }

        Assert.That(triangles, Is.EqualTo(1),
            "Face with a negative exact dot product must be drawn despite int32 wrap");
    }

}
