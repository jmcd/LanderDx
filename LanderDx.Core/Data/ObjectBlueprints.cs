namespace LanderDx.Core.Data;

/// <summary>
/// All 13 object blueprints ported directly from Lander.arm:12718-13277.
/// Each model preserves the exact vertex coordinates and face definitions
/// from the original game.
/// </summary>
public static class ObjectBlueprints
{
    // ---- Rock (6 vertices, 8 faces, rotates, has shadow) ----
    public static readonly ObjectBlueprint Rock = new(
        name: "Rock",
        vertexCount: 6,
        faceCount: 8,
        flags: 0b00000011,  // rotates, has shadow
        vertices: new Vector3Int[]
        {
            new(0x00000000, 0x00000000, 0x00A00000),  // vertex 0
            new(0x00A00000, 0x00A00000, 0x00000000),  // vertex 1
            new(-10485760, 0x00A00000, 0x00000000),  // vertex 2
            new(0x00A00000, -10485760, 0x00000000),  // vertex 3
            new(-10485760, -10485760, 0x00000000),  // vertex 4
            new(0x00000000, 0x00000000, -10485760),  // vertex 5
        },
        faces: new Face[]
        {
            new(new(0x00000000, 0x54DA5200, 0x54DA5200), 0, 1, 2, 0x444),  // face 0
            new(new(0x54DA5200, 0x00000000, 0x54DA5200), 0, 3, 1, 0x444),  // face 1
            new(new(0x00000000, -1423593984, 0x54DA5200), 0, 4, 3, 0x444),  // face 2
            new(new(-1423593984, 0x00000000, 0x54DA5200), 0, 2, 4, 0x444),  // face 3
            new(new(0x00000000, 0x54DA5200, -1423593984), 5, 1, 2, 0x444),  // face 4
            new(new(0x54DA5200, 0x00000000, -1423593984), 5, 3, 1, 0x444),  // face 5
            new(new(0x00000000, -1423593984, -1423593984), 5, 4, 3, 0x444),  // face 6
            new(new(-1423593984, 0x00000000, -1423593984), 5, 2, 4, 0x444),  // face 7
        });

    // ---- Pyramid (5 vertices, 6 faces, rotates, no shadow) ----
    public static readonly ObjectBlueprint Pyramid = new(
        name: "Pyramid",
        vertexCount: 5,
        faceCount: 6,
        flags: 0b00000001,  // rotates, no shadow
        vertices: new Vector3Int[]
        {
            new(0x00000000, 0x01000000, 0x00000000),  // vertex 0
            new(0x00C00000, -8388608, 0x00C00000),  // vertex 1
            new(-12582912, -8388608, 0x00C00000),  // vertex 2
            new(0x00C00000, -8388608, -12582912),  // vertex 3
            new(-12582912, -8388608, -12582912),  // vertex 4
        },
        faces: new Face[]
        {
            new(new(0x00000000, 0x35AA66D2, 0x6B54CDA5), 0, 1, 2, 0x800),  // face 0
            new(new(0x6B54CDA5, 0x35AA66D2, 0x00000000), 0, 3, 1, 0x088),  // face 1
            new(new(0x00000000, 0x35AA66D2, -1800719781), 0, 4, 3, 0x880),  // face 2
            new(new(-1800719781, 0x35AA66D2, 0x00000000), 0, 2, 4, 0x808),  // face 3
            new(new(0x00000000, -2013265920, 0x00000000), 1, 2, 3, 0x444),  // face 4
            new(new(0x00000000, -2013265920, 0x00000000), 2, 3, 4, 0x008),  // face 5
        });

