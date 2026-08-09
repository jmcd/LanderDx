using Relander.Core.Engine;
using Relander.Core.Math;

namespace Relander.Tests;

[TestFixture]
public class PlacementDebugTests
{
    [Test]
    public void CheckAltitudeDistribution()
    {
        var random = new RandomGenerator(0x12345678, unchecked((int)0x9ABCDEF0));
        var state = new GameState();
        var landscape = new LandscapeGenerator(state);

        int seaLevel = 0;
        int launchpad = 0;
        int other = 0;
        var altitudes = new List<int>();

        // Sample 100 random positions
        for (int i = 0; i < 100; i++)
        {
            int rand0 = random.GetRandomNumbers().Item1;
            int x = rand0 & unchecked((int)0xFF000000);
            int z = (rand0 << 8) & unchecked((int)0xFF000000);
            int alt = landscape.GetAltitude(x, z);
            altitudes.Add(alt);

            if (alt == FixedPoint.SEA_LEVEL)
                seaLevel++;
            else if (alt == FixedPoint.LAUNCHPAD_ALTITUDE)
                launchpad++;
            else
                other++;
        }

        TestContext.WriteLine($"Sea level: {seaLevel}, Launchpad: {launchpad}, Other: {other}");
        TestContext.WriteLine($"Sample altitudes: {string.Join(", ", altitudes.Take(10).Select(a => $"0x{a:X8}"))}");

        Assert.That(other, Is.GreaterThan(0),
            $"All 100 positions were sea level or launchpad — altitude filter removes everything");
    }

    [Test]
    public void ObjectPlacement_SucceedsForMostAttempts()
    {
        // Use a simpler approach: manually place objects and check altitude
        var random = new RandomGenerator(0x12345678, unchecked((int)0x9ABCDEF0));
        var state = new GameState();
        var landscape = new LandscapeGenerator(state);

        int placed = 0;
        int attempts = 2048;

        for (int i = 0; i < attempts; i++)
        {
            int rand0 = random.GetRandomNumbers().Item1;
            int x = rand0 & unchecked((int)0xFF000000);
            int z = (rand0 << 8) & unchecked((int)0xFF000000);
            int alt = landscape.GetAltitude(x, z);

            if (alt != FixedPoint.SEA_LEVEL && alt != FixedPoint.LAUNCHPAD_ALTITUDE)
                placed++;
        }

        TestContext.WriteLine($"Placed: {placed}/{attempts} ({100.0 * placed / attempts:F1}%)");
        Assert.That(placed, Is.GreaterThan(500),
            $"Only {placed} objects placed — something is filtering too aggressively");
    }

    [Test]
    public void PRNG_ProducesRepeatingSequence()
    {
        // The original PRNG has a 64-value cycle by design
        var random = new RandomGenerator(0x12345678, unchecked((int)0x9ABCDEF0));
        var seen = new HashSet<int>();
        int repeats = 0;
        for (int i = 0; i < 1000; i++)
        {
            int val = random.GetRandomNumbers().Item1;
            if (!seen.Add(val)) repeats++;
        }
        TestContext.WriteLine($"Unique values in 1000 calls: {seen.Count}, repeats: {repeats}");
        // The PRNG cycles every ~64 values
        Assert.That(seen.Count, Is.InRange(32, 128),
            $"Expected 64-ish unique values, got {seen.Count}");
    }
}
