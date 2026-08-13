using Relander.Core.Interfaces;
using Relander.Core.Math;
using Relander.Core.Data;

namespace Relander.Core.Engine;

/// <summary>
/// Main game engine orchestrating all subsystems.
/// Implements the main loop from Lander.arm:12485-12549 including
/// back-to-front landscape rendering with depth-sorted graphics buffers.
/// </summary>
public class GameEngine
{
    private readonly GameState _state;
    private readonly GraphicsBuffers _buffers;
    private readonly IRandomSource _random;
    private readonly IScreen _screen;

    private readonly LandscapeGenerator _landscape;
    private readonly ObjectMap _objectMap;
    private readonly PlayerController _player;
    private readonly ParticleSystem _particles;
    private readonly TriangleRasterizer _rasterizer;
    private readonly byte[] _framebuffer;

    // Landscape rendering state
    private (int x, int y)[] _prevRowCorners;  // Projected corners from the previous row
    private (int x, int y)[] _curRowCorners;   // Projected corners for the current row

    public GameState State => _state;
    public LandscapeGenerator Landscape => _landscape;
    public ObjectMap ObjectMap => _objectMap;

    public GameEngine(IRandomSource random, IScreen screen)
    {
        _state = new GameState();
        _buffers = new GraphicsBuffers();
        _random = random;
        _screen = screen;

        _landscape = new LandscapeGenerator(_state);
        _objectMap = new ObjectMap(_landscape, _random);
        _player = new PlayerController(_state, _buffers, _landscape);
        _particles = new ParticleSystem(_state, _landscape, _objectMap, _buffers, _random);

        _framebuffer = new byte[320 * 240];
        _rasterizer = new TriangleRasterizer(_framebuffer);

        _prevRowCorners = new (int, int)[FixedPoint.TILES_X];
        _curRowCorners = new (int, int)[FixedPoint.TILES_X];
    }

    public void StartNewGame()
    {
        _state.Initialize();
        _objectMap.PlaceObjects();
        _state.PlaceOnLaunchpad();
        _buffers.Clear();
        _rasterizer.Clear(0);
    }

    public bool Update(IGameInput input)
    {
        if (input.EscapePressed) return false;

        // Crash animation loop (30 frames of explosion after player crash)
        if (_state.CrashLoopCount > 0)
        {
            _particles.UpdateAndDraw();
            DrawVisibleObjects();
            _buffers.AddTerminators();
            DrawLandscapeAndBuffers();
            RenderScoreBar();
            CopyToScreen();
            _rasterizer.Clear(0);
            _buffers.Clear();
            _state.MainLoopCount++;
            _state.CrashLoopCount--;

            if (_state.CrashLoopCount == 0)
            {
                _state.RemainingLives--;
                if (_state.RemainingLives <= 0)
                    StartNewGame();
                else
                    _state.PlaceOnLaunchpad();
            }
            return true;
        }

        // 1. Player update (input → physics → collision → draw ship into buffers)
        if (!_player.Update(input))
        {
            TriggerCrash();
            return true;
        }


        _state.UpdateGravity();

        // 2. Exhaust particles if engines firing (bit 1 = hover, bit 2 = full thrust).
        // Bit 0 is the fire key — must NOT trigger exhaust.
        if ((_state.FuelBurnRate & 6) != 0)
            _particles.SpawnExhaust(_state.XPlayer, _state.YPlayer, _state.ZPlayer,
                _state.XVelocity, _state.YVelocity, _state.ZVelocity);

        // 3. Bullet firing if fire button pressed
        if (input.Fire)
            _particles.SpawnBullet(_state.XPlayer, _state.YPlayer, _state.ZPlayer,
                _state.XVelocity, _state.YVelocity, _state.ZVelocity,
                _state.XNoseV, _state.YNoseV, _state.ZNoseV);

        // 4. Random rock dropping if score >= 800 (Lander.arm:4570-4630)
        if (_state.PlayingGame != 0)
            DropRocksFromTheSky();

        // 5. Particles update + draw into buffers
        _particles.UpdateAndDraw();

        // 5. Draw objects (trees, buildings) into buffers
        DrawVisibleObjects();


        // 5. Terminate buffers
        _buffers.AddTerminators();

        // 6. Draw landscape + buffer contents back-to-front
        DrawLandscapeAndBuffers();

        // 7. Output play area to screen buffer
        CopyToScreen();

        // 8. Draw HUD score bar onto top 16 rows of screen buffer
        RenderScoreBar();

        // 9. Clear for next frame
        _rasterizer.Clear(0);
        _buffers.Clear();
        _state.MainLoopCount++;

        return true;
    }

    // ---- Object drawing ----

