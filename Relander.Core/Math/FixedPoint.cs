namespace Relander.Core.Math;

/// <summary>
/// Configuration constants and fixed-point math values ported from Lander.arm lines 42-178.
/// The original game uses 32-bit fixed-point where the top byte is the integer part
/// and the bottom 3 bytes are fractional. TILE_SIZE = 0x01000000 = 1.0 in tile units.
/// </summary>
public static class FixedPoint
{
    // ---- Configuration variables (Lander.arm:42-72) ----

    /// <summary>The length of one side of a square landscape tile in 3D coordinates.</summary>
    public const int TILE_SIZE = 0x01000000;

    /// <summary>Number of tile corners in a landscape row from left to right (i.e. 12 tiles).</summary>
    public const int TILES_X = 13;

    /// <summary>Number of tile corners in a landscape row from front to back (i.e. 10 tiles).</summary>
    public const int TILES_Z = 11;

    /// <summary>Maximum number of particles at any one time.</summary>
    public const int MAX_PARTICLES = 484;

    /// <summary>The type of object along the right edge of the launchpad (rocket = 9).</summary>
    public const int LAUNCHPAD_OBJECT = 9;

    /// <summary>The altitude of the launchpad.</summary>
    public const int LAUNCHPAD_ALTITUDE = 0x03500000;

    /// <summary>The altitude of sea level.</summary>
    public const int SEA_LEVEL = 0x05500000;

    /// <summary>The maximum safe landing speed.</summary>
    public const int LANDING_SPEED = 0x00200000;

    /// <summary>The speed at which smoke particles rise up from a destroyed object.</summary>
    public const int SMOKE_RISING_SPEED = 0x00080000;

    /// <summary>Height of the ship's undercarriage (0.390625 tiles).</summary>
    public const int UNDERCARRIAGE_Y = 0x00640000;

    /// <summary>Size of each graphics buffer in bytes.</summary>
    public const int BUFFER_SIZE = 4308;

    // ---- Calculated constants (Lander.arm:80-178) ----

    /// <summary>Extra bytes of cornerStore for every 32 tiles (0 for default landscape).</summary>
    public const int STORE = 256 * (TILES_X / 32);

    /// <summary>The y-coordinate of the ship sitting on the launchpad on its undercarriage.</summary>
    public const int LAUNCHPAD_Y = LAUNCHPAD_ALTITUDE - UNDERCARRIAGE_Y;

    /// <summary>Size of the launchpad (8 tile sizes).</summary>
    public const int LAUNCHPAD_SIZE = TILE_SIZE * 8;

    /// <summary>Highest altitude for the engines to work (52 tile sizes).</summary>
    public const int HIGHEST_ALTITUDE = TILE_SIZE * 52;

    /// <summary>Height above sea at which splash particles are added (1/16 tile).</summary>
    public const int SPLASH_HEIGHT = TILE_SIZE / 16;

    /// <summary>Vertical distance above ship for explosion cloud on crash (5/16 tile).</summary>
    public const int CRASH_CLOUD_Y = TILE_SIZE * 5 / 16;

    /// <summary>Height for smoke particles above destroyed object (3/4 tile).</summary>
    public const int SMOKE_HEIGHT = TILE_SIZE * 3 / 4;

    /// <summary>Minimum safe height for avoiding objects on ground (1.5 tiles).</summary>
    public const int SAFE_HEIGHT = TILE_SIZE * 3 / 2;

    /// <summary>Distance along z-axis between player and camera position.</summary>
    public const int CAMERA_PLAYER_Z = (TILES_Z - 6) * TILE_SIZE;

    /// <summary>Altitude of the mid-point of the generated landscape.</summary>
    public const int LAND_MID_HEIGHT = TILE_SIZE * 5;

    /// <summary>z-distance between tile in front of player and camera (6 tiles forward).</summary>
    public const int PLAYER_FRONT_Z = (TILES_Z - 5) * TILE_SIZE;

    /// <summary>Height from which rocks drop (32 tile sizes).</summary>
    public const int ROCK_HEIGHT = TILE_SIZE * 32;

    /// <summary>Width of visible landscape in x-coordinates (whole tiles, ignoring centre tile).</summary>
    public const int LANDSCAPE_X_WIDTH = TILE_SIZE * (TILES_X - 2);

    /// <summary>Depth of visible landscape in z-coordinates (whole tiles).</summary>
    public const int LANDSCAPE_Z_DEPTH = TILE_SIZE * (TILES_Z - 1);

