using Relander.Core.Math;
using Relander.Core.Data;
using Relander.Core.Interfaces;

namespace Relander.Core.Engine;

/// <summary>
/// The 256×256 byte object map that determines which objects appear on each tile.
/// Ported from Lander.arm:12276-12413 (PlaceObjectsOnMap).
/// </summary>
public class ObjectMap
{
    private readonly LandscapeGenerator _landscape;
    private readonly IRandomSource _random;

    /// <summary>256×256 byte grid. Entry &FF = empty, 1-11 = live object, 12+ = destroyed.</summary>
    public byte[] Map { get; } = new byte[256 * 256];

    /// <summary>Number of objects placed.</summary>
    public const int OBJECT_COUNT = 2048;

    public ObjectMap(LandscapeGenerator landscape, IRandomSource random)
    {
        _landscape = landscape;
        _random = random;
    }

    /// <summary>
    /// Clear the entire object map (all entries set to NO_OBJECT = &FF).
    /// </summary>
    public void Clear()
    {
        Array.Fill(Map, (byte)ObjectTypes.NO_OBJECT);
    }

    /// <summary>
    /// Randomly place 2048 objects on the map, avoiding the sea and launchpad.
    /// Places three rockets along the right edge of the launchpad.
    /// </summary>
    public void PlaceObjects()
    {
        Clear();

        // Place 2048 random objects
        for (int i = 0; i < OBJECT_COUNT; i++)
        {
            var (rand0, rand1) = _random.GetRandomNumbers();

            // Top bytes of rand0 determine x-coordinate, shifted rand0 determines z
            int x = rand0 & unchecked((int)0xFF000000);
            int z = (rand0 << 8) & unchecked((int)0xFF000000);

            // Get altitude at this position
            int altitude = _landscape.GetAltitude(x, z);

            // Don't place objects on sea or launchpad
            if (altitude == FixedPoint.SEA_LEVEL || altitude == FixedPoint.LAUNCHPAD_ALTITUDE)
                continue;

            // Determine object type (1-8, weighted towards trees)
            int type = (rand0 & 7) + 1;
            // Types: 1=small leafy, 2=tall leafy, 3=small leafy, 4=small leafy,
            //        5=gazebo, 6=tall leafy, 7=fir tree, 8=building

            // Calculate map index: (z_byte << 8) | x_byte
            int mapIndex = ((z >> 16) & 0xFF00) | ((x >> 24) & 0xFF);
            Map[mapIndex] = (byte)type;
        }

        // Place three rockets along the right edge of the launchpad
        // Rockets at (x=7, z=1), (x=7, z=3), (x=7, z=5)
        Map[0x0107] = (byte)LAUNCHPAD_OBJECT;  // (7, 1)
        Map[0x0307] = (byte)LAUNCHPAD_OBJECT;  // (7, 3)
        Map[0x0507] = (byte)LAUNCHPAD_OBJECT;  // (7, 5)
    }

    /// <summary>
    /// Get the object type at a specific world coordinate.
    /// </summary>
    public int GetObjectAt(int worldX, int worldZ)
    {
        int mapIndex = ((worldZ >> 16) & 0xFF00) | ((worldX >> 24) & 0xFF);
        return Map[mapIndex];
    }

    /// <summary>
    /// Set the object type at a specific world coordinate.
    /// Used for object destruction (replacing live objects with smoking remains).
    /// </summary>
    public void SetObjectAt(int worldX, int worldZ, byte type)
    {
        int mapIndex = ((worldZ >> 16) & 0xFF00) | ((worldX >> 24) & 0xFF);
        Map[mapIndex] = type;
    }

    /// <summary>
    /// Get the object type at a specific map index (tile coordinates).
    /// </summary>
    public int GetObjectAtTile(int tileX, int tileZ)
    {
        return Map[(tileZ << 8) | tileX];
    }

    /// <summary>
    /// The launchpad object type constant.
    /// </summary>
    public const int LAUNCHPAD_OBJECT = ObjectTypes.FIRST_LIVE_TYPE + 8; // Rocket = type 9
}
