using Relander.Core.Math;
using Relander.Core.Data;
using Relander.Core.Interfaces;

namespace Relander.Core.Engine;

/// <summary>
/// Player ship control: mouse input, physics, collision detection, and ship drawing.
/// Based on MoveAndDrawPlayer from Lander.arm:1734-2600.
/// </summary>
public class PlayerController
{
    private readonly GameState _state;
    private readonly GraphicsBuffers _buffers;
    private readonly LandscapeGenerator _landscape;

    public PlayerController(GameState state, GraphicsBuffers buffers, LandscapeGenerator landscape)
    {
        _state = state;
        _buffers = buffers;
        _landscape = landscape;
    }

    /// <summary>
    /// Process one frame of player movement: read input, update physics,
    /// detect collisions, and draw the ship into graphics buffers.
    /// Returns true if the player is still alive after this frame.
    /// </summary>
    public bool Update(IGameInput input)
    {
        if (_state.PlayingGame == 0)
            return true; // Crash animation — skip player update

        // --- Read mouse and compute rotation ---
        ReadMouse(input);
        UpdateAnglesFromMouse();
        ComputeRotationMatrix();
        UpdatePhysics();

        // --- Check for landing / collision ---
        bool alive = CheckCollisionAndLanding();

        // --- Draw the ship ---
        DrawShip();

        return alive;
    }

    private void ReadMouse(IGameInput input)
    {
        // Set fuel burn rate from buttons
        int burnRate = 0;
        if (input.RightButton) burnRate |= 1;   // Fire
        if (input.MiddleButton) burnRate |= 2;  // Hover
        if (input.LeftButton) burnRate |= 4;    // Full thrust

        _state.FuelBurnRate = burnRate;

        // Zero burn rate if out of fuel
        if (_state.FuelLevel <= 0)
            _state.FuelBurnRate = 0;

        // Store mouse position for angle computation
        int mx = global::System.Math.Clamp(input.MouseX, 0, 1023);
        int my = global::System.Math.Clamp(input.MouseY, 0, 1023);

        // Convert to -512..+511 range and scale up
        int scaledX = (mx - 512) << 22;
        int scaledY = (512 - my) << 22;

        // Convert to polar coordinates using arctan and sqrt tables
        _mouseAngle = GetAngle(scaledX, scaledY);
        _mouseDistance = GetDistance(scaledX, scaledY);

        // Cap distance
        if ((uint)_mouseDistance > 0x40000000)
            _mouseDistance = 0x3FFFFFFF;
        _mouseDistance <<= 1;  // Scale to 0..&7FFFFFFE
    }

    private int _mouseAngle;
    private int _mouseDistance;

    // ---- Angle/polar coordinate computation ----

    private static int GetAngle(int x, int y)
    {
        // Determine quadrant and compute ratio
        bool xNeg = x < 0, yNeg = y < 0;
        uint ax = (uint)(xNeg ? -x : x);
        uint ay = (uint)(yNeg ? -y : y);
        bool flipped = ax < ay;

        uint numerator = flipped ? ax : ay;
        uint denominator = flipped ? ay : ax;

        // Compute ratio * 128 for arctan lookup
        int ratio = denominator != 0 ? (int)(numerator * 128 / denominator) : 0;
        if (ratio >= 128) ratio = 127;
        int angle = ArctanTable.Data[ratio];

        // Adjust quadrant
        if (flipped)
            angle = 0x40000000 - angle;  // 90 degrees - angle
        if (xNeg)
            angle = unchecked((int)(0x80000000 - (uint)angle));  // 180 degrees - angle
        if (!yNeg && angle < 0)
            angle += unchecked((int)0x80000000);
        // Full circle is 0x80000000 (needs wrapping)

        return angle;
    }

    private static int GetDistance(int x, int y)
    {
        // Compute x^2 + y^2 then sqrt via lookup
        long xl = x, yl = y;
        long sum = (xl * xl + yl * yl) >> 24;  // Scale down
        if (sum < 0) sum = long.MaxValue >> 24;
        int index = (int)((ulong)sum >> 20);
        if (index >= SquareRootTable.Length) index = SquareRootTable.Length - 1;
        if (index < 0) index = 0;
        return SquareRootTable.Data[index];
    }