    // ---- Player Ship (9 vertices, 9 faces, rotates, has shadow) ----
    public static readonly ObjectBlueprint PlayerShip = new(
        name: "PlayerShip",
        vertexCount: 9,
        faceCount: 9,
        flags: 0b00000011,  // rotates, has shadow
        vertices: new Vector3Int[]
        {
            new(0x01000000, 0x00500000, 0x00800000),  // vertex 0
            new(0x01000000, 0x00500000, -8388608),  // vertex 1
            new(0x00000000, 0x000A0000, -20132659),  // vertex 2
            new(-15099494, 0x00500000, 0x00000000),  // vertex 3
            new(0x00000000, 0x000A0000, 0x01333333),  // vertex 4
            new(-1677721, -7864320, 0x00000000),  // vertex 5
            new(0x00555555, 0x00500000, 0x00400000),  // vertex 6
            new(0x00555555, 0x00500000, -4194304),  // vertex 7
            new(-3355443, 0x00500000, 0x00000000),  // vertex 8
        },
        faces: new Face[]
        {
            new(new(0x457C441A, -1641406644, 0x00000000), 0, 1, 5, 0x080),  // face 0
            new(new(0x35F5D83B, -1681899839, -636299491), 1, 2, 5, 0x040),  // face 1
            new(new(0x35F5D83B, -1681899839, 0x25ED28E3), 0, 5, 4, 0x040),  // face 2
            new(new(-1323051748, -1354805010, -683576712), 2, 3, 5, 0x040),  // face 3
            new(new(-1323051747, -1354805010, 0x28BE8D88), 3, 4, 5, 0x040),  // face 4
            new(new(-144320307, 0x73242236, -548417162), 1, 2, 3, 0x088),  // face 5
            new(new(-144320307, 0x73242236, 0x20B02E8A), 0, 3, 4, 0x088),  // face 6
            new(new(0x00000000, 0x78000000, 0x00000000), 0, 1, 3, 0x044),  // face 7
            new(new(0x00000000, 0x78000000, 0x00000000), 6, 7, 8, 0xC80),  // face 8
        });

    // ---- Small Leafy Tree (11 vertices, 5 faces, static, has shadow) ----
    public static readonly ObjectBlueprint SmallLeafyTree = new(
        name: "SmallLeafyTree",
        vertexCount: 11,
        faceCount: 5,
        flags: 0b00000010,  // static, has shadow
        vertices: new Vector3Int[]
        {
            new(0x00300000, -25165824, 0x00300000),  // vertex 0
            new(-2516582, 0x00000000, 0x00000000),  // vertex 1
            new(0x00266666, 0x00000000, 0x00000000),  // vertex 2
            new(0x00000000, -17616076, -12582912),  // vertex 3
            new(0x00800000, -12582912, -8388608),  // vertex 4
            new(-12582912, -20132659, -2796202),  // vertex 5
            new(-8388608, -22649241, 0x00400000),  // vertex 6
            new(0x00800000, -27682406, 0x002AAAAA),  // vertex 7
            new(0x00C00000, -22649241, -4194304),  // vertex 8
            new(-6291456, -20132659, 0x00999999),  // vertex 9
            new(0x00C00000, -12582912, 0x00C00000),  // vertex 10
        },
        faces: new Face[]
        {
            new(new(0x14A01873, -1349541997, 0x56A0681E), 0, 9, 10, 0x040),  // face 0
            new(new(0x00000000, 0x00000000, 0x00000000), 0, 1, 2, 0x400),  // face 1
            new(new(0x499A254E, -1323041748, -882027879), 0, 3, 4, 0x080),  // face 2
            new(new(-455938370, -1916262345, -416291351), 0, 5, 6, 0x080),  // face 3
            new(new(-714013307, -1298205852, -1363116365), 0, 7, 8, 0x080),  // face 4
        });

    // ---- Tall Leafy Tree (14 vertices, 6 faces, static, has shadow) ----
    public static readonly ObjectBlueprint TallLeafyTree = new(
        name: "TallLeafyTree",
        vertexCount: 14,
        faceCount: 6,
        flags: 0b00000010,  // static, has shadow
        vertices: new Vector3Int[]
        {
            new(0x0036DB6D, -42781900, 0x00300000),  // vertex 0
            new(-3145728, 0x00000000, 0x00000000),  // vertex 1
            new(0x00300000, 0x00000000, 0x00000000),  // vertex 2
            new(0x00000000, -32715571, -12582912),  // vertex 3
            new(0x00800000, -27682406, -8388608),  // vertex 4
            new(-11324620, -30198988, -3595117),  // vertex 5
            new(-12582912, -22649241, 0x00600000),  // vertex 6
            new(0x00000000, -15099494, -10066329),  // vertex 7
            new(-8388608, -12582912, -6291456),  // vertex 8
            new(-6291456, -25165824, 0x00999999),  // vertex 9
            new(0x00C00000, -20132659, 0x00C00000),  // vertex 10
            new(-5033164, -15099494, 0x00E66666),  // vertex 11
            new(0x00800000, -12582912, 0x00C00000),  // vertex 12
            new(0x00300000, -27682406, 0x00300000),  // vertex 13
        },
        faces: new Face[]
        {
            new(new(-46333475, -758434018, 0x6F20024E), 0, 9, 10, 0x040),  // face 0
            new(new(0x1E6F981A, -1156555058, 0x5D638B16), 13, 11, 12, 0x080),  // face 1
            new(new(0x00000000, 0x00000000, 0x00000000), 0, 1, 2, 0x400),  // face 2
            new(new(0x49D96509, -1192810654, -1046595047), 0, 3, 4, 0x080),  // face 3
            new(new(-1390331020, -1237202339, 0x2DC40650), 0, 5, 6, 0x040),  // face 4
            new(new(-921690031, -1400607571, -1114462015), 13, 7, 8, 0x040),  // face 5
        });

