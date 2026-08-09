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
        // TST R1, R1, LSR #1 — sets Z flag, does NOT modify C
        // The C flag used by RRX below is whatever was left by the caller.
        // In the object placement loop, SUBS sets C=1 before each call.
        // We use a fixed C=1 as the default (matches the common calling pattern).
        const bool existingCarry = true;

        uint r1u = (uint)_seed2;
        // TST result affects Z (checked later by callers? No — MOVS overwrites Z too).
        // The TST is essentially a NOP for flag purposes; it's likely a pipeline delay slot.

        // MOVS R14, R0, RRX — rotate R0 right through the EXISTING C flag
        uint r0u = (uint)_seed1;
        uint r14 = (r0u >> 1) | (existingCarry ? 0x80000000u : 0u);
        bool nextCarry = (r0u & 1) != 0;

        // ADC R1, R1, R1 — R1 = R1 + R1 + nextCarry
        uint newR1 = r1u + r1u + (nextCarry ? 1u : 0u);

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
    /// Returns the NEW (mutated) R0 and R1 values, as the original does.
    /// </summary>
    public (int, int) GetRandomNumbers()
    {
        int newR0 = Next();
        return (newR0, _seed1);  // newR0 → R0, new seed1 (was R1 in original) → R1
    }
}