    // ---- Angle damping ----

    private void UpdateAnglesFromMouse()
    {
        int shipDir = _state.ShipDirection;
        int shipPitch = _state.ShipPitch;

        // Difference between current direction and mouse angle
        int diffDir = shipDir - _mouseAngle;
        if (diffDir < -0x30000000) diffDir = -0x30000001;
        if (diffDir > 0x30000000) diffDir = 0x30000001;

        // Difference between current pitch and mouse distance
        int diffPitch = shipPitch - _mouseDistance;
        if (diffPitch > 0x30000000) diffPitch = 0x30000001;
        if (diffPitch < -0x30000000) diffPitch = -0x30000001;

        // Apply damping: new = old - diff / 2
        _state.ShipDirection = shipDir - (diffDir >> 1);
        _state.ShipPitch = shipPitch - (diffPitch >> 1);
    }

    // ---- Rotation matrix (Lander.arm:6311-6562) ----

    public void ComputeRotationMatrix()
    {
        int a = _state.ShipPitch;  // Pitch angle
        int b = _state.ShipDirection;  // Heading angle

        // sin/cos via sine table (cos = sin at index + 256 = +90 degrees)
        int sinA = SinLookup(a);
        int cosA = SinLookup(a + 0x40000000);  // +90 degrees
        int sinB = SinLookup(b);
        int cosB = SinLookup(b + 0x40000000);  // +90 degrees

        // Rotation matrix (row vectors):
        // [ xNoseV xRoofV xSideV ]   [  cosA*cosB  -sinA*cosB   sinB ]
        // [ yNoseV yRoofV ySideV ] = [     sinA        cosA        0  ]
        // [ zNoseV zRoofV zSideV ]   [ -cosA*sinB   sinA*sinB   cosB ]

        _state.XNoseV = MulFixed(cosA, cosB);
        _state.XRoofV = -MulFixed(sinA, cosB);
        _state.XSideV = sinB;
        _state.YNoseV = sinA;
        _state.YRoofV = cosA;
        _state.YSideV = 0;
        _state.ZNoseV = -MulFixed(cosA, sinB);
        _state.ZRoofV = MulFixed(sinA, sinB);
        _state.ZSideV = cosB;
    }

    private static int SinLookup(int angle)
    {
        int index = (int)((uint)(angle + 0x40000000) >> 22) & 0x3FF;
        return SineTable.Data[index];
    }

    // Fixed-point multiply: (a * b) >> 31
    private static int MulFixed(int a, int b)
    {
        return (int)((long)a * b >> 31);
    }

    // ---- Physics update (Lander.arm:1910-2050) ----

    private void UpdatePhysics()
    {
        int x = _state.XPlayer, y = _state.YPlayer, z = _state.ZPlayer;
        int vx = _state.XVelocity, vy = _state.YVelocity, vz = _state.ZVelocity;

        // Get thrust direction (roof vector)
        int tx = _state.XRoofV;
        int ty = _state.YRoofV;
        int tz = _state.ZRoofV;
        _state.XExhaust = tx;
        _state.YExhaust = ty;
        _state.ZExhaust = tz;

        int burnRate = _state.FuelBurnRate;

        // Cut engines above highest altitude
        if (-y > FixedPoint.HIGHEST_ALTITUDE)
            burnRate &= ~6;  // Clear hover and thrust bits

        // Friction: velocity -= velocity / 64
        vx -= vx >> FixedPoint.FRICTION_SHIFT;
        vy -= vy >> FixedPoint.FRICTION_SHIFT;
        vz -= vz >> FixedPoint.FRICTION_SHIFT;

        // Full thrust (left button, bit 2)
        if ((burnRate & 4) != 0)
        {
            vx -= tx >> FixedPoint.THRUST_SHIFT;
            vy -= ty >> FixedPoint.THRUST_SHIFT;
            vz -= tz >> FixedPoint.THRUST_SHIFT;
        }

        // Apply velocity to position
        x += vx;
        y += vy;
        z += vz;

        // Hover thrust (middle button, bit 1) — applied after position update (inertia)
        if ((burnRate & 2) != 0)
        {
            vx -= tx >> FixedPoint.HOVER_THRUST_SHIFT;
            vy -= ty >> FixedPoint.HOVER_THRUST_SHIFT;
            vz -= tz >> FixedPoint.HOVER_THRUST_SHIFT;
        }

        // Gravity
        vy += _state.Gravity;

        // Store updated values
        _state.XPlayer = x;
        _state.YPlayer = y;
        _state.ZPlayer = z;
        _state.XVelocity = vx;
        _state.YVelocity = vy;
        _state.ZVelocity = vz;

        // Fuel consumption (bit 0 of burn rate ignored for fuel)
        _state.FuelLevel = global::System.Math.Max(0, _state.FuelLevel - burnRate);
    }

