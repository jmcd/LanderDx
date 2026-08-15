namespace LanderDx.Core.Data;

/// <summary>
/// Maps object type IDs (0-24) to their blueprints, ported from Lander.arm:4638-4666.
/// Types 1-11 are live objects; types 12-24 are smoking/destroyed variants.
/// Type &FF (255) means no object at that map position.
/// </summary>
public static class ObjectTypes
{
    public const int NO_OBJECT = 0xFF;
    public const int FIRST_LIVE_TYPE = 1;
    public const int LAST_LIVE_TYPE = 11;
    public const int FIRST_SMOKING_TYPE = 12;
    public const int DESTROY_OFFSET = 12;  // Add to live type to get smoking variant

    /// <summary>Maps object type ID (0-24) to the corresponding blueprint.</summary>
    /// <remarks>
    /// Type 0: pyramid (unused in normal gameplay)
    /// Types 1,3,4: small leafy trees (most common)
    /// Types 2,6: tall leafy trees
    /// Type 5: gazebo
    /// Type 7: fir tree
    /// Type 8: building
    /// Types 9-11: rockets (launchpad only)
    /// Types 12-24: smoking/destroyed remains
    /// </remarks>
    public static readonly ObjectBlueprint?[] ByType =
    [
        // ---- Live objects ----
        ObjectBlueprints.Pyramid,               // 0  = pyramid (unused)
        ObjectBlueprints.SmallLeafyTree,        // 1  = small leafy tree
        ObjectBlueprints.TallLeafyTree,         // 2  = tall leafy tree
        ObjectBlueprints.SmallLeafyTree,        // 3  = small leafy tree
        ObjectBlueprints.SmallLeafyTree,        // 4  = small leafy tree
        ObjectBlueprints.Gazebo,                // 5  = gazebo
        ObjectBlueprints.TallLeafyTree,         // 6  = tall leafy tree
        ObjectBlueprints.FirTree,               // 7  = fir tree
        ObjectBlueprints.Building,              // 8  = building
        ObjectBlueprints.Rocket,                // 9  = rocket
        ObjectBlueprints.Rocket,                // 10 = rocket
        ObjectBlueprints.Rocket,                // 11 = rocket

        // ---- Smoking/destroyed objects ----
        ObjectBlueprints.Rocket,                // 12 = smoking but intact rocket (unused)
        ObjectBlueprints.SmokingRemainsRight,   // 13 = smoking remains (bends right)
        ObjectBlueprints.SmokingRemainsLeft,    // 14 = smoking remains (bends left)
        ObjectBlueprints.SmokingRemainsLeft,    // 15 = smoking remains (bends left)
        ObjectBlueprints.SmokingRemainsLeft,    // 16 = smoking remains (bends left)
        ObjectBlueprints.SmokingGazebo,         // 17 = smoking remains of a gazebo
        ObjectBlueprints.SmokingRemainsRight,   // 18 = smoking remains (bends right)
        ObjectBlueprints.SmokingRemainsRight,   // 19 = smoking remains (bends right)
        ObjectBlueprints.SmokingBuilding,       // 20 = smoking remains of a building
        ObjectBlueprints.SmokingRemainsRight,   // 21 = smoking remains (bends right)
        ObjectBlueprints.SmokingRemainsLeft,    // 22 = smoking remains (bends left)
        ObjectBlueprints.SmokingRemainsLeft,    // 23 = smoking remains (bends left)
        ObjectBlueprints.SmokingRemainsLeft,    // 24 = smoking remains (unused)
    ];

    /// <summary>Get the blueprint for a given object type, or null if invalid.</summary>
    public static ObjectBlueprint? GetBlueprint(int type)
    {
        if (type < 0 || type >= ByType.Length)
            return null;
        return ByType[type];
    }

    /// <summary>Check if an object type represents a live (destructible) object.</summary>
    public static bool IsLiveObject(int type) =>
        type >= FIRST_LIVE_TYPE && type <= LAST_LIVE_TYPE;

    /// <summary>Check if an object type represents a smoking/destroyed object.</summary>
    public static bool IsSmokingObject(int type) =>
        type >= FIRST_SMOKING_TYPE && type < ByType.Length;

    /// <summary>Get the smoking remains type for a given live object type.</summary>
    public static int GetSmokingType(int liveType) =>
        IsLiveObject(liveType) ? liveType + DESTROY_OFFSET : liveType;
}
