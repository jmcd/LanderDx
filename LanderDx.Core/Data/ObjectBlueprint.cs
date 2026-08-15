namespace LanderDx.Core.Data;

/// <summary>
/// A face from an object blueprint, containing a normal vector,
/// three vertex indices, and a 12-bit RGB colour.
/// </summary>
public readonly record struct Face(
    Vector3Int Normal,
    int V1,
    int V2,
    int V3,
    int Colour  // 12-bit RGB (4 bits per channel: &rgb)
);

/// <summary>
/// Object blueprint defining vertices and faces for a 3D model.
/// Mirrors the format at Lander.arm:12718-13277.
/// </summary>
public class ObjectBlueprint(
    string name,
    int vertexCount,
    int faceCount,
    int flags,
    Vector3Int[] vertices,
    Face[] faces)
{
    /// <summary>Descriptive name for debugging.</summary>
    public string Name { get; } = name;

    /// <summary>Number of vertices in the model.</summary>
    public int VertexCount { get; } = vertexCount;

    /// <summary>Number of faces in the model.</summary>
    public int FaceCount { get; } = faceCount;

    /// <summary>
    /// Flags: bit 0 = object rotates, bit 1 = object has a shadow.
    /// </summary>
    public int Flags { get; } = flags;

    /// <summary>True if this object rotates (uses rotation matrix).</summary>
    public bool Rotates => (Flags & 1) != 0;

    /// <summary>True if this object casts a shadow.</summary>
    public bool HasShadow => (Flags & 2) != 0;

    /// <summary>Vertex coordinates in object-local space (fixed-point).</summary>
    public Vector3Int[] Vertices { get; } = vertices;

    /// <summary>Face definitions with normals, vertex indices, and colours.</summary>
    public Face[] Faces { get; } = faces;
}
