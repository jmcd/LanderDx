namespace Relander.Core.Interfaces;

/// <summary>
/// Abstraction for random number generation, enabling deterministic testing.
/// The original game uses a simple PRNG based on OS_Byte calls.
/// </summary>
public interface IRandomSource
{
    /// <summary>Get the next random 32-bit integer.</summary>
    int Next();

    /// <summary>Get two random 32-bit integers (matching the GetRandomNumbers pattern).</summary>
    (int, int) GetRandomNumbers();
}