    /// <summary>x-coordinate of the landscape offset (half width, centring the view).</summary>
    public const int LANDSCAPE_X = LANDSCAPE_X_WIDTH / 2;

    /// <summary>y-coordinate of the landscape offset (zero).</summary>
    public const int LANDSCAPE_Y = 0;

    /// <summary>z-coordinate of landscape offset (depth + 10 tiles).</summary>
    public const int LANDSCAPE_Z = LANDSCAPE_Z_DEPTH + (10 * TILE_SIZE);

    /// <summary>Half the number of tiles from left to right.</summary>
    public const int HALF_TILES_X = TILES_X / 2;

    /// <summary>Width of half the landscape in x-coordinates.</summary>
    public const int LANDSCAPE_X_HALF = TILE_SIZE * HALF_TILES_X;

    /// <summary>Depth of visible landscape plus one more tile.</summary>
    public const int LANDSCAPE_Z_BEYOND = LANDSCAPE_Z_DEPTH + TILE_SIZE;

    /// <summary>z-coordinate of the front of the visible landscape.</summary>
    public const int LANDSCAPE_Z_FRONT = LANDSCAPE_Z - LANDSCAPE_Z_DEPTH;

    /// <summary>z-coordinate of the mid-point of the landscape depth (the player).</summary>
    public const int LANDSCAPE_Z_MID = LANDSCAPE_Z - CAMERA_PLAYER_Z;

    // ---- Gameplay constants ----

    /// <summary>Initial fuel level.</summary>
    public const int INITIAL_FUEL_LEVEL = 3413;  // 0x0D55

    /// <summary>Maximum fuel level (refuel cap).</summary>
    public const int MAX_FUEL_LEVEL = 0x1400;    // 5120

    /// <summary>Fuel refuel rate per frame on launchpad.</summary>
    public const int FUEL_REFUEL_RATE = 0x20;

    /// <summary>Initial score (also the starting bullet count).</summary>
    public const int INITIAL_SCORE = 500;

    /// <summary>Initial number of lives.</summary>
    public const int INITIAL_LIVES = 3;

    /// <summary>Base gravity value.</summary>
    public const int BASE_GRAVITY = 0x30000;

    /// <summary>Increased gravity at score >= 1024.</summary>
    public const int GRAVITY_LEVEL_2 = 0x50000;

    /// <summary>Increased gravity at score >= 1488.</summary>
    public const int GRAVITY_LEVEL_3 = 0x70000;

    /// <summary>Score threshold for rocks to start falling.</summary>
    public const int ROCK_SCORE_THRESHOLD = 800;

    /// <summary>Score threshold for gravity increase level 2.</summary>
    public const int GRAVITY_SCORE_THRESHOLD_2 = 1024;

    /// <summary>Score threshold for gravity increase level 3.</summary>
    public const int GRAVITY_SCORE_THRESHOLD_3 = 1488;

    /// <summary>Maximum rock drop probability divisor.</summary>
    public const int ROCK_PROBABILITY_DIVISOR = 16384;

    /// <summary>Score added per object destroyed.</summary>
    public const int SCORE_PER_DESTROY = 20;

    /// <summary>Friction divider (velocity -= velocity / 64 each frame).</summary>
    public const int FRICTION_SHIFT = 6;

    /// <summary>Thrust divider (velocity -= exhaust / 2048 for full thrust).</summary>
    public const int THRUST_SHIFT = 11;

    /// <summary>Hover thrust divider (velocity -= exhaust / 8192, quarter thrust).</summary>
    public const int HOVER_THRUST_SHIFT = 13;

    /// <summary>Screen dimensions in pixels.</summary>
    public const int SCREEN_WIDTH = 320;
    public const int SCREEN_HEIGHT = 256;
    public const int SCORE_BAR_HEIGHT = 16;  // 2 text rows × 8 pixels
    public const int PLAY_AREA_HEIGHT = 240; // SCREEN_HEIGHT - SCORE_BAR_HEIGHT

    /// <summary>Number of graphics buffers (one per tile corner row + 1).</summary>
    public const int GRAPHICS_BUFFER_COUNT = TILES_Z + 1;  // 12

    /// <summary>Crash animation loop count.</summary>
    public const int CRASH_LOOP_COUNT = 30;

    /// <summary>Crash explosion cluster count.</summary>
    public const int CRASH_EXPLOSION_CLUSTERS = 81;

    /// <summary>Object destruction explosion cluster count.</summary>
    public const int DESTROY_EXPLOSION_CLUSTERS = 20;
}
