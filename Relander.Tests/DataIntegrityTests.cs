using Relander.Core.Math;
using Relander.Core.Engine;
using Relander.Core.Data;

namespace Relander.Tests;

public class DataIntegrityTests
{
    [Test]
    public void SineTable_Has1024Entries()
    {
        Assert.That(SineTable.Data, Has.Length.EqualTo(1024));
    }

    [Test]
    public void SineTable_Entry0_IsZero()
    {
        Assert.That(SineTable.Data[0], Is.EqualTo(0));
    }

    [Test]
    public void SineTable_QuarterCircle_Matches()
    {
        // sin(90°) = (2^31 - 1) = 0x7FFFFFFE (or close)
        // Entry 256 is sin(2π * 256/1024) = sin(π/2) = 1
        int sin90 = SineTable.Data[256];
        Assert.That(sin90, Is.EqualTo(0x7FFFFFFE),
            $"Expected 0x7FFFFFFE, got 0x{sin90:X8}");
    }

    [Test]
    public void ArctanTable_Has128Entries()
    {
        Assert.That(ArctanTable.Data, Has.Length.EqualTo(128));
    }

    [Test]
    public void SquareRootTable_Has1024Entries()
    {
        Assert.That(SquareRootTable.Data, Has.Length.EqualTo(1024));
    }

    [Test]
    public void DivisionTable_Has4096Entries()
    {
        Assert.That(DivisionTable.Data, Has.Length.EqualTo(4096));
    }

    [Test]
    public void DivisionTable_Terminator_IsAllOnes()
    {
        // Entry n=0 for any denominator d should be 0xFFFFFFFF
        for (int d = 0; d < 64; d++)
        {
            Assert.That(DivisionTable.Data[d * 64], Is.EqualTo(unchecked((int)0xFFFFFFFF)),
                $"Division table [{d},0] should be terminator");
        }
    }

    [Test]
    public void ObjectBlueprints_All13Defined()
    {
        Assert.That(ObjectBlueprints.All, Has.Length.EqualTo(13));
    }

    [Test]
    public void ObjectBlueprints_PlayerShip_Has9Vertices()
    {
        var ship = ObjectBlueprints.PlayerShip;
        Assert.That(ship.VertexCount, Is.EqualTo(9));
        Assert.That(ship.FaceCount, Is.EqualTo(9));
    }

    [Test]
    public void ObjectBlueprints_Rock_Has6Vertices8Faces()
    {
        var rock = ObjectBlueprints.Rock;
        Assert.That(rock.VertexCount, Is.EqualTo(6));
        Assert.That(rock.FaceCount, Is.EqualTo(8));
    }

    [Test]
    public void ObjectTypes_Maps24Types()
    {
        Assert.That(ObjectTypes.ByType, Has.Length.EqualTo(25));  // 0-24
    }

    [Test]
    public void Landscape_LaunchpadIsFlat()
    {
        var state = new Core.Engine.GameState();
        var gen = new Core.Engine.LandscapeGenerator(state);

        // Launchpad area should be at LAUNCHPAD_ALTITUDE
        int alt = gen.GetAltitude(0, 0);
        Assert.That(alt, Is.EqualTo(FixedPoint.LAUNCHPAD_ALTITUDE),
            $"Launchpad altitude should be 0x{FixedPoint.LAUNCHPAD_ALTITUDE:X8}, got 0x{alt:X8}");
    }

    [Test]
    public void Landscape_SeaLevelClamps()
    {
        var state = new Core.Engine.GameState();
        var gen = new Core.Engine.LandscapeGenerator(state);

        // Far away from origin, altitude should be <= SEA_LEVEL
        for (int x = -10; x <= 10; x += 5)
        {
            for (int z = -10; z <= 10; z += 5)
            {
                int worldX = x * FixedPoint.TILE_SIZE;
                int worldZ = z * FixedPoint.TILE_SIZE;

                // Skip launchpad area
                if (worldX < FixedPoint.LAUNCHPAD_SIZE && worldX > -FixedPoint.LAUNCHPAD_SIZE &&
                    worldZ < FixedPoint.LAUNCHPAD_SIZE && worldZ > -FixedPoint.LAUNCHPAD_SIZE)
                    continue;

                int alt = gen.GetAltitude(worldX, worldZ);
                Assert.That(alt, Is.LessThanOrEqualTo(FixedPoint.SEA_LEVEL),
                    $"Altitude at ({x},{z}) = 0x{alt:X8} should be <= SEA_LEVEL (0x{FixedPoint.SEA_LEVEL:X8})");
            }
        }
    }

    [Test]
    public void RandomGenerator_DefaultSeeds_MatchRomConstants()
    {
        // The ROM's fixed seed pair is EQUD &4F9C3490 / EQUD &DA0383CF
        // (Lander.arm:7776-7795). With the default constructor the first
        // GetRandomNumbers output must be 0x64871C00 / 0xB407079E (verified by
        // independent instruction-level simulation of the LFSR at
        // Lander.arm:7830-7854). The previous defaults (0x12345678/0x9ABCDEF0)
        // changed the entire game's random sequence: object map layout, spray
        // colours, rock drops.
        var rng = new RandomGenerator();
        var (r0, r1) = rng.GetRandomNumbers();

        Assert.That(r0, Is.EqualTo(unchecked((int)0x64871C00)),
            "First R0 must match the ROM-seeded sequence");
        Assert.That(r1, Is.EqualTo(unchecked((int)0xB407079E)),
            "First R1 must match the ROM-seeded sequence");
    }
}