    // ---- Smoking Remains Left (5 vertices, 2 faces, static, no shadow) ----
    public static readonly ObjectBlueprint SmokingRemainsLeft = new(
        name: "SmokingRemainsLeft",
        vertexCount: 5,
        faceCount: 2,
        flags: 0b00000000,  // static, no shadow
        vertices: new Vector3Int[]
        {
            new(-2516582, 0x00000000, 0x00000000),  // vertex 0
            new(0x00266666, 0x00000000, 0x00000000),  // vertex 1
            new(0x002B3333, -4194304, 0x00000000),  // vertex 2
            new(0x00300000, -8388608, 0x00000000),  // vertex 3
            new(-2796202, -20132659, 0x00000000),  // vertex 4
        },
        faces: new Face[]
        {
            new(new(0x00000000, 0x00000000, 0x00000000), 0, 1, 3, 0x000),  // face 0
            new(new(0x00000000, 0x00000000, 0x00000000), 2, 3, 4, 0x000),  // face 1
        });

    // ---- Smoking Remains Right (5 vertices, 2 faces, static, no shadow) ----
    public static readonly ObjectBlueprint SmokingRemainsRight = new(
        name: "SmokingRemainsRight",
        vertexCount: 5,
        faceCount: 2,
        flags: 0b00000000,  // static, no shadow
        vertices: new Vector3Int[]
        {
            new(0x002AAAAA, 0x00000000, 0x00000000),  // vertex 0
            new(-2796202, 0x00000000, 0x00000000),  // vertex 1
            new(-2831155, -3145728, 0x00000000),  // vertex 2
            new(-3145728, -6291456, 0x00000000),  // vertex 3
            new(0x002AAAAA, -22649241, 0x00000000),  // vertex 4
        },
        faces: new Face[]
        {
            new(new(0x00000000, 0x00000000, 0x00000000), 0, 1, 3, 0x000),  // face 0
            new(new(0x00000000, 0x00000000, 0x00000000), 2, 3, 4, 0x000),  // face 1
        });

    // ---- Fir Tree (5 vertices, 2 faces, static, has shadow) ----
    public static readonly ObjectBlueprint FirTree = new(
        name: "FirTree",
        vertexCount: 5,
        faceCount: 2,
        flags: 0b00000010,  // static, has shadow
        vertices: new Vector3Int[]
        {
            new(-6291456, -3595117, -3595117),  // vertex 0
            new(0x00600000, -3595117, -3595117),  // vertex 1
            new(0x00000000, -30198988, 0x0036DB6D),  // vertex 2
            new(0x00266666, 0x00000000, 0x00000000),  // vertex 3
            new(-2516582, 0x00000000, 0x00000000),  // vertex 4
        },
        faces: new Face[]
        {
            new(new(0x00000000, 0x00000000, 0x00000000), 2, 3, 4, 0x400),  // face 0
            new(new(0x00000000, -525279152, -1943533245), 0, 1, 2, 0x040),  // face 1
        });

    // ---- Gazebo (13 vertices, 8 faces, static, has shadow) ----
    public static readonly ObjectBlueprint Gazebo = new(
        name: "Gazebo",
        vertexCount: 13,
        faceCount: 8,
        flags: 0b00000010,  // static, has shadow
        vertices: new Vector3Int[]
        {
            new(0x00000000, -16777216, 0x00000000),  // vertex 0
            new(-8388608, -12582912, 0x00800000),  // vertex 1
            new(-8388608, -12582912, -8388608),  // vertex 2
            new(0x00800000, -12582912, -8388608),  // vertex 3
            new(0x00800000, -12582912, 0x00800000),  // vertex 4
            new(-8388608, 0x00000000, 0x00800000),  // vertex 5
            new(-8388608, 0x00000000, -8388608),  // vertex 6
            new(0x00800000, 0x00000000, -8388608),  // vertex 7
            new(0x00800000, 0x00000000, 0x00800000),  // vertex 8
            new(-6710886, -12582912, 0x00800000),  // vertex 9
            new(-6710886, -12582912, -8388608),  // vertex 10
            new(0x00666666, -12582912, -8388608),  // vertex 11
            new(0x00666666, -12582912, 0x00800000),  // vertex 12
        },
        faces: new Face[]
        {
            new(new(0x00000000, 0x00000000, 0x78000000), 1, 5, 9, 0x444),  // face 0
            new(new(0x00000000, 0x00000000, -2013265920), 2, 6, 10, 0x444),  // face 1
            new(new(0x00000000, -1800719781, 0x35AA66D2), 0, 1, 4, 0x400),  // face 2
            new(new(0x00000000, 0x00000000, -2013265920), 3, 7, 11, 0x444),  // face 3
            new(new(0x00000000, 0x00000000, 0x78000000), 4, 8, 12, 0x444),  // face 4
            new(new(-900359890, -1800719781, 0x00000000), 0, 1, 2, 0x840),  // face 5
            new(new(0x35AA66D2, -1800719781, 0x00000000), 0, 3, 4, 0x840),  // face 6
            new(new(0x00000000, -1800719781, -900359890), 0, 2, 3, 0x400),  // face 7
        });

