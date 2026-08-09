using System.Numerics;
using Raylib_cs;
using Relander.Core.Engine;

namespace Relander;

public static class Program
{
    private const int SCALE = 4;
    private const int GAME_WIDTH = 320;
    private const int GAME_HEIGHT = 256;

    public static void Main()
    {
        // Initialize raylib
        Raylib.InitWindow(GAME_WIDTH * SCALE, GAME_HEIGHT * SCALE, "Relander");
        Raylib.SetTargetFPS(60);
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

        // Main game loop
        while (!Raylib.WindowShouldClose() && engine.Update(input))
        {
            // Convert palette-indexed framebuffer to RGBA
            var framebuffer = screen.Framebuffer;
            for (int i = 0; i < framebuffer.Length; i++)
            {
                uint colour = palette[framebuffer[i]];
                int offset = i * 4;
                rgbaBuffer[offset] = (byte)(colour >> 16);
                rgbaBuffer[offset + 1] = (byte)(colour >> 8);
                rgbaBuffer[offset + 2] = (byte)colour;
                rgbaBuffer[offset + 3] = (byte)(colour >> 24);
            }

            // Upload to texture
            Raylib.UpdateTexture(texture, rgbaBuffer.AsSpan());

            // Render
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
