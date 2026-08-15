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

    private ViewConfig _viewConfig;
    private int _viewDepthIndex;
    private int _viewWidthIndex;

    /// <summary>View depth presets cycled by the C key (extra tile rows beyond the original grid).</summary>
    private static readonly int[] ViewDepthPresets = { 0, 4, 8, 12 };

    /// <summary>View width presets cycled by the X key (extra tile columns per side).</summary>
    private static readonly int[] ViewWidthPresets = { 0, 4, 8, 12 };

    private readonly LandscapeGenerator _landscape;
    private readonly ObjectMap _objectMap;
    private readonly PlayerController _player;
    private readonly ParticleSystem _particles;
    private readonly TriangleRasterizer _rasterizer;
    private readonly byte[] _framebuffer;
    private readonly int _playWidth;
    private readonly int _playHeight;

    // Landscape rendering state
    private (int x, int y)[] _prevRowCorners;  // Projected corners from the previous row
    private (int x, int y)[] _curRowCorners;   // Projected corners for the current row

    public GameState State => _state;
    public LandscapeGenerator Landscape => _landscape;
    public ObjectMap ObjectMap => _objectMap;
    public ParticleSystem Particles => _particles;

    /// <summary>Current number of extra view-depth rows (0 = original view).</summary>
    public int ExtraDepthTiles => _viewConfig.ExtraDepthTiles;

    /// <summary>Current number of extra view-width columns per side (0 = original view).</summary>
    public int ExtraWidthCols => _viewConfig.ExtraWidthCols;

    /// <summary>
    /// Re-apply the view configuration to the subsystems that hold a copy of it
    /// (graphics buffers, particle side-culling, corner stores). Must be called
    /// between frames — the buffers are rebuilt in place, dropping in-flight
    /// commands.
    /// </summary>
    private void ApplyViewConfig()
    {
        _buffers.Resize(_viewConfig.GraphicsBufferCount,
            _viewConfig.LandscapeZ, _viewConfig.LandscapeZDepth, _viewConfig.LandscapeZBeyond);
        _particles.LandscapeXHalf = _viewConfig.LandscapeXHalf;
        _prevRowCorners = new (int, int)[_viewConfig.TilesX];
        _curRowCorners = new (int, int)[_viewConfig.TilesX];
    }

    /// <summary>Set the extended view depth (0 = original), keeping the width.</summary>
    public void SetExtraDepth(int extraDepthTiles)
    {
        _viewConfig = new ViewConfig(extraDepthTiles, _viewConfig.ExtraWidthCols);
        ApplyViewConfig();
    }

    /// <summary>Set the extended view width in columns per side (0 = original), keeping the depth.</summary>
    public void SetExtraWidth(int extraWidthCols)
    {
        _viewConfig = new ViewConfig(_viewConfig.ExtraDepthTiles, extraWidthCols);
        ApplyViewConfig();
    }

    /// <summary>
    /// Cycle the view depth presets: original → +4 → +8 → +12 → original
    /// (C key, a deliberate deviation from the original — opt-in only).
    /// </summary>
    public void CycleViewDepth()
    {
        _viewDepthIndex = (_viewDepthIndex + 1) % ViewDepthPresets.Length;
        SetExtraDepth(ViewDepthPresets[_viewDepthIndex]);
    }

    /// <summary>
    /// Cycle the view width presets: original → +4 → +8 → +12 columns per side
    /// → original (X key, opt-in like the depth extension).
    /// </summary>
    public void CycleViewWidth()
    {
        _viewWidthIndex = (_viewWidthIndex + 1) % ViewWidthPresets.Length;
        SetExtraWidth(ViewWidthPresets[_viewWidthIndex]);
    }

    /// <summary>
    /// Toggle the HUD coordinate display (P key, opt-in — off by default).
    /// </summary>
    public void ToggleCoords()
    {
        _state.ShowCoords = !_state.ShowCoords;
    }

    public GameEngine(IRandomSource random, IScreen screen, ViewConfig? viewConfig = null)
    {
        _state = new GameState();
        _viewConfig = viewConfig ?? new ViewConfig(0);
        _viewDepthIndex = global::System.Math.Max(0, Array.IndexOf(ViewDepthPresets, _viewConfig.ExtraDepthTiles));
        _viewWidthIndex = global::System.Math.Max(0, Array.IndexOf(ViewWidthPresets, _viewConfig.ExtraWidthCols));
        _buffers = new GraphicsBuffers(_viewConfig.GraphicsBufferCount, FixedPoint.BUFFER_SIZE / 4,
            _viewConfig.LandscapeZ, _viewConfig.LandscapeZDepth, _viewConfig.LandscapeZBeyond);
        _random = random;
        _screen = screen;

        _landscape = new LandscapeGenerator(_state);
        _objectMap = new ObjectMap(_landscape, _random);
        _player = new PlayerController(_state, _buffers, _landscape, _objectMap);
        _particles = new ParticleSystem(_state, _landscape, _objectMap, _buffers, _random);

        // Play area: the full screen minus the 16-row score bar (320×240 in
        // the original; the --widescreen frontend passes a 456×256 screen).
        _playWidth = screen.Width;
        _playHeight = screen.Height - FixedPoint.SCORE_BAR_HEIGHT;
        Viewport.Configure(_playWidth, _playHeight);

        _framebuffer = new byte[_playWidth * _playHeight];
        _rasterizer = new TriangleRasterizer(_framebuffer, _playWidth, _playHeight);

        _prevRowCorners = new (int, int)[_viewConfig.TilesX];
        _curRowCorners = new (int, int)[_viewConfig.TilesX];
    }

    public void StartNewGame()
    {
        // High score: max(highScore, currentScore) at game start — the original
        // compares and stores here (Lander.arm:12218-12230), not per-frame, so
        // a new high score only appears once the next game begins
        if (_state.CurrentScore >= _state.HighScore)
            _state.HighScore = _state.CurrentScore;

        _state.Initialize();
        _objectMap.PlaceObjects();
        _state.PlaceOnLaunchpad();
        _buffers.Clear();
        _rasterizer.Clear(0);
    }

    public bool Update(IGameInput input)
    {
        if (input.EscapePressed) return false;

        // Game over: the original prints "GAME OVER - press a key to start
        // again" to both banks and blocks in OS_ReadC until any key is pressed,
        // then starts a brand new game (Lander.arm:2696-2744).
        if (_state.PlayingGame == -2)
        {
            CopyToScreen();
            RenderScoreBar();
            var gameOverBuf = _screen.GetFramebuffer();
            const string msg = "GAME OVER - press a key to start again";
            int msgX = (_playWidth - msg.Length * SystemFont.CHAR_WIDTH) / 2;  // 8 at 320, centred when wide
            SystemFont.DrawString(gameOverBuf, _screen.Width, msgX, 128, msg,
                VidcColour.Encode(15, 15, 15));
            if (input.AnyKeyPressed)
                StartNewGame();
            return true;
        }

        // Crash animation loop: 31 frames of explosion after player crash
        // (the original's SUBS/BPL loop with R8 = 30 runs the body for 30..0).
        // PlayingGame == 0 is the crash state (TriggerCrash sets it; the count
        // is the loop's R8 counter), as in the original.
        if (_state.PlayingGame == 0)
        {
            // The original's crash loop calls PrintCurrentScore every iteration
            // (Lander.arm:2661-2665), which updates gravity from the score — the
            // explosion particles fall with current gravity during the animation.
            _state.UpdateGravity();
            _particles.UpdateAndDraw();
            DrawVisibleObjects();
            _buffers.AddTerminators();
            DrawLandscapeAndBuffers();
            CopyToScreen();
            RenderScoreBar();
            _rasterizer.Clear(0);
            _buffers.Clear();
            _state.MainLoopCount++;
            _state.CrashLoopCount--;

            // The original's crash loop runs 31 iterations: SUBS R8, R8, #1 /
            // BPL with R8 = 30 (Lander.arm:2671-2676) executes the body for
            // R8 = 30 down to 0, and only then falls through to LoseLife. The
            // previous == 0 check cut the animation one frame short.
            if (_state.CrashLoopCount < 0)
            {
                _state.RemainingLives--;
                if (_state.RemainingLives <= 0)
                    _state.PlayingGame = -2;  // Game over: show the message and wait for a key
                else
                    _state.PlaceOnLaunchpad();
            }
            return true;
        }

        // Map mode toggle (TAB or R key)
        if (input.ToggleMap)
        {
            _state.MapMode = (_state.MapMode + 1) % 3;
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

        // 3. Bullet firing: gated on the fire bit of the burn rate. When fuel
        // runs out ReadKeyboardInput zeroes the whole burn rate (including bit 0,
        // Lander.arm:1771-1773), and the original fires only on
        // TST R10, #%00000001 (Lander.arm:2379) — so no bullets at zero fuel.
        if ((_state.FuelBurnRate & 1) != 0)
            _particles.SpawnBullet(_state.XPlayer, _state.YPlayer, _state.ZPlayer,
                _state.XVelocity, _state.YVelocity, _state.ZVelocity,
                _state.XNoseV, _state.YNoseV, _state.ZNoseV);

        // Rock rotation matrix from the main loop counter (Lander.arm:12507-12518):
        // the original overwrites the shared matrix with rock-spin angles here,
        // after the ship has been drawn and before the rocks are
        _player.ComputeRockRotationMatrix();

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

        // 9. Render mini-map radar overlay
        var screenBuf = _screen.GetFramebuffer();
        MinimapRenderer.Render(screenBuf, _screen.Width, _state, _landscape, _objectMap, _particles);

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

        // Negative tz covers the extended far band: worldZ = camTileZ + k for
        // k = 1..ExtraDepthTiles. The objZ formula below stays correct across
        // the whole extended grid (projZ = worldZ - ZCamera + LANDSCAPE_Z).
        for (int tz = -_viewConfig.ExtraDepthTiles; tz < FixedPoint.TILES_Z; tz++)
        {
            int worldZ = (camTileZ - tz * FixedPoint.TILE_SIZE) & unchecked((int)0xFF000000);
            // tx - ExtraWidthCols keeps the original columns at their exact
            // world positions; the extra columns widen the window each side.
            for (int tx = 0; tx < _viewConfig.TilesX; tx++)
            {
                int worldX = (camTileX + (tx - _viewConfig.ExtraWidthCols) * FixedPoint.TILE_SIZE)
                    & unchecked((int)0xFF000000);
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
        if (_state.CurrentScore < FixedPoint.ROCK_SCORE_THRESHOLD) return;

        int scoreDelta = _state.CurrentScore - FixedPoint.ROCK_SCORE_THRESHOLD;

        var (rand0, rand1) = _random.GetRandomNumbers();
        int r0 = (int)((uint)rand0 >> 18); // 0..16383

        if (r0 < scoreDelta)
        {
            int x = _state.XCamera;
            // Fixed world altitude very high in the sky: ~ROCK_HEIGHT, i.e.
            // -(ROCK_HEIGHT + 1) = -(32 tiles + 1) (Lander.arm:4612-4620:
            // MVN R1, #ROCK_HEIGHT) — not relative to the player's altitude
            int y = ~FixedPoint.ROCK_HEIGHT;
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
        // This is the z value used for PROJECTION, not the world z for altitude.
        // The extended view adds ExtraDepthTiles rows at the far end: the back
        // row moves back by N tiles and samples terrain behind the camera tile
        // (worldZ = zCameraTile + (N - row) * TILE_SIZE). The original rows keep
        // their exact projection z, world z and colours — projZ = worldZ -
        // ZCamera + LANDSCAPE_Z stays linear across the whole grid.
        int zRow = _viewConfig.LandscapeZ - zFrac;

        // World z for altitude lookup (starts N tiles behind the camera tile,
        // goes down one tile per row)
        int worldZBase = zCameraTile + _viewConfig.ExtraDepthTiles * FixedPoint.TILE_SIZE;

        // For each tile corner row (back to front)
        for (int row = 0; row < _viewConfig.TilesZ; row++)
        {
            _state.TileCornerRow = row;

            // Projection z for this row (decreases by TILE_SIZE each row toward front)
            int projZ = zRow;

            // For each tile corner column (left to right). The extended width
            // adds ExtraWidthCols columns on each side; col 0..TILES_X-1 keep
            // their exact world positions (worldX = startX + (col - M) * TILE_SIZE
            // degenerates to the original expression when M = 0).
            for (int col = 0; col < _viewConfig.TilesX; col++)
            {
                int worldX = startX + (col - _viewConfig.ExtraWidthCols) * FixedPoint.TILE_SIZE;
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

                    // Brightness row: the original ramp only covers rows 0-10
                    // counting from the original back edge. The extension rows
                    // use the darkest shade and the original rows keep their
                    // ramp (BigLander's fix: SUBS R8, R8, #TILES_Z-11 / MOVLT R8, #0).
                    int colour = _landscape.GetTileColour(_viewConfig.MapTileCornerRow(row));
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

        // Draw remaining buffers: the penultimate and last buffers
        // (Lander.arm:1216-1222 draws TILES_Z - 2 = 9 and TILES_Z - 1 = 10).
        // Buffer indices clamp at LANDSCAPE_Z_DEPTH (10), so buffer 11 is never
        // populated; drawing it instead of buffer 9 left all objects on the 9th
        // z-row invisible.
        //
        // In the extended view, buffer 0 can legitimately hold shadows of the
        // far-band objects (Lander.arm culls them because z > 20 never occurs);
        // it is drawn after row 2, the same 2-row latency as the original.
        DrawBuffer(_viewConfig.TilesZ - 2);
        DrawBuffer(_viewConfig.TilesZ - 1);
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

                // The original anchors blocks at the particle coordinate: 3-wide
                // blocks extend x-1..x+1 (STRB at offsets -1, 0, +1 in
                // Draw3x2ParticleFromBuffer, Lander.arm:8072-8073), all other
                // widths start at x, and y is always the top row. The previous
                // centring drew 2x1/2x2 particles one pixel left and 2x2/3x2
                // particles one pixel up.
                int startDx = (w == 3) ? -1 : 0;
                int startDy = 0;
                for (int dy = 0; dy < h; dy++)
                    for (int dx = 0; dx < w; dx++)
                    {
                        int sx = px + startDx + dx, sy = py + startDy + dy;
                        if ((uint)sx < _playWidth && (uint)sy < _playHeight)
                            _framebuffer[sy * _playWidth + sx] = colour;
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

        // 0. Title on text row 0 (Lander.arm:12157-12161, 12191-12195): the
        // original prints it to both banks at entry and it remains for the
        // whole game.
        SystemFont.DrawString(screenBuf, stride, 0, 0,
            "Lander Demo/Practice (C) D.J.Braben 1987",
            VidcColour.Encode(15, 15, 15));

        // 1. Fuel level bar at the top of the play area: screen rows 17-19
        // (Lander.arm:5884-5954: screenAddr points 16 rows down past the two
        // text rows, and the bar rows are drawn at +320, +2*320, +3*320), in
        // the fuelBarColour &37373737 (EQUD at Lander.arm:5829-5831). The
        // previous orange bar at rows 2-4 sat inside the HUD instead.
        int fuelPixels = _state.FuelLevel >> 4;
        if (fuelPixels > _playWidth) fuelPixels = _playWidth;
        byte fuelColor = 0x37;
        int len = global::System.Math.Min(fuelPixels, stride);
        if (len > 0)
        {
            screenBuf.Slice(17 * stride, len).Fill(fuelColor);
            screenBuf.Slice(18 * stride, len).Fill(fuelColor);
            screenBuf.Slice(19 * stride, len).Fill(fuelColor);
        }

        // 2. Text header on text row 1 (pixel y = 8). The original prints all
        // three values through PrintScoreInBothBanks in the default VDU
        // foreground (white) — no per-value colours.
        byte white = VidcColour.Encode(15, 15, 15);

        // Col 0: Bullet count / current score
        string scoreStr = _state.CurrentScore.ToString();
        SystemFont.DrawString(screenBuf, stride, 0, 8, scoreStr, white);

        // Col 30 (x = 240): Remaining lives
        string livesStr = _state.RemainingLives.ToString();
        SystemFont.DrawString(screenBuf, stride, _playWidth - 80, 8, livesStr, white);

        // Col 35 (x = 280): High score
        string highStr = _state.HighScore.ToString();
        SystemFont.DrawString(screenBuf, stride, _playWidth - 40, 8, highStr, white);

        // 3. Coordinate display (P key, opt-in — no original counterpart): ship
        // position in the empty middle of text row 1, in player-facing terms —
        // X and Y are the ground axes (world X/Z, wrapped with the 256-tile
        // periodic world so the launchpad always reads as 0..8) and Alt is the
        // height above the terrain below the ship (positive up).
        if (_state.ShowCoords)
        {
            int groundAltitude = _landscape.GetAltitude(_state.XPlayer, _state.ZPlayer);
            SystemFont.DrawString(screenBuf, stride, 40, 8,
                CoordDisplay.FormatHud(_state.XPlayer, _state.ZPlayer, _state.YPlayer, groundAltitude), white);
        }
    }

    private void CopyToScreen()
    {
        var screenBuf = _screen.GetFramebuffer();
        int stride = _screen.Width;

        // Clear top 16 rows (score bar HUD area, y = 0..15)
        screenBuf.Slice(0, 16 * stride).Clear();

        // Copy the play area to rows 16..255 of the screen buffer
        for (int y = 0; y < _playHeight; y++)
        {
            _framebuffer.AsSpan(y * _playWidth, _playWidth)
                .CopyTo(screenBuf.Slice((y + FixedPoint.SCORE_BAR_HEIGHT) * stride, _playWidth));
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