    // ---- Building (16 vertices, 12 faces, static, no shadow) ----
    public static readonly ObjectBlueprint Building = new(
        name: "Building",
        vertexCount: 16,
        faceCount: 12,
        flags: 0b00000000,  // static, no shadow
        vertices: new Vector3Int[]
        {
            new(-15099494, -14260633, 0x00000000),  // vertex 0
            new(-12582912, -14260633, 0x00000000),  // vertex 1
            new(0x00C00000, -14260633, 0x00000000),  // vertex 2
            new(0x00E66666, -14260633, 0x00000000),  // vertex 3
            new(-15099494, -7549747, 0x00A66666),  // vertex 4
            new(-15099494, -7549747, -10905190),  // vertex 5
            new(0x00E66666, -7549747, 0x00A66666),  // vertex 6
            new(0x00E66666, -7549747, -10905190),  // vertex 7
            new(-12582912, -10066329, 0x00800000),  // vertex 8
            new(-12582912, -10066329, -8388608),  // vertex 9
            new(0x00C00000, -10066329, 0x00800000),  // vertex 10
            new(0x00C00000, -10066329, -8388608),  // vertex 11
            new(-12582912, 0x00000000, 0x00800000),  // vertex 12
            new(-12582912, 0x00000000, -8388608),  // vertex 13
            new(0x00C00000, 0x00000000, 0x00800000),  // vertex 14
            new(0x00C00000, 0x00000000, -8388608),  // vertex 15
        },
        faces: new Face[]
        {
            new(new(0x00000000, -1714614675, 0x3EE445CC), 0, 4, 6, 0x400),  // face 0
            new(new(0x00000000, -1714614675, 0x3EE445CC), 0, 3, 6, 0x400),  // face 1
            new(new(-2013265920, 0x00000000, 0x00000000), 1, 8, 9, 0xDDD),  // face 2
            new(new(0x78000000, 0x00000000, 0x00000000), 2, 10, 11, 0x555),  // face 3
            new(new(-2013265920, 0x00000000, 0x00000000), 8, 12, 13, 0xFFF),  // face 4
            new(new(-2013265920, 0x00000000, 0x00000000), 8, 9, 13, 0xFFF),  // face 5
            new(new(0x78000000, 0x00000000, 0x00000000), 10, 14, 15, 0x777),  // face 6
            new(new(0x78000000, 0x00000000, 0x00000000), 10, 11, 15, 0x777),  // face 7
            new(new(0x00000000, 0x00000000, -2013265920), 9, 13, 15, 0xBBB),  // face 8
            new(new(0x00000000, 0x00000000, -2013265920), 9, 11, 15, 0xBBB),  // face 9
            new(new(0x00000000, -1714614675, -1055147468), 0, 5, 7, 0x800),  // face 10
            new(new(0x00000000, -1714614675, -1055147468), 0, 3, 7, 0x800),  // face 11
        });

    // ---- Smoking Building (6 vertices, 6 faces, static, no shadow) ----
    public static readonly ObjectBlueprint SmokingBuilding = new(
        name: "SmokingBuilding",
        vertexCount: 6,
        faceCount: 6,
        flags: 0b00000000,  // static, no shadow
        vertices: new Vector3Int[]
        {
            new(-12582912, 0x00000001, 0x00800000),  // vertex 0
            new(-12582912, 0x00000001, -8388608),  // vertex 1
            new(0x00C00000, 0x00000001, 0x00800000),  // vertex 2
            new(0x00C00000, 0x00000001, -8388608),  // vertex 3
            new(-12582912, -6710886, 0x00800000),  // vertex 4
            new(0x00C00000, -5033164, -8388608),  // vertex 5
        },
        faces: new Face[]
        {
            new(new(0x00000000, 0x78000000, 0x00000000), 0, 1, 2, 0x000),  // face 0
            new(new(0x00000000, 0x78000000, 0x00000000), 1, 2, 3, 0x000),  // face 1
            new(new(0x00000000, 0x00000000, 0x78000000), 0, 2, 4, 0x333),  // face 2
            new(new(-2013265920, 0x00000000, 0x00000000), 0, 1, 4, 0x666),  // face 3
            new(new(0x78000000, 0x00000000, 0x00000000), 2, 3, 5, 0x555),  // face 4
            new(new(0x00000000, 0x00000000, -2013265919), 1, 3, 5, 0x777),  // face 5
        });

