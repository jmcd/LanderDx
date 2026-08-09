using Relander.Core.Engine;
using Relander.Core.Data;
using Relander.Core.Math;

namespace Relander.Tests;

[TestFixture]
public class ShadingDiagnosticTests
{
    [Test]
    public void RocketFaceColours_AllNonZero_AfterShading()
    {
        var rocket = ObjectBlueprints.Rocket;

        foreach (var face in rocket.Faces)
        {
            // Compute shade as the engine does
            int shade = (int)((0x80000000u - (uint)face.Normal.Y) >> 28);
            if (face.Normal.X < 0) shade++;
            shade = global::System.Math.Max(0, shade - 5);
            if (shade > 3) shade = 3;

            int r = global::System.Math.Min(((face.Colour >> 8) & 0xF) + shade, 15);
            int g = global::System.Math.Min(((face.Colour >> 4) & 0xF) + shade, 15);
            int b = global::System.Math.Min((face.Colour & 0xF) + shade, 15);

            byte vidc = VidcColour.Encode(r, g, b);
            var (dr, dg, db) = VidcColour.DecodeToRgb24(vidc);

            TestContext.WriteLine(
                $"Face: colour=0x{face.Colour:X3} normal=({face.Normal.X:X8},{face.Normal.Y:X8},{face.Normal.Z:X8}) " +
                $"shade={shade} → (r={r},g={g},b={b}) → VIDC=0x{vidc:X2} → RGB=({dr},{dg},{db})");

            Assert.That(vidc, Is.Not.EqualTo(0),
                $"Face colour encodes to black! colour=0x{face.Colour:X3}, shade={shade}");
            Assert.That(dr + dg + db, Is.GreaterThan(20),
                $"Face is nearly black: RGB=({dr},{dg},{db})");
        }
    }

    [Test]
    public void RocketVertices_ProjectToScreen_WithEngineCoords()
    {
        // Simulate what happens when the engine draws a rocket
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        var state = engine.State;
        // Rocket at world position (7,5) - the closest one to viewer
        int worldX = 7 * FixedPoint.TILE_SIZE;
        int worldZ = 5 * FixedPoint.TILE_SIZE;

        int objX = worldX - state.XCamera;
        int objY = engine.Landscape.GetAltitude(worldX, worldZ) - state.YCamera;
        int objZ = FixedPoint.LANDSCAPE_Z - state.ZCamera + worldZ;  // Screen-depth z

        TestContext.WriteLine($"Camera: ({state.XCamera:X8},{state.YCamera:X8},{state.ZCamera:X8})");
        TestContext.WriteLine($"Object: pos=({objX:X8},{objY:X8},{objZ:X8})");

        var rocket = ObjectBlueprints.Rocket;
        foreach (var face in rocket.Faces)
        {
            var v1 = rocket.Vertices[face.V1];
            var v2 = rocket.Vertices[face.V2];
            var v3 = rocket.Vertices[face.V3];

            int wx1 = objX + v1.X, wy1 = objY + v1.Y, wz1 = objZ + v1.Z;
            int wx2 = objX + v2.X, wy2 = objY + v2.Y, wz2 = objZ + v2.Z;
            int wx3 = objX + v3.X, wy3 = objY + v3.Y, wz3 = objZ + v3.Z;

            bool ok1 = Projection.Project(wx1, wy1, wz1, out int sx1, out int sy1);
            bool ok2 = Projection.Project(wx2, wy2, wz2, out int sx2, out int sy2);
            bool ok3 = Projection.Project(wx3, wy3, wz3, out int sx3, out int sy3);

            if (ok1 && ok2 && ok3)
            {
                // Check if triangle has non-zero area
                int area = global::System.Math.Abs((sx2 - sx1) * (sy3 - sy1) - (sx3 - sx1) * (sy2 - sy1));
                TestContext.WriteLine(
                    $"Face: area={area} screen=({sx1},{sy1}) ({sx2},{sy2}) ({sx3},{sy3}) " +
                    $"world=({wx1:X8},{wy1:X8},{wz1:X8})");

                Assert.That(area, Is.GreaterThan(0),
                    $"Face triangle has zero area — degenerate projection");
            }
            else
            {
                TestContext.WriteLine(
                    $"Face: BEHIND CAMERA world=({wx1:X8},{wy1:X8},{wz1:X8})");
            }
        }
    }

    [Test]
    public void RocketInBuffer_HasNonZeroColourWord()
    {
        // Verify that the AddTriangle stores the correct colour word
        var buffers = new GraphicsBuffers();

        int r = 15, g = 15, b = 3;  // Typical shaded rocket colour
        byte vidc = VidcColour.Encode(r, g, b);
        int colourWord = VidcColour.ReplicateQuad(vidc);

        buffers.AddTriangle(0, 10, 10, 30, 10, 20, 30, colourWord);
        buffers.AddTerminators();

        var data = buffers.GetBufferData(0);
        Assert.That(data.Length, Is.GreaterThanOrEqualTo(8));

        int storedColour = data[7];
        byte extractedVidc = (byte)(storedColour & 0xFF);

        TestContext.WriteLine($"Colour word: 0x{storedColour:X8}, low byte: 0x{extractedVidc:X2}");
        Assert.That(extractedVidc, Is.Not.EqualTo(0), "Stored colour byte is 0 (black)");
        Assert.That(extractedVidc, Is.EqualTo(vidc), $"Stored VIDC 0x{extractedVidc:X2} != encoded 0x{vidc:X2}");
    }

