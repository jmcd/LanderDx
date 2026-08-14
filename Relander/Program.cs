using System.Numerics;
using Raylib_cs;
using Relander.Core.Engine;

namespace Relander;

public static class Program
{
    private const int SCALE = 4;
    private const int GAME_WIDTH = 320;
    private const int GAME_HEIGHT = 256;

    // The original Archimedes ARM2 completed one game frame in approximately
    // 4 VSync periods (4 × 20 ms at 50 Hz PAL) = 80 ms ≈ 12.5 FPS.
    // This constant drives the fixed-step accumulator so game logic runs at the
    // authentic speed regardless of the display refresh rate.
    private const double TARGET_GAME_FPS = 12.5;
    private const double GAME_FRAME_SECONDS = 1.0 / TARGET_GAME_FPS;

    public static void Main()
    {
        // Initialize raylib
        Raylib.InitWindow(GAME_WIDTH * SCALE, GAME_HEIGHT * SCALE, "Relander");
        Raylib.SetTargetFPS(60);   // Display runs at 60 FPS for a smooth window
        Raylib.SetExitKey(KeyboardKey.Null);

        // Create game engine
        var random = new RandomGenerator();
        var screen = new RaylibScreen(GAME_WIDTH, GAME_HEIGHT);
        var input = new RaylibInput();
        var engine = new GameEngine(random, screen);

        // Start the game
        engine.StartNewGame();

        // Build VIDC palette
        var palette = VidcColour.BuildPalette();

        // Create a texture for the game framebuffer
        var image = Raylib.GenImageColor(GAME_WIDTH, GAME_HEIGHT, Color.Black);
        var texture = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);

        // RGBA buffer for texture upload
        var rgbaBuffer = new byte[GAME_WIDTH * GAME_HEIGHT * 4];

        // Fixed-step accumulator: game logic advances in GAME_FRAME_SECONDS steps
        // while the display renders as fast as the target FPS allows.
        double accumulator = 0.0;
        bool running = true;

        // Main game loop
        while (!Raylib.WindowShouldClose() && running)
        {
            input.PollEvents();

            // View depth toggle (C): a presentation option handled at the
            // display rate, between game ticks.
            if (input.ConsumeViewDepthToggle())
                engine.CycleViewDepth();

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

            var srcRect = new Rectangle(0, 0, GAME_WIDTH, GAME_HEIGHT);
            var dstRect = new Rectangle(0, 0, GAME_WIDTH * SCALE, GAME_HEIGHT * SCALE);
            Raylib.DrawTexturePro(texture, srcRect, dstRect, Vector2.Zero, 0f, Color.White);

            Raylib.EndDrawing();
        }

        // Cleanup
        Raylib.UnloadTexture(texture);
        Raylib.CloseWindow();
    }
}
