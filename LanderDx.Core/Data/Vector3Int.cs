using System.Runtime.InteropServices;

namespace LanderDx.Core.Data;

/// <summary>
/// A 3D vector using the original game's 32-bit fixed-point format
/// (top byte = integer, lower 3 bytes = fractional).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Vector3Int(int X, int Y, int Z)
{
    public static readonly Vector3Int Zero = new(0, 0, 0);

    public override string ToString() => $"({X:X8}, {Y:X8}, {Z:X8})";
}