    [Test]
    public void AllFaceColours_ProduceNonBlackVidc()
    {
        // Check every face of every object (except intentional black faces on destroyed objects)
        foreach (var bp in ObjectBlueprints.All)
        {
            foreach (var face in bp.Faces)
            {
                // Smoking remains have intentional black faces (colour 0x000)
                if (face.Colour == 0) continue;

                int shade = (int)((0x80000000u - (uint)face.Normal.Y) >> 28);
                if (face.Normal.X < 0) shade++;
                shade = global::System.Math.Max(0, shade - 5);
                if (shade > 3) shade = 3;

                int r = global::System.Math.Min(((face.Colour >> 8) & 0xF) + shade, 15);
                int g = global::System.Math.Min(((face.Colour >> 4) & 0xF) + shade, 15);
                int b = global::System.Math.Min((face.Colour & 0xF) + shade, 15);

                byte vidc = VidcColour.Encode(r, g, b);
                Assert.That(vidc, Is.Not.EqualTo(0),
                    $"{bp.Name} face colour 0x{face.Colour:X3} + shade {shade} = VIDC 0");
            }
        }
    }

    [Test]
    public void RocketPixels_ShowCorrectColour_InFramebuffer()
    {
        var random = new RandomGenerator(42);
        var screen = new TestScreen();
        var engine = new GameEngine(random, screen);
        engine.StartNewGame();

        // First frame renders with start position
        engine.Update(new TestInput());

        // Expected rocket colour: face colour 0xCC0 + shade 3 → VIDC 0x77
        // Decoded: R=119, G=255, B=51 — bright yellow-green
        byte expectedVidc = VidcColour.Encode(15, 15, 3);
        TestContext.WriteLine($"Expected rocket colour: VIDC 0x{expectedVidc:X2}");

        var state = engine.State;
        // Rocket at (7,3) — middle one
        int worldX = 7 * FixedPoint.TILE_SIZE;
        int worldZ = 3 * FixedPoint.TILE_SIZE;
        int objZ = FixedPoint.LANDSCAPE_Z - state.ZCamera + worldZ;
        int objY = engine.Landscape.GetAltitude(worldX, worldZ) - state.YCamera;

        // Compute expected screen position for several rocket vertices
        var rocket = ObjectBlueprints.Rocket;
        int minX = 320, maxX = 0, minY = 240, maxY = 0;
        foreach (var vert in rocket.Vertices)
        {
            int wx = (worldX - state.XCamera) + vert.X;
            int wy = objY + vert.Y;
            int wz = objZ + vert.Z;
            if (Projection.Project(wx, wy, wz, out int sx, out int sy))
            {
                minX = global::System.Math.Min(minX, sx);
                maxX = global::System.Math.Max(maxX, sx);
                minY = global::System.Math.Min(minY, sy);
                maxY = global::System.Math.Max(maxY, sy);
            }
        }
        TestContext.WriteLine($"Rocket bounding box: ({minX},{minY})-({maxX},{maxY})");

        // Check pixels INSIDE the rocket's bounding box
        int rocketPixels = 0;
        int blackPixels = 0;
        var colorsFound = new Dictionary<byte, int>();
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if ((uint)x >= 320 || (uint)y >= 240) continue;
                byte c = screen.GetPlayPixel(x, y);
                if (c != 0)
                {
                    rocketPixels++;
                    colorsFound.TryGetValue(c, out int cnt);
                    colorsFound[c] = cnt + 1;
                }
                else blackPixels++;
            }
        }

        TestContext.WriteLine($"Rocket bounding box: {rocketPixels} colored, {blackPixels} black");
        foreach (var kv in colorsFound.OrderByDescending(kv => kv.Value).Take(5))
        {
            var (r, g, b) = VidcColour.DecodeToRgb24(kv.Key);
            TestContext.WriteLine($"  VIDC 0x{kv.Key:X2} = RGB({r},{g},{b}): {kv.Value} pixels");
        }

        Assert.That(rocketPixels, Is.GreaterThan(5),
            $"Only {rocketPixels} colored pixels in rocket bounding box");
        Assert.That(colorsFound.ContainsKey(expectedVidc),
            $"Expected rocket colour VIDC 0x{expectedVidc:X2} not found in bounding box");
    }

    private class TestInput : Relander.Core.Interfaces.IGameInput
    {
        public int MouseX => 512;
        public int MouseY => 512;
        public bool LeftButton => false;
        public bool MiddleButton => false;
        public bool RightButton => false;
        public bool EscapePressed => false;
    }

    private class TestScreen : Relander.Core.Interfaces.IScreen
    {
        private readonly byte[] _fb = new byte[320 * 256];
        public int Width => 320;
        public int Height => 256;
        public Span<byte> GetFramebuffer() => _fb;
        public void Clear(byte color = 0) => Array.Fill(_fb, color);
        public byte GetPlayPixel(int x, int y) => _fb[(y + 16) * 320 + x];
    }
}
