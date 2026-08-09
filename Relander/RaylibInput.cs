using Raylib_cs;
using Relander.Core.Interfaces;

namespace Relander;

/// <summary>
/// Implements IGameInput using raylib's mouse and keyboard API.
/// </summary>
public class RaylibInput : IGameInput
{
    // Scale factor: raylib window size to game's 0-1023 range
    private readonly int _windowWidth;
    private readonly int _windowHeight;

    public RaylibInput(int windowWidth, int windowHeight)
    {
        _windowWidth = windowWidth;
        _windowHeight = windowHeight;
    }

    public int MouseX
    {
        get
        {
            float mx = Raylib.GetMousePosition().X;
            return (int)(mx * 1024 / _windowWidth);
        }
    }

    public int MouseY
    {
        get
        {
            float my = Raylib.GetMousePosition().Y;
            return (int)(my * 1024 / _windowHeight);
        }
    }

    public bool LeftButton => Raylib.IsKeyDown(KeyboardKey.A);
    public bool MiddleButton => Raylib.IsKeyDown(KeyboardKey.S);
    public bool RightButton => Raylib.IsKeyDown(KeyboardKey.D);
    public bool EscapePressed => Raylib.IsKeyDown(KeyboardKey.Escape);
}