    private void DrawVisibleObjects()
    {
        int camTileX = (_state.XCamera - FixedPoint.LANDSCAPE_X) & unchecked((int)0xFF000000);
        int camTileZ = _state.ZCamera & unchecked((int)0xFF000000);

        for (int tz = 0; tz < FixedPoint.TILES_Z; tz++)
        {
            int worldZ = (camTileZ - tz * FixedPoint.TILE_SIZE) & unchecked((int)0xFF000000);
            for (int tx = 0; tx < FixedPoint.TILES_X; tx++)
            {
                int worldX = (camTileX + tx * FixedPoint.TILE_SIZE) & unchecked((int)0xFF000000);
                int objType = _objectMap.GetObjectAt(worldX, worldZ);
                if (objType == ObjectTypes.NO_OBJECT) continue;

                var blueprint = ObjectTypes.GetBlueprint(objType);
                if (blueprint == null) continue;

                int objX = worldX - _state.XCamera;
                int objY = _landscape.GetAltitude(worldX, worldZ) - _state.YCamera;
                // Screen-depth z: maps worldZ to positive projection distance
                // At camera back: z ≈ LANDSCAPE_Z; at player: z = LANDSCAPE_Z_MID
                int objZ = FixedPoint.LANDSCAPE_Z - _state.ZCamera + worldZ;

                ObjectRenderer.DrawObject(blueprint, objX, objY, objZ, worldX, worldZ, _state, _buffers, _landscape);
            }
        }
    }

    /// <summary>
    /// Randomly drop rocks from the sky if score >= 800 (Lander.arm:4570-4630).
    /// </summary>
    private void DropRocksFromTheSky()
    {
        if (_state.CurrentScore < 800) return;

        int scoreDelta = _state.CurrentScore - 800;

        var (rand0, rand1) = _random.GetRandomNumbers();
        int r0 = (int)((uint)rand0 >> 18); // 0..16383

        if (r0 < scoreDelta)
        {
            int x = _state.XCamera;
            int y = -(FixedPoint.ROCK_HEIGHT + 1);
            int z = _state.ZCamera - FixedPoint.PLAYER_FRONT_Z;

            _particles.DropRock(x, y, z);
        }
    }

    // ---- Landscape and buffer drawing (back to front) ----

    private void DrawLandscapeAndBuffers()
    {
        // Camera-aligned tile positions
        int xCameraTile = _state.XCamera & unchecked((int)0xFF000000);
        int zCameraTile = _state.ZCamera & unchecked((int)0xFF000000);
        int zFrac = _state.ZCamera - zCameraTile;

        // Starting x for the back-left corner:
        // worldX = xCameraTile - LANDSCAPE_X (then + col * TILE_SIZE in the loop)
        int startX = xCameraTile - FixedPoint.LANDSCAPE_X;

        // Starting z for the back row (LANDSCAPE_Z - fractional part of camera z)
        // This is the z value used for PROJECTION, not the world z for altitude
        int zRow = FixedPoint.LANDSCAPE_Z - zFrac;

        // World z for altitude lookup (starts at camera tile z, goes down each row)
        int worldZBase = zCameraTile;

        // For each tile corner row (back to front)
        for (int row = 0; row < FixedPoint.TILES_Z; row++)
        {
            _state.TileCornerRow = row;

            // Projection z for this row (decreases by TILE_SIZE each row toward front)
            int projZ = zRow;

            // For each tile corner column (left to right)
            for (int col = 0; col < FixedPoint.TILES_X; col++)
            {
                int worldX = startX + col * FixedPoint.TILE_SIZE;
                int worldZ = worldZBase;

                // Get altitude at this corner (using world coordinates)
                int alt = _landscape.GetAltitude(worldX, worldZ);

                // Camera-relative x and y (z is the landscape row z, not world z)
                int relX = worldX - _state.XCamera;
                int relY = alt - _state.YCamera;

                // Project using the landscape row z (NOT worldZ - zCamera)
                if (!Projection.Project(relX, relY, projZ, out int sx, out int sy))
                {
                    _curRowCorners[col] = (-1, -1);
                    continue;
                }
                _curRowCorners[col] = (sx, sy);

                // Draw tile if we have all 4 corners (past first row and column)
                if (row > 0 && col > 0)
                {
                    var c00 = _prevRowCorners[col - 1];
                    var c10 = _prevRowCorners[col];
                    var c01 = _curRowCorners[col - 1];
                    var c11 = _curRowCorners[col];

                    if (c00.x < 0 || c10.x < 0 || c01.x < 0 || c11.x < 0) continue;

                    int colour = _landscape.GetTileColour(row);
                    byte vidc = (byte)(colour & 0xFF);

                    _rasterizer.DrawTriangle(c00.x, c00.y, c10.x, c10.y, c11.x, c11.y, vidc);
                    _rasterizer.DrawTriangle(c00.x, c00.y, c11.x, c11.y, c01.x, c01.y, vidc);
                }
            }

            // Swap corner arrays
            var temp = _prevRowCorners;
            _prevRowCorners = _curRowCorners;
            _curRowCorners = temp;

            // Advance to next row: zRow and worldZBase decrease by one tile
            zRow -= FixedPoint.TILE_SIZE;
            worldZBase -= FixedPoint.TILE_SIZE;

            // Draw graphics buffer for objects 2 rows behind
            if (row >= 2)
                DrawBuffer(row - 2);
        }

        // Draw remaining buffers
        for (int b = FixedPoint.TILES_Z - 1; b < FixedPoint.GRAPHICS_BUFFER_COUNT; b++)
            DrawBuffer(b);
    }

