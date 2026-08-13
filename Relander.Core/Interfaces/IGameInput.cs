namespace Relander.Core.Interfaces;

/// <summary>
/// Abstraction for game input (keyboard controls).
/// The raylib backend implements this; tests provide a mock implementation.
/// </summary>
public interface IGameInput
{
    /// <summary>Yaw left (key A).</summary>
    bool YawLeft { get; }

    /// <summary>Yaw right (key D).</summary>
    bool YawRight { get; }

    /// <summary>Pitch up / nose down (key W).</summary>
    bool PitchUp { get; }

    /// <summary>Pitch down / nose up (key S).</summary>
    bool PitchDown { get; }

    /// <summary>Fire bullets (key N).</summary>
    bool Fire { get; }

    /// <summary>Full thrust (key M).</summary>
    bool Thrust { get; }

    /// <summary>Hover (key H).</summary>
    bool Hover { get; }

    /// <summary>Toggle mini-map radar view (key Tab).</summary>
    bool ToggleMap { get; }

    /// <summary>Escape key: quit game.</summary>
    bool EscapePressed { get; }
}
