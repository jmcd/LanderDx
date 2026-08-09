namespace Relander.Core.Interfaces;

/// <summary>
/// Abstraction for game input (mouse, keyboard).
/// The raylib backend implements this; tests provide a mock implementation.
/// </summary>
public interface IGameInput
{
    /// <summary>Mouse x-coordinate (0-1023 range as in the original).</summary>
    int MouseX { get; }

    /// <summary>Mouse y-coordinate (0-1023 range as in the original).</summary>
    int MouseY { get; }

    /// <summary>Left mouse button: full thrust.</summary>
    bool LeftButton { get; }

    /// <summary>Middle mouse button: hover.</summary>
    bool MiddleButton { get; }

    /// <summary>Right mouse button: fire bullets.</summary>
    bool RightButton { get; }

    /// <summary>Escape key: quit game.</summary>
    bool EscapePressed { get; }
}