    // ---- Buffer rendering to framebuffer ----

    private void DrawBuffer(int bufferIndex)
    {
        var data = _buffers.GetBufferData(bufferIndex);
        int i = 0;
        while (i < data.Length)
        {
            int cmd = data[i];
            if (cmd == GraphicsBuffers.COMMAND_TERMINATOR) break;

            if (cmd == GraphicsBuffers.COMMAND_TRIANGLE && i + 7 < data.Length)
            {
                _rasterizer.DrawTriangle(
                    data[i + 1], data[i + 2],
                    data[i + 3], data[i + 4],
                    data[i + 5], data[i + 6],
                    (byte)(data[i + 7] & 0xFF));
                i += 8;
            }
            else if (cmd <= 17 && i + 1 < data.Length)
            {
                int packed = data[i + 1];
                int px = (packed >> 20) & 0xFFF;
                int py = packed & 0xFF;

                int w, h;
                byte colour;

                if (cmd <= 8)
                {
                    colour = (byte)((packed >> 12) & 0xFF);
                    if (cmd <= 5) { w = 3; h = 2; }
                    else if (cmd == 6) { w = 2; h = 2; }
                    else if (cmd == 7) { w = 2; h = 1; }
                    else { w = 1; h = 1; }
                }
                else
                {
                    colour = 0;  // Shadow particle (black)
                    if (cmd <= 14) { w = 3; h = 1; }
                    else if (cmd <= 16) { w = 2; h = 1; }
                    else { w = 1; h = 1; }
                }

                int startDx = -(w / 2);
                int startDy = -(h / 2);
                for (int dy = 0; dy < h; dy++)
                    for (int dx = 0; dx < w; dx++)
                    {
                        int sx = px + startDx + dx, sy = py + startDy + dy;
                        if ((uint)sx < 320 && (uint)sy < 240)
                            _framebuffer[sy * 320 + sx] = colour;
                    }
                i += 2;
            }
            else i++;
        }
    }


    // ---- Score bar and screen output ----

    private void RenderScoreBar()
    {
        var screenBuf = _screen.GetFramebuffer();
        int stride = _screen.Width;

        // 1. Fuel level bar on rows 2-4 (pixel y = 2, 3, 4)
        int fuelPixels = _state.FuelLevel >> 4;
        if (fuelPixels > 320) fuelPixels = 320;
        byte fuelColor = VidcColour.Encode(12, 8, 0); // Orange fuel bar
        int len = global::System.Math.Min(fuelPixels, stride);
        if (len > 0)
        {
            screenBuf.Slice(2 * stride, len).Fill(fuelColor);
            screenBuf.Slice(3 * stride, len).Fill(fuelColor);
            screenBuf.Slice(4 * stride, len).Fill(fuelColor);
        }

        // 2. Text header on text row 1 (pixel y = 8)
        byte white = VidcColour.Encode(15, 15, 15);
        byte yellow = VidcColour.Encode(15, 15, 0);
        byte cyan = VidcColour.Encode(0, 15, 15);

        // Col 0: Bullet count / current score
        string scoreStr = _state.CurrentScore.ToString();
        SystemFont.DrawString(screenBuf, stride, 0, 8, scoreStr, white);

        // Col 30 (x = 240): Remaining lives
        string livesStr = _state.RemainingLives.ToString();
        SystemFont.DrawString(screenBuf, stride, 240, 8, livesStr, yellow);

        // Col 35 (x = 280): High score
        string highStr = _state.HighScore.ToString();
        SystemFont.DrawString(screenBuf, stride, 280, 8, highStr, cyan);

        // 3. Game Over text message when lives <= 0 (middle of play area, y = 128)
        if (_state.RemainingLives <= 0)
        {
            byte red = VidcColour.Encode(15, 0, 0);
            SystemFont.DrawString(screenBuf, stride, 120, 128, "GAME OVER", red);
        }
    }

    private void CopyToScreen()
    {
        var screenBuf = _screen.GetFramebuffer();
        int stride = _screen.Width;

        // Clear top 16 rows (score bar HUD area, y = 0..15)
        screenBuf.Slice(0, 16 * stride).Clear();

        // Copy 240-row 3D play area to rows 16..255 of screen buffer
        for (int y = 0; y < 240; y++)
        {
            _framebuffer.AsSpan(y * 320, global::System.Math.Min(320, stride))
                .CopyTo(screenBuf.Slice((y + 16) * stride, global::System.Math.Min(320, stride)));
        }
    }

    private void TriggerCrash()
    {
        _state.PlayingGame = 0;
        _state.CrashLoopCount = 30;

        // Lander.arm:2612: 5/16 tile sizes above player ship
        int crashY = _state.YPlayer - (FixedPoint.TILE_SIZE * 5 / 16);
        _particles.AddBigExplosion(_state.XPlayer, crashY, _state.ZPlayer, 81);
    }
}

