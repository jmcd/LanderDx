using Relander.Core.Data;
using Relander.Core.Math;

namespace Relander.Core.Engine;

/// <summary>
/// Handles 3D object blueprint rendering, back-face culling, vertex transformation,
/// perspective projection, VIDC shading, and ground shadow calculation.
/// Shared by GameEngine (landscape objects) and ParticleSystem (falling rocks).
/// </summary>
public static class ObjectRenderer
{
    public static void DrawObject(ObjectBlueprint blueprint, int objX, int objY, int objZ,
        int worldX, int worldZ, GameState state, GraphicsBuffers buffers, LandscapeGenerator landscape)
    {
        bool rotates = blueprint.Rotates;

        foreach (var face in blueprint.Faces)
        {
            int nx, ny, nz;
            if (rotates)
            {
                nx = DotMatrix(face.Normal.X, face.Normal.Y, face.Normal.Z, 0, state);
                ny = DotMatrix(face.Normal.X, face.Normal.Y, face.Normal.Z, 1, state);
                nz = DotMatrix(face.Normal.X, face.Normal.Y, face.Normal.Z, 2, state);

                // Back-face culling for rotating objects. The original scales the
                // object coordinates up and uses GetDotProduct (Lander.arm:5024-5081);
                // the quirky multiply is linear in its second operand, so the sign
                // of the exact 64-bit sum of unscaled products equals the sign of
                // the original's scaled 32-bit accumulation. The previous plain int
                // arithmetic wrapped and culled faces at random.
                long dot = (long)FixedPoint.Multiply(nx, objX)
                         + (long)FixedPoint.Multiply(ny, objY)
                         + (long)FixedPoint.Multiply(nz, objZ);
                if (dot >= 0) continue;
            }
            else
            {
                nx = face.Normal.X;
                ny = face.Normal.Y;
                nz = face.Normal.Z;
            }

            var v1 = blueprint.Vertices[face.V1];
            var v2 = blueprint.Vertices[face.V2];
            var v3 = blueprint.Vertices[face.V3];

            int rx1, ry1, rz1, rx2, ry2, rz2, rx3, ry3, rz3;
            if (rotates)
            {
                rx1 = DotMatrix(v1.X, v1.Y, v1.Z, 0, state);
                ry1 = DotMatrix(v1.X, v1.Y, v1.Z, 1, state);
                rz1 = DotMatrix(v1.X, v1.Y, v1.Z, 2, state);

                rx2 = DotMatrix(v2.X, v2.Y, v2.Z, 0, state);
                ry2 = DotMatrix(v2.X, v2.Y, v2.Z, 1, state);
                rz2 = DotMatrix(v2.X, v2.Y, v2.Z, 2, state);

                rx3 = DotMatrix(v3.X, v3.Y, v3.Z, 0, state);
                ry3 = DotMatrix(v3.X, v3.Y, v3.Z, 1, state);
                rz3 = DotMatrix(v3.X, v3.Y, v3.Z, 2, state);
            }
            else
            {
                rx1 = v1.X; ry1 = v1.Y; rz1 = v1.Z;
                rx2 = v2.X; ry2 = v2.Y; rz2 = v2.Z;
                rx3 = v3.X; ry3 = v3.Y; rz3 = v3.Z;
            }

            int wx1 = rx1 + objX, wy1 = ry1 + objY, wz1 = rz1 + objZ;
            int wx2 = rx2 + objX, wy2 = ry2 + objY, wz2 = rz2 + objZ;
            int wx3 = rx3 + objX, wy3 = ry3 + objY, wz3 = rz3 + objZ;

            if (!Projection.Project(wx1, wy1, wz1, out int sx1, out int sy1)) continue;
            if (!Projection.Project(wx2, wy2, wz2, out int sx2, out int sy2)) continue;
            if (!Projection.Project(wx3, wy3, wz3, out int sx3, out int sy3)) continue;

            int shade = (int)((0x80000000u - (uint)ny) >> 28);
            if (nx < 0) shade++;
            shade = global::System.Math.Max(0, shade - 5);
            int r = global::System.Math.Min(((face.Colour >> 8) & 0xF) + shade, 15);
            int g = global::System.Math.Min(((face.Colour >> 4) & 0xF) + shade, 15);
            int b = global::System.Math.Min((face.Colour & 0xF) + shade, 15);

            byte vidc = VidcColour.Encode(r, g, b);
            int colourWord = VidcColour.ReplicateQuad(vidc);

            int bufIdx = buffers.GetBufferIndex(objZ);
            buffers.AddTriangle(bufIdx, sx1, sy1, sx2, sy2, sx3, sy3, colourWord);

            if (blueprint.HasShadow)
            {
                int worldVX1 = worldX + rx1;
                int worldVZ1 = worldZ + rz1;
                int groundY1 = landscape.GetAltitude(worldVX1, worldVZ1);

                int worldVX2 = worldX + rx2;
                int worldVZ2 = worldZ + rz2;
                int groundY2 = landscape.GetAltitude(worldVX2, worldVZ2);

                int worldVX3 = worldX + rx3;
                int worldVZ3 = worldZ + rz3;
                int groundY3 = landscape.GetAltitude(worldVX3, worldVZ3);

                if (Projection.Project(wx1, groundY1 - state.YCamera, wz1, out int shadowX1, out int shadowY1) &&
                    Projection.Project(wx2, groundY2 - state.YCamera, wz2, out int shadowX2, out int shadowY2) &&
                    Projection.Project(wx3, groundY3 - state.YCamera, wz3, out int shadowX3, out int shadowY3))
                {
                    int shadowBufIdx = buffers.GetShadowBufferIndex(objZ);
                    buffers.AddTriangle(shadowBufIdx, shadowX1, shadowY1, shadowX2, shadowY2, shadowX3, shadowY3, 0);
                }
            }
        }
    }

    private static int DotMatrix(int x, int y, int z, int row, GameState state)
    {
        int mx, my, mz;
        switch (row)
        {
            case 0: mx = state.XNoseV; my = state.XRoofV; mz = state.XSideV; break;
            case 1: mx = state.YNoseV; my = state.YRoofV; mz = state.YSideV; break;
            default: mx = state.ZNoseV; my = state.ZRoofV; mz = state.ZSideV; break;
        }
        // The original's GetDotProduct (Lander.arm:6116-6187) uses the quirky
        // shift-and-add multiply and accumulates in a wrapping 32-bit register.
        return unchecked(FixedPoint.Multiply(x, mx) + FixedPoint.Multiply(y, my) + FixedPoint.Multiply(z, mz));
    }
}
