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

        // 2. Exhaust particles if engines firing
        if (_state.FuelBurnRate != 0)
            _particles.SpawnExhaust(_state.XPlayer, _state.YPlayer, _state.ZPlayer,
                _state.XVelocity, _state.YVelocity, _state.ZVelocity);

        // 3. Bullet firing if fire button pressed
        if (input.Fire)
            _particles.SpawnBullet(_state.XPlayer, _state.YPlayer, _state.ZPlayer,
                _state.XVelocity, _state.YVelocity, _state.ZVelocity,
                _state.XNoseV, _state.YNoseV, _state.ZNoseV);


        // 4. Particles update + draw into buffers
        _particles.UpdateAndDraw();

        // 5. Draw objects (trees, buildings) into buffers
        DrawVisibleObjects();


        // 5. Terminate buffers
        _buffers.AddTerminators();

        // 6. Draw landscape + buffer contents back-to-front
        DrawLandscapeAndBuffers();

        // 7. Score bar
        RenderScoreBar();

        // 8. Output to screen
        CopyToScreen();

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

                DrawObject(blueprint, objX, objY, objZ);
            }
        }
    }

    private void DrawObject(ObjectBlueprint blueprint, int objX, int objY, int objZ)
    {
        bool rotates = blueprint.Rotates;

        // Reconstruct world position for shadow computation
        int worldObjX = objX + _state.XCamera;
        int worldObjZ = objZ - FixedPoint.LANDSCAPE_Z + _state.ZCamera;

        foreach (var face in blueprint.Faces)
        {
            // Back-face culling for rotating objects only
            if (rotates)
            {
                // Dot product of camera→object with face normal
                int dot = objX * face.Normal.X + objY * face.Normal.Y + objZ * face.Normal.Z;
                if (dot >= 0) continue;  // Facing away
            }

            var v1 = blueprint.Vertices[face.V1];
            var v2 = blueprint.Vertices[face.V2];
            var v3 = blueprint.Vertices[face.V3];

            // Rotate vertices for rotating objects
            int rx1, ry1, rz1, rx2, ry2, rz2, rx3, ry3, rz3;
            if (rotates)
            {
                rx1 = DotMatrix(v1.X, v1.Y, v1.Z, 0); ry1 = DotMatrix(v1.X, v1.Y, v1.Z, 1); rz1 = DotMatrix(v1.X, v1.Y, v1.Z, 2);
                rx2 = DotMatrix(v2.X, v2.Y, v2.Z, 0); ry2 = DotMatrix(v2.X, v2.Y, v2.Z, 1); rz2 = DotMatrix(v2.X, v2.Y, v2.Z, 2);
                rx3 = DotMatrix(v3.X, v3.Y, v3.Z, 0); ry3 = DotMatrix(v3.X, v3.Y, v3.Z, 1); rz3 = DotMatrix(v3.X, v3.Y, v3.Z, 2);
            }
            else
            {
                rx1 = v1.X; ry1 = v1.Y; rz1 = v1.Z;
                rx2 = v2.X; ry2 = v2.Y; rz2 = v2.Z;
                rx3 = v3.X; ry3 = v3.Y; rz3 = v3.Z;
            }

            // World space
            int wx1 = rx1 + objX, wy1 = ry1 + objY, wz1 = rz1 + objZ;
            int wx2 = rx2 + objX, wy2 = ry2 + objY, wz2 = rz2 + objZ;
            int wx3 = rx3 + objX, wy3 = ry3 + objY, wz3 = rz3 + objZ;

            // Project
            if (!Projection.Project(wx1, wy1, wz1, out int sx1, out int sy1)) continue;
            if (!Projection.Project(wx2, wy2, wz2, out int sx2, out int sy2)) continue;
            if (!Projection.Project(wx3, wy3, wz3, out int sx3, out int sy3)) continue;

            // Shading: brightness from face normal (light above-left)
            // ALWAYS computed from yNormal — not conditional on sign
            int shade = (int)((0x80000000u - (uint)face.Normal.Y) >> 28);
            if (face.Normal.X < 0) shade++;
            shade = global::System.Math.Max(0, shade - 5);
            if (shade > 3) shade = 3;
            int r = global::System.Math.Min(((face.Colour >> 8) & 0xF) + shade, 15);
            int g = global::System.Math.Min(((face.Colour >> 4) & 0xF) + shade, 15);
            int b = global::System.Math.Min((face.Colour & 0xF) + shade, 15);

            byte vidc = VidcColour.Encode(r, g, b);
            int colourWord = VidcColour.ReplicateQuad(vidc);

            int bufIdx = _buffers.GetBufferIndex(objZ);
            _buffers.AddTriangle(bufIdx, sx1, sy1, sx2, sy2, sx3, sy3, colourWord);

            // Shadow: project vertices from ground level (landscape altitude)
            if (blueprint.HasShadow)
            {
                // Reconstruct worldZ from screen-depth z: worldZ = objZ - LANDSCAPE_Z + zCamera
                int objWorldZ = objZ - FixedPoint.LANDSCAPE_Z + _state.ZCamera;

                int worldVX1 = worldObjX + rx1;
                int worldVZ1 = worldObjZ + rz1;
                int worldVX2 = worldObjX + rx2;
                int worldVZ2 = worldObjZ + rz2;
                int worldVX3 = worldObjX + rx3;
                int worldVZ3 = worldObjZ + rz3;

                int alt1 = _landscape.GetAltitude(worldVX1, worldVZ1);
                int alt2 = _landscape.GetAltitude(worldVX2, worldVZ2);
                int alt3 = _landscape.GetAltitude(worldVX3, worldVZ3);

                int shRelY1 = alt1 - _state.YCamera;
                int shRelY2 = alt2 - _state.YCamera;
                int shRelY3 = alt3 - _state.YCamera;

                if (Projection.Project(wx1, shRelY1, wz1, out int shx1, out int shy1) &&
                    Projection.Project(wx2, shRelY2, wz2, out int shx2, out int shy2) &&
                    Projection.Project(wx3, shRelY3, wz3, out int shx3, out int shy3))
                {
                    int shIdx = _buffers.GetShadowBufferIndex(objZ);
                    _buffers.AddTriangle(shIdx, shx1, shy1, shx2, shy2, shx3, shy3, 0);
                }
            }
        }
    }

    private int DotMatrix(int x, int y, int z, int row)
    {
        // Matrix is stored row-major: row 0 = (xNoseV, xRoofV, xSideV), etc.
        // Each row dot with the vertex gives the rotated component.
        int mx, my, mz;
        switch (row)
        {
            case 0: mx = _state.XNoseV; my = _state.XRoofV; mz = _state.XSideV; break;
            case 1: mx = _state.YNoseV; my = _state.YRoofV; mz = _state.YSideV; break;
            default: mx = _state.ZNoseV; my = _state.ZRoofV; mz = _state.ZSideV; break;
        }
        return (int)(((long)x * mx + (long)y * my + (long)z * mz) >> 31);
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
        // 1. Fuel level bar on rows 2-4
        int fuelPixels = _state.FuelLevel >> 4;
        if (fuelPixels > 320) fuelPixels = 320;
        byte fuelColor = VidcColour.Encode(12, 8, 0); // Orange fuel bar
        int len = global::System.Math.Min(fuelPixels, 320);
        if (len > 0)
        {
            _framebuffer.AsSpan(2 * 320, len).Fill(fuelColor);
            _framebuffer.AsSpan(3 * 320, len).Fill(fuelColor);
            _framebuffer.AsSpan(4 * 320, len).Fill(fuelColor);
        }

        // 2. Text header on text row 1 (pixel y = 8)
        byte white = VidcColour.Encode(15, 15, 15);
        byte yellow = VidcColour.Encode(15, 15, 0);
        byte cyan = VidcColour.Encode(0, 15, 15);

        // Col 0: Bullet count / current score
        string scoreStr = _state.CurrentScore.ToString();
        SystemFont.DrawString(_framebuffer, 320, 0, 8, scoreStr, white);

        // Col 30 (x = 240): Remaining lives
        string livesStr = _state.RemainingLives.ToString();
        SystemFont.DrawString(_framebuffer, 320, 240, 8, livesStr, yellow);

        // Col 35 (x = 280): High score
        string highStr = _state.HighScore.ToString();
        SystemFont.DrawString(_framebuffer, 320, 280, 8, highStr, cyan);

        // 3. Game Over text message when lives <= 0
        if (_state.RemainingLives <= 0)
        {
            byte red = VidcColour.Encode(15, 0, 0);
            SystemFont.DrawString(_framebuffer, 320, 120, 112, "GAME OVER", red);
        }
    }



    private void CopyToScreen()
    {
        var screenBuf = _screen.GetFramebuffer();
        int stride = _screen.Width;
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