    // ---- Collision detection and landing (Lander.arm:2064-2600) ----

    private bool CheckCollisionAndLanding()
    {
        int x = _state.XPlayer;
        int y = _state.YPlayer;
        int z = _state.ZPlayer;
        int vx = _state.XVelocity;
        int vy = _state.YVelocity;
        int vz = _state.ZVelocity;

        // Update camera position to follow ship
        int camY = y;
        if (camY > 0) camY = 0;
        _state.XCamera = x;
        _state.YCamera = camY;
        _state.ZCamera = z + FixedPoint.CAMERA_PLAYER_Z;

        // Check altitude below ship
        int shipX = x;
        int shipZ = z;
        int terrainAlt = _landscape.GetAltitude(shipX, shipZ);
        int safeAlt = terrainAlt - FixedPoint.UNDERCARRIAGE_Y;

        // If safely above objects, no further checks needed
        if (safeAlt - y < FixedPoint.SAFE_HEIGHT)
        {
            // Check for landing on launchpad
            if ((uint)x < FixedPoint.LAUNCHPAD_SIZE &&
                (uint)z < FixedPoint.LAUNCHPAD_SIZE)
            {
                // Over the launchpad — check landing speed
                int totalSpeed = global::System.Math.Abs(vx) + global::System.Math.Abs(vy) + global::System.Math.Abs(vz);
                if ((uint)totalSpeed < FixedPoint.LANDING_SPEED && y >= FixedPoint.LAUNCHPAD_Y)
                {
                    // Safe landing!
                    _state.YPlayer = FixedPoint.LAUNCHPAD_Y;
                    _state.XVelocity = 0;
                    _state.YVelocity = 0;
                    _state.ZVelocity = 0;

                    // Refuel
                    _state.FuelLevel = global::System.Math.Min(FixedPoint.MAX_FUEL_LEVEL,
                        _state.FuelLevel + FixedPoint.FUEL_REFUEL_RATE);
                    return true;
                }
            }
            else if (y >= safeAlt)
            {
                // Collision with ground! Lose a life
                return false;
            }
        }

        return true;
    }

    // ---- Ship drawing ----

    private void DrawShip()
    {
        // The ship is always drawn at screen center:
        // x = 0 (camera follows ship, ship centered horizontally)
        // y = yPlayer - yCamera (height above ground, 0 when high up)
        // z = LANDSCAPE_Z_MID (fixed depth at middle of landscape)
        //   LANDSCAPE_Z_MID = LANDSCAPE_Z - CAMERA_PLAYER_Z = 15 * TILE_SIZE
        int objX = 0;
        int objY = _state.YPlayer - _state.YCamera;
        int objZ = FixedPoint.LANDSCAPE_Z_MID;

        // Draw the player ship as a rotating 3D object
        DrawObject(ObjectBlueprints.PlayerShip, objX, objY, objZ, isRotating: true);
    }

    // ---- 3D Object Drawing ----

