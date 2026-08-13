using Raylib_cs;
using Relander.Core.Interfaces;

namespace Relander;

public class RaylibInput : IGameInput
{
    public bool YawLeft => Raylib.IsKeyDown(KeyboardKey.A);
    public bool YawRight => Raylib.IsKeyDown(KeyboardKey.D);
    public bool PitchUp => Raylib.IsKeyDown(KeyboardKey.W);
    public bool PitchDown => Raylib.IsKeyDown(KeyboardKey.S);
    public bool Fire => Raylib.IsKeyDown(KeyboardKey.N);
    public bool Thrust => Raylib.IsKeyDown(KeyboardKey.M);
    public bool Hover => Raylib.IsKeyDown(KeyboardKey.H);
    public bool ToggleMap => Raylib.IsKeyPressed(KeyboardKey.Tab) || Raylib.IsKeyPressed(KeyboardKey.R);
    public bool EscapePressed => Raylib.IsKeyDown(KeyboardKey.Escape);
}