    // ---- Smoking Gazebo (6 vertices, 4 faces, static, has shadow) ----
    public static readonly ObjectBlueprint SmokingGazebo = new(
        name: "SmokingGazebo",
        vertexCount: 6,
        faceCount: 4,
        flags: 0b00000010,  // static, has shadow
        vertices: new Vector3Int[]
        {
            new(0x00000000, -7549747, -1048576),  // vertex 0
            new(0x00199999, -7549747, -1048576),  // vertex 1
            new(0x00800000, 0x00000000, 0x00800000),  // vertex 2
            new(-8388608, 0x00000000, 0x00800000),  // vertex 3
            new(0x00800000, 0x00000000, -8388608),  // vertex 4
            new(-8388608, 0x00000000, -8388608),  // vertex 5
        },
        faces: new Face[]
        {
            new(new(0x00000000, -1572096578, 0x4AF6A1AD), 0, 1, 2, 0x000),  // face 0
            new(new(0x00000000, -1572096578, 0x4AF6A1AD), 0, 1, 3, 0x333),  // face 1
            new(new(0x00000000, -1403404192, -1443501416), 0, 1, 4, 0x444),  // face 2
            new(new(0x00000000, -1403404192, -1443501416), 0, 1, 5, 0x000),  // face 3
        });

    // ---- Rocket (13 vertices, 8 faces, static, has shadow) ----
    public static readonly ObjectBlueprint Rocket = new(
        name: "Rocket",
        vertexCount: 13,
        faceCount: 8,
        flags: 0b00000010,  // static, has shadow
        vertices: new Vector3Int[]
        {
            new(0x00000000, -29360128, 0x00000000),  // vertex 0
            new(-3670016, -2669102, 0x00380000),  // vertex 1
            new(-3670016, -2669102, -3670016),  // vertex 2
            new(0x00380000, -2669102, 0x00380000),  // vertex 3
            new(0x00380000, -2669102, -3670016),  // vertex 4
            new(-7340032, 0x00000000, 0x00700000),  // vertex 5
            new(-7340032, 0x00000000, -7340032),  // vertex 6
            new(0x00700000, 0x00000000, 0x00700000),  // vertex 7
            new(0x00700000, 0x00000000, -7340032),  // vertex 8
            new(-1835008, -16311182, 0x001C0000),  // vertex 9
            new(-1835008, -16311182, -1835008),  // vertex 10
            new(0x001C0000, -16311182, 0x001C0000),  // vertex 11
            new(0x001C0000, -16311182, -1835008),  // vertex 12
        },
        faces: new Face[]
        {
            new(new(0x00000000, 0x00000000, 0x00000000), 9, 1, 5, 0xCC0),  // face 0
            new(new(0x00000000, 0x00000000, 0x00000000), 11, 3, 7, 0xCC0),  // face 1
            new(new(0x00000000, -274243737, 0x76E1A76B), 0, 1, 3, 0xC00),  // face 2
            new(new(-1994499947, -274243737, 0x00000000), 0, 1, 2, 0x800),  // face 3
            new(new(0x76E1A76B, -274243737, 0x00000000), 3, 0, 4, 0x800),  // face 4
            new(new(0x00000000, -274243737, -1994499947), 0, 2, 4, 0xC00),  // face 5
            new(new(0x00000000, 0x00000000, 0x00000000), 10, 2, 6, 0xCC0),  // face 6
            new(new(0x00000000, 0x00000000, 0x00000000), 12, 4, 8, 0xCC0),  // face 7
        });

    /// <summary>All 13 object blueprints indexed by their blueprint number (0-12).</summary>
    public static readonly ObjectBlueprint[] All =
    [
        Rock,               // 0
        Pyramid,            // 1
        PlayerShip,         // 2
        SmallLeafyTree,     // 3
        TallLeafyTree,      // 4
        SmokingRemainsLeft, // 5
        SmokingRemainsRight,// 6
        FirTree,            // 7
        Gazebo,             // 8
        Building,           // 9
        SmokingBuilding,    // 10
        SmokingGazebo,      // 11
        Rocket,             // 12
    ];
}
