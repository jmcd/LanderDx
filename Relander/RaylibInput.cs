using Raylib_cs;
using Relander.Core.Interfaces;

namespace Relander;

public class RaylibInput : IGameInput
{
    private bool _toggleMapLatched = false;
    private bool _viewDepthLatched = false;
    private bool _viewWidthLatched = false;

    /// <summary>
    /// Must be called every frame of the Raylib window loop (60 Hz) to latch
    /// transient single-press key events so they are never missed when game logic ticks (12.5 Hz).
    /// </summary>
    public void PollEvents()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Tab) || Raylib.IsKeyPressed(KeyboardKey.R))
        {
            _toggleMapLatched = true;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.C))
        {
            _viewDepthLatched = true;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.X))
        {
            _viewWidthLatched = true;
        }
    }

    public bool YawLeft => Raylib.IsKeyDown(KeyboardKey.A);
    public bool YawRight => Raylib.IsKeyDown(KeyboardKey.D);
    public bool PitchUp => Raylib.IsKeyDown(KeyboardKey.W);
    public bool PitchDown => Raylib.IsKeyDown(KeyboardKey.S);
    public bool Fire => Raylib.IsKeyDown(KeyboardKey.N);
    public bool Thrust => Raylib.IsKeyDown(KeyboardKey.M);
    public bool Hover => Raylib.IsKeyDown(KeyboardKey.H);

    public bool ToggleMap
    {
        get
        {
            bool val = _toggleMapLatched || Raylib.IsKeyPressed(KeyboardKey.Tab) || Raylib.IsKeyPressed(KeyboardKey.R);
            _toggleMapLatched = false;
            return val;
        }
    }

    public bool EscapePressed => Raylib.IsKeyDown(KeyboardKey.Escape);

    /// <summary>
    /// Cycle view depth (key C): latched across display frames so a tap between
    /// 60 Hz polls is never missed. Consumed by the frontend (not IGameInput —
    /// the view depth is a presentation option, not game state).
    /// </summary>
    public bool ConsumeViewDepthToggle()
    {
        bool val = _viewDepthLatched || Raylib.IsKeyPressed(KeyboardKey.C);
        _viewDepthLatched = false;
        return val;
    }

    /// <summary>
    /// Cycle view width (key X): latched across display frames like the depth
    /// toggle. Consumed by the frontend (presentation option, not game state).
    /// </summary>
    public bool ConsumeViewWidthToggle()
    {
        bool val = _viewWidthLatched || Raylib.IsKeyPressed(KeyboardKey.X);
        _viewWidthLatched = false;
        return val;
    }

    /// <summary>
    /// Any key: presses accumulated since the last poll (Raylib queues key-pressed
    /// events, so a tap between 12.5 Hz game ticks is still seen), plus any held
    /// game key.
    /// </summary>
    public bool AnyKeyPressed =>
        Raylib.GetKeyPressed() != 0
        || YawLeft || YawRight || PitchUp || PitchDown || Fire || Thrust || Hover;
}
