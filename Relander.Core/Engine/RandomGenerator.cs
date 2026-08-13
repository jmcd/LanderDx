using Relander.Core.Interfaces;

namespace Relander.Core.Engine;

/// <summary>
/// Implements the original Lander PRNG (LFSR-based) from Lander.arm:7830-7854.
/// Uses two 32-bit seeds that are mutated on each call, returning two random values.
/// </summary>
public class RandomGenerator : IRandomSource
{
    private uint _seed1;
    private uint _seed2;

    // The ROM's fixed seed pair (Lander.arm:7776-7795: EQUD &4F9C3490 /
    // EQUD &DA0383CF) — the default must be the original's, or the whole
    // random sequence (object map layout, spray colours, rock drops) differs.
    public RandomGenerator(int seed1 = unchecked((int)0x4F9C3490), int seed2 = unchecked((int)0xDA0383CF))
    {
        _seed1 = (uint)seed1;
        _seed2 = (uint)seed2;
    }

    /// <summary>Generate the next random number and update internal state.</summary>
    public int Next()
    {
        // Lander.arm:7830-7854:
        // LDR R0, randomSeed1
        // LDR R1, randomSeed2
        // TST R1, R1, LSR #1 -> sets C flag to bit 0 of R1 (seed2)
        // MOVS R14, R0, RRX -> rotates R0 right 1 bit using C (from seed2 bit 0), bit 0 of R0 goes into C
        // ADC R1, R1, R1 -> R1 = R1 + R1 + C
        // EOR R14, R14, R0, LSL #12
        // EOR R0, R14, R14, LSR #20
        // STR R1, randomSeed1
        // STR R0, randomSeed2
        uint r0 = _seed1;
        uint r1 = _seed2;

        // TST R1, R1, LSR #1 sets C to bit 0 of r1
        bool c0 = (r1 & 1) != 0;

        // MOVS R14, R0, RRX — rotate R0 right 1 bit using c0, bit 0 of r0 becomes c1
        uint r14 = (r0 >> 1) | (c0 ? 0x80000000u : 0u);
        bool c1 = (r0 & 1) != 0;

        // ADC R1, R1, R1 — R1 = R1 + R1 + c1
        uint newR1 = r1 + r1 + (c1 ? 1u : 0u);

        // EOR R14, R14, R0, LSL #12
        r14 ^= (r0 << 12);

        // EOR R0, R14, R14, LSR #20
        uint newR0 = r14 ^ (r14 >> 20);

        _seed1 = newR1;
        _seed2 = newR0;

        return (int)newR0;
    }

    /// <summary>
    /// Generate two random numbers, matching the GetRandomNumbers pattern.
    /// Returns the NEW (mutated) R0 and R1 values, as the original does.
    /// </summary>
    public (int, int) GetRandomNumbers()
    {
        int newR0 = Next();
        return (newR0, (int)_seed1);  // newR0 -> R0, _seed1 -> R1
    }
}
