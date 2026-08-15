using System.Numerics;
using Raylib_cs;
using LanderDx.Core.Engine;

namespace LanderDx;

public static class Program
{
    // The original Archimedes ARM2 completed one game frame in approximately
    // 4 VSync periods (4 × 20 ms at 50 Hz PAL) = 80 ms ≈ 12.5 FPS.
    // This constant drives the fixed-step accumulator so game logic runs at the
    // authentic speed regardless of the display refresh rate.
    private const double TARGET_GAME_FPS = 12.5;
    private const double GAME_FRAME_SECONDS = 1.0 / TARGET_GAME_FPS;

    public static void Main(string[] args)
    {
        // The default is the enhanced widescreen mode: a 456×256 framebuffer
        // (16:9, 456×240 play area) at 3× scale (1368×768) with the maximum
        // view size baked in. --original selects the original 320×256
        // resolution (the untouched byte-identical view) at 4× (1280×1024).
        bool original = args.Contains("--original");
        int gameWidth = original ? 320 : 456;
        const int gameHeight = 256;
        int scale = original ? 4 : 3;

        // Initialize raylib
        Raylib.InitWindow(gameWidth * scale, gameHeight * scale, "Lander DX");
        Raylib.SetTargetFPS(60);   // Display runs at 60 FPS for a smooth window
        Raylib.SetExitKey(KeyboardKey.Null);

        // Create game engine. The widescreen default bakes in the maximum view
        // at startup (no runtime view toggles); --original uses the original.
        var random = new RandomGenerator();
        var screen = new RaylibScreen(gameWidth, gameHeight);
        var input = new RaylibInput();
        var engine = new GameEngine(random, screen, original ? null : ViewConfig.Maximum);

        // Start the game
        engine.StartNewGame();

        // Build VIDC palette
        var palette = VidcColour.BuildPalette();

        // Create a texture for the game framebuffer
        var image = Raylib.GenImageColor(gameWidth, gameHeight, Color.Black);
        var texture = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);

        // RGBA buffer for texture upload
        var rgbaBuffer = new byte[gameWidth * gameHeight * 4];

        // Fixed-step accumulator: game logic advances in GAME_FRAME_SECONDS steps
        // while the display renders as fast as the target FPS allows.
        double accumulator = 0.0;
        bool running = true;

        // Main game loop
        while (!Raylib.WindowShouldClose() && running)
        {
            input.PollEvents();

            // The coordinate display (P): a presentation option handled at the
            // display rate, between game ticks.
            if (input.ConsumeCoordsToggle())
                engine.ToggleCoords();

            accumulator += Raylib.GetFrameTime();
            // After a long display stall (window drag/freeze) the accumulator
            // would otherwise catch up in a burst of game ticks; clamp to one
            // frame so a stall drops time instead of fast-forwarding the game.
            if (accumulator > GAME_FRAME_SECONDS)
                accumulator = GAME_FRAME_SECONDS;

            // Step game logic once per accumulated game frame
            bool textureStale = false;
            while (accumulator >= GAME_FRAME_SECONDS)
            {
                if (!engine.Update(input))
                {
                    running = false;
                    break;
                }
                accumulator -= GAME_FRAME_SECONDS;
                textureStale = true;
            }

            // Only re-convert the framebuffer when game logic produced a new frame
            if (textureStale)
            {
                var framebuffer = screen.Framebuffer;
                for (int i = 0; i < framebuffer.Length; i++)
                {
                    uint colour = palette[framebuffer[i]];
                    int offset = i * 4;
                    rgbaBuffer[offset]     = (byte)(colour >> 16);
                    rgbaBuffer[offset + 1] = (byte)(colour >> 8);
                    rgbaBuffer[offset + 2] = (byte)colour;
                    rgbaBuffer[offset + 3] = (byte)(colour >> 24);
                }
                Raylib.UpdateTexture(texture, rgbaBuffer.AsSpan());
            }

            // Render (always, so the window stays responsive)
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            var srcRect = new Rectangle(0, 0, gameWidth, gameHeight);
            var dstRect = new Rectangle(0, 0, gameWidth * scale, gameHeight * scale);
            Raylib.DrawTexturePro(texture, srcRect, dstRect, Vector2.Zero, 0f, Color.White);

            Raylib.EndDrawing();
        }

        // Cleanup
        Raylib.UnloadTexture(texture);
        Raylib.CloseWindow();
    }
}