    private void DrawObject(ObjectBlueprint blueprint, int objX, int objY, int objZ, bool isRotating)
    {
        // The original uses UNSCALED object position + rotated vertex for world coords,
        // then ProjectVertexOntoScreen handles precision scaling internally.
        // Project each vertex
        var projectedVertices = new (int x, int y, int shadowX, int shadowY)[blueprint.VertexCount];

        for (int v = 0; v < blueprint.VertexCount; v++)
        {
            var vert = blueprint.Vertices[v];

            // Rotate vertex if object rotates
            int rvx, rvy, rvz;
            if (isRotating)
            {
                rvx = DotProduct(vert.X, vert.Y, vert.Z, _state.XNoseV, _state.YNoseV, _state.ZNoseV);
                rvy = DotProduct(vert.X, vert.Y, vert.Z, _state.XRoofV, _state.YRoofV, _state.ZRoofV);
                rvz = DotProduct(vert.X, vert.Y, vert.Z, _state.XSideV, _state.YSideV, _state.ZSideV);
            }
            else
            {
                rvx = vert.X;
                rvy = vert.Y;
                rvz = vert.Z;
            }

            // World-space vertex (unscaled — Projection handles scaling)
            int wx = rvx + objX;
            int wy = rvy + objY;
            int wz = rvz + objZ;

            // Project vertex to screen
            if (Projection.Project(wx, wy, wz, out int screenX, out int screenY))
            {
                projectedVertices[v].x = screenX;
                projectedVertices[v].y = screenY;
            }

            // Project shadow (point on ground below vertex)
            // Reconstruct world position for altitude lookup
            int worldVX = objX + _state.XCamera + rvx;
            int worldVZ = objZ - FixedPoint.LANDSCAPE_Z + _state.ZCamera + rvz;
            int groundY = _landscape.GetAltitude(worldVX, worldVZ);

            if (Projection.Project(wx, groundY - _state.YCamera, wz, out int shadowX, out int shadowY))
            {
                projectedVertices[v].shadowX = shadowX;
                projectedVertices[v].shadowY = shadowY;
            }

            // Crash test: object vertex below its shadow
            if (projectedVertices[v].y >= projectedVertices[v].shadowY)
                _state.CrashedFlag = -1;
        }

        // Draw faces
        foreach (var face in blueprint.Faces)
        {
            // Get projected vertices
            var pv1 = projectedVertices[face.V1];
            var pv2 = projectedVertices[face.V2];
            var pv3 = projectedVertices[face.V3];

            // Compute shading from face normal (always computed, light above-left)
            int brightness = (int)((0x80000000u - (uint)face.Normal.Y) >> 28);
            if (face.Normal.X < 0) brightness++;
            brightness = global::System.Math.Max(0, brightness - 5);
            if (brightness > 3) brightness = 3;

            // Apply brightness to colour channels
            int r = ((face.Colour >> 8) & 0xF) + brightness;
            int g = ((face.Colour >> 4) & 0xF) + brightness;
            int b = (face.Colour & 0xF) + brightness;
            r = global::System.Math.Min(r, 15);
            g = global::System.Math.Min(g, 15);
            b = global::System.Math.Min(b, 15);

            byte vidc = VidcColour.Encode(r, g, b);
            int colourWord = VidcColour.ReplicateQuad(vidc);

            // Get buffer index
            int bufferIdx = _buffers.GetBufferIndex(objZ);

            // Draw triangle into buffer
            _buffers.AddTriangle(bufferIdx,
                pv1.x, pv1.y, pv2.x, pv2.y, pv3.x, pv3.y, colourWord);

            // Draw shadow if applicable
            if (blueprint.HasShadow)
            {
                int shadowIdx = _buffers.GetShadowBufferIndex(objZ);
                _buffers.AddTriangle(shadowIdx,
                    pv1.shadowX, pv1.shadowY,
                    pv2.shadowX, pv2.shadowY,
                    pv3.shadowX, pv3.shadowY,
                    0);  // Black shadow
            }
        }
    }

    private static int DotProduct(int x, int y, int z, int mx, int my, int mz)
    {
        return (int)(((long)x * mx + (long)y * my + (long)z * mz) >> 31);
    }
}
