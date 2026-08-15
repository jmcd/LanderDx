using System.Runtime.InteropServices.JavaScript;
using LanderDx.Core.Engine;
using LanderDx.Core.Interfaces;

/// <summary>
/// Browser (browser-wasm) frontend for the LanderDx engine: the same
/// zero-dependency LanderDx.Core compiled to WebAssembly, driven by the
/// canvas/JS glue in main.js. The engine runs its authentic 12.5 Hz fixed
/// step (the accumulator lives in main.js, mirroring the desktop Program).
/// </summary>
public static partial class Program
{
    private static JSInput _input = new();
    private static JSScreen? _screen;
    private static GameEngine? _engine;
    private static bool _widescreen;

    public static void Main()
    {
        // The JS side drives everything; Main exists only to satisfy the
        // entry point. Start() is called from main.js after the exports load.
    }

    /// <summary>Start a new game; widescreen = 456x256 with the maximum view.</summary>
    [JSExport]
    internal static void Start(bool widescreen)
    {
        _widescreen = widescreen;
        _screen = new JSScreen(widescreen ? 456 : 320, 256);
        _engine = new GameEngine(new RandomGenerator(), _screen,
            widescreen ? ViewConfig.Maximum : null);
        _engine.StartNewGame();
    }

    /// <summary>
    /// Advance one game tick with the given input state. Returns false when
    /// the game quit (Escape) — main.js starts a new game in that case.
    /// held/pressed are bitmasks: 1=A yaw left, 2=D yaw right, 4=W pitch,
    /// 8=S pitch, 16=N fire, 32=M thrust, 64=H hover, 128=Tab/R map
    /// (edge-triggered), 256=Escape (edge-triggered).
    /// </summary>
    [JSExport]
    internal static bool Update(int held, int pressed)
    {
        _input.Held = held;
        _input.Pressed = pressed;
        return _engine!.Update(_input);
    }

    /// <summary>Toggle the P-key display (position readout + landing panel).</summary>
    [JSExport]
    internal static void ToggleCoords() => _engine!.ToggleCoords();

    /// <summary>The screen framebuffer as palette indices (width x 256).</summary>
    [JSExport]
    internal static byte[] GetScreen() => _screen!.GetFramebuffer().ToArray();

    /// <summary>The 256-entry RGBA palette as interleaved bytes (r,g,b,a per entry).</summary>
    [JSExport]
    internal static byte[] GetPalette()
    {
        var pal = VidcColour.BuildPalette();
        var rgba = new byte[pal.Length * 4];
        for (int i = 0; i < pal.Length; i++)
        {
            uint c = pal[i];
            rgba[i * 4] = (byte)(c >> 16);
            rgba[i * 4 + 1] = (byte)(c >> 8);
            rgba[i * 4 + 2] = (byte)c;
            rgba[i * 4 + 3] = (byte)(c >> 24);
        }
        return rgba;
    }

    /// <summary>The screen width, so main.js can size the canvas.</summary>
    [JSExport]
    internal static int GetWidth() => _screen!.Width;
}

/// <summary>IGameInput driven by the JS key state bitmasks.</summary>
internal sealed class JSInput : IGameInput
{
    public int Held;
    public int Pressed;

    public bool YawLeft => (Held & 1) != 0;
    public bool YawRight => (Held & 2) != 0;
    public bool PitchUp => (Held & 4) != 0;
    public bool PitchDown => (Held & 8) != 0;
    public bool Fire => (Held & 16) != 0;
    public bool Thrust => (Held & 32) != 0;
    public bool Hover => (Held & 64) != 0;
    public bool ToggleMap => (Pressed & 128) != 0;   // edge-triggered, like the desktop latch
    public bool EscapePressed => (Pressed & 256) != 0;
    public bool AnyKeyPressed => Pressed != 0 || Held != 0;
}

/// <summary>IScreen backed by a plain byte[] framebuffer of palette indices.</summary>
internal sealed class JSScreen : IScreen
{
    private readonly byte[] _fb;

    public JSScreen(int width, int height)
    {
        Width = width;
        Height = height;
        _fb = new byte[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public Span<byte> GetFramebuffer() => _fb;
    public void Clear(byte color = 0) => Array.Fill(_fb, color);
}
