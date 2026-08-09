using Relander.Core.Engine;
using Relander.Core.Math;

namespace Relander.Tests;

[TestFixture]
public class LandscapeTests
{
    private GameState _state = null!;
    private LandscapeGenerator _gen = null!;

    [SetUp]
    public void Setup()
    {
        _state = new GameState();
        _gen = new LandscapeGenerator(_state);
    }

    [Test]
    public void Launchpad_IsFlat()
    {
        // The launchpad area (center of map) should be at LAUNCHPAD_ALTITUDE
        for (int x = 0; x < 4 * FixedPoint.TILE_SIZE; x += FixedPoint.TILE_SIZE)
        {
            for (int z = 0; z < 4 * FixedPoint.TILE_SIZE; z += FixedPoint.TILE_SIZE)
            {
                int alt = _gen.GetAltitude(x, z);
                Assert.That(alt, Is.EqualTo(FixedPoint.LAUNCHPAD_ALTITUDE),
                    $"Launchpad at ({x:X8},{z:X8}) should be 0x{FixedPoint.LAUNCHPAD_ALTITUDE:X8}, got 0x{alt:X8}");
            }
        }
    }

    [Test]
    public void SeaLevel_IsNeverExceeded()
    {
        // Sample many points — altitude should never exceed SEA_LEVEL
        var rand = new Random(42);
        for (int i = 0; i < 1000; i++)
        {
            int x = (rand.Next() & 0xFFFF) << 16;
            int z = (rand.Next() & 0xFFFF) << 16;
            int alt = _gen.GetAltitude(x, z);
            Assert.That(alt, Is.LessThanOrEqualTo(FixedPoint.SEA_LEVEL),
                $"Altitude 0x{alt:X8} at ({x:X8},{z:X8}) exceeds SEA_LEVEL");
        }
    }

    [Test]
    public void Altitude_IsDeterministic()
    {
        // Same input should always give same output
        int alt1 = _gen.GetAltitude(0x05000000, 0x0A000000);
        int alt2 = _gen.GetAltitude(0x05000000, 0x0A000000);
        Assert.That(alt1, Is.EqualTo(alt2));
    }

    [Test]
    public void PrevAltitude_IsUpdated()
    {
        _gen.GetAltitude(0x01000000, 0x02000000);
        int prev1 = _state.PrevAltitude;
        int curr1 = _state.Altitude;

        _gen.GetAltitude(0x03000000, 0x04000000);
        Assert.That(_state.PrevAltitude, Is.EqualTo(curr1),
            "prevAltitude should be altitude from previous call");
    }

    [Test]
    public void TileColour_Launchpad_IsGrey()
    {
        // Set altitude to launchpad
        _gen.GetAltitude(0, 0); // This sets altitude and prevAltitude
        _gen.GetAltitude(FixedPoint.TILE_SIZE, 0); // Move one tile right, still on launchpad

        int colour = _gen.GetTileColour(5); // Mid-distance row
        byte vidc = (byte)(colour & 0xFF);

        var (r, g, b) = VidcColour.DecodeToRgb24(vidc);
        // Grey: all channels roughly equal
        Assert.That(global::System.Math.Abs(r - g), Is.LessThanOrEqualTo(32), $"Not grey: R={r} G={g} B={b}");
        Assert.That(global::System.Math.Abs(g - b), Is.LessThanOrEqualTo(32), $"Not grey: R={r} G={g} B={b}");
    }

    [Test]
    public void TileColour_Sea_IsBlue()
    {
        // Sea level is at altitude 0x05500000. Need a position that gives sea level.
        // Sea occurs when both current and previous are at sea level.
        // Find a position far from origin that might hit sea level.
        for (int i = 0; i < 100; i++)
        {
            int x = (0x10 + i) * FixedPoint.TILE_SIZE;
            _gen.GetAltitude(x, 0);
            if (_state.Altitude == FixedPoint.SEA_LEVEL && _state.PrevAltitude == FixedPoint.SEA_LEVEL)
            {
                int colour = _gen.GetTileColour(5);
                byte vidc = (byte)(colour & 0xFF);
                var (r, g, b) = VidcColour.DecodeToRgb24(vidc);
                Assert.That(b, Is.GreaterThan(r), $"Sea should be blue: R={r} G={g} B={b}");
                Assert.That(b, Is.GreaterThan(g), $"Sea should be blue: R={r} G={g} B={b}");
                return;
            }
        }
        Assert.Inconclusive("Could not find a sea-level tile in 100 samples");
    }

    [Test]
    public void TileColour_VariesWithRow()
    {
        // Farther rows should be darker (lower brightness)
        _gen.GetAltitude(FixedPoint.TILE_SIZE * 10, FixedPoint.TILE_SIZE * 10);
        _gen.GetAltitude(FixedPoint.TILE_SIZE * 11, FixedPoint.TILE_SIZE * 10);

        int colourBack = _gen.GetTileColour(1);   // Back row (darker)
        int colourFront = _gen.GetTileColour(10); // Front row (brighter)

        byte vidcBack = (byte)(colourBack & 0xFF);
        byte vidcFront = (byte)(colourFront & 0xFF);

        var (rb, gb, bb) = VidcColour.DecodeToRgb24(vidcBack);
        var (rf, gf, bf) = VidcColour.DecodeToRgb24(vidcFront);

        int brightBack = rb + gb + bb;
        int brightFront = rf + gf + bf;

        Assert.That(brightFront, Is.GreaterThanOrEqualTo(brightBack),
            $"Front row ({brightFront}) should not be darker than back row ({brightBack})");
    }
}
