using Relander.Core.Interfaces;

namespace Relander.Core.Engine;

/// <summary>
/// Implements the original Lander PRNG (LFSR-based) from Lander.arm:7830-7854.
/// Uses two 32-bit seeds that are mutated on each call, returning two random values.
/// </summary>
public class RandomGenerator : IRandomSource
{
    private int _seed1;
    private int _seed2;

    public RandomGenerator(int seed1 = 0x12345678, int seed2 = unchecked((int)0x9ABCDEF0))
    {
        _seed1 = seed1;
        _seed2 = seed2;
    }

    /// <summary>Generate the next random number and update internal state.</summary>
    public int Next()
    {
        // TST R1, R1, LSR #1 — test R1 AND (R1 >> 1)
        uint r1u = (uint)_seed2;
        bool carry = (r1u & (r1u >> 1)) != 0;

        // MOVS R14, R0, RRX — rotate R0 right through carry
        uint r0u = (uint)_seed1;
        uint r14 = (r0u >> 1) | (carry ? 0x80000000u : 0u);
        bool newCarry = (r0u & 1) != 0;

        // ADC R1, R1, R1 — R1 = R1 + R1 + C
        uint newR1 = r1u + r1u + (newCarry ? 1u : 0u);

        // EOR R14, R14, R0, LSL #12
        r14 ^= r0u << 12;

        // EOR R0, R14, R14, LSR #20
        uint newR0 = r14 ^ (r14 >> 20);

        _seed1 = (int)newR1;
        _seed2 = (int)newR0;

        return (int)newR0;
    }

    /// <summary>
    /// Generate two random numbers, matching the GetRandomNumbers pattern.
    /// Returns the seed values that were current before mutation.
    /// </summary>
    public (int, int) GetRandomNumbers()
    {
        int r0 = _seed1;
        Next();
        return (r0, _seed2);
    }
}
