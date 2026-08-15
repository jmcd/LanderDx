using LanderDx.Core.Math;
using LanderDx.Core.Data;

namespace LanderDx.Core.Engine;

/// <summary>
/// Holds all workspace variables from Lander.arm:228-498.
/// The original uses R11 as a base pointer with offsets; here we use named fields.
/// </summary>
public class GameState
{
    // ---- Object currently being drawn ----
    public int XObject, YObject, ZObject;

    // ---- Rotation matrix (3x3, stored as row vectors) ----
    public int XNoseV, XRoofV, XSideV;
    public int YNoseV, YRoofV, YSideV;
    public int ZNoseV, ZRoofV, ZSideV;

    // ---- Vertex processing ----
    public int XVertex, YVertex, ZVertex;
    public int XVertexRotated, YVertexRotated, ZVertexRotated;
    public int XCoord, YCoord, ZCoord;
    public int XObjectScaled, YObjectScaled, ZObjectScaled;

    // ---- Player state ----
    public int XPlayer, YPlayer, ZPlayer;
    public int XVelocity, YVelocity, ZVelocity;
    public int XExhaust, YExhaust, ZExhaust;

    // ---- Landscape drawing state ----
    public int XLandscapeRow, YLandscapeRow, ZLandscapeRow;
    public int XLandscapeCol, YLandscapeCol, ZLandscapeCol;
    public int XPrevA, YPrevA, ZPrevA;
    public int XPrevB, YPrevB, ZPrevB;
    public int TileCornerRow;
    public int TileRowOddEven;
    public int UnusedConfig;
    public int ObjectType;

    // ---- Altitude cache ----
    public int Altitude;
    public int PrevAltitude;

    // ---- Particle state ----
    public int ParticleEnd;
    public int ParticleCount;

    // ---- Object drawing state ----
    public int ObjectData;
    public int ObjectFlags;

    // ---- Game flow ----
    public int MainLoopCount;
    public int CrashLoopCount;
    public int CrashedFlag;
    public int PlayingGame; // 0 = crash animation, -1 = playing, -2 = game over (waiting for a key)

    // ---- Scoring ----
    public int CurrentScore;
    public int FuelLevel;
    public int Gravity;
    public int RemainingLives;
    // Set once at entry (Lander.arm:11977-11979, 12201-12203); StartNewGame
    // latches max(highScore, currentScore) — Initialize must not reset it.
    public int HighScore = FixedPoint.INITIAL_HIGH_SCORE;
    public int MapMode; // 0 = Inset Mini-Map, 1 = Full 256x256 Overlay, 2 = Hidden
    public bool ShowCoords; // HUD coordinate display (P key) — opt-in, no original counterpart

    // ---- Camera ----
    public int XCamera, YCamera, ZCamera;
    public int XCameraTile, YCameraTile, ZCameraTile;

    // ---- Ship orientation ----
    public int ShipDirection;
    public int ShipPitch;
    public int FuelBurnRate;

    /// <summary>Initialize for a new game.</summary>
    public void Initialize()
    {
        CurrentScore = FixedPoint.INITIAL_SCORE;
        FuelLevel = FixedPoint.INITIAL_FUEL_LEVEL;
        Gravity = FixedPoint.BASE_GRAVITY;
        RemainingLives = FixedPoint.INITIAL_LIVES;
        PlayingGame = -1; // playing
        MainLoopCount = 0;
    }

    /// <summary>Place player on launchpad for a new life.</summary>
    public void PlaceOnLaunchpad()
    {
        PlayingGame = -1;
        CrashedFlag = 0;   // Clear any stale crash from the previous life's DrawShip
        XCamera = 0;
        ZCamera = 0;
        ShipDirection = 0;
        ShipPitch = 1;  // Lander.arm:12453-12454: MOV R0, #1. Level: yRoofV=+1 (thrust up). Model Y-flipped so canopy (-Y) maps to world UP. (Index-identical to 0 in the sine lookup, but kept verbatim.)
        int padHalf = FixedPoint.LAUNCHPAD_SIZE / 2;
        XPlayer = padHalf;
        YPlayer = FixedPoint.LAUNCHPAD_Y;
        ZPlayer = padHalf;
        XVelocity = 0;
        YVelocity = 0;
        ZVelocity = 0;
        FuelBurnRate = 0;
    }

    /// <summary>Update gravity based on score (difficulty scaling).</summary>
    public void UpdateGravity()
    {
        if (CurrentScore >= FixedPoint.GRAVITY_SCORE_THRESHOLD_3)
            Gravity = FixedPoint.GRAVITY_LEVEL_3;
        else if (CurrentScore >= FixedPoint.GRAVITY_SCORE_THRESHOLD_2)
            Gravity = FixedPoint.GRAVITY_LEVEL_2;
        else
            Gravity = FixedPoint.BASE_GRAVITY;
    }
}
