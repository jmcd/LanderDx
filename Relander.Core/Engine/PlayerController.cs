using Relander.Core.Math;
using Relander.Core.Data;
using Relander.Core.Interfaces;

namespace Relander.Core.Engine;

/// <summary>
/// Player ship control: keyboard input, physics, collision detection, and ship drawing.
/// Based on MoveAndDrawPlayer from Lander.arm:1734-2600.
/// </summary>
public class PlayerController
{
    private readonly GameState _state;
    private readonly GraphicsBuffers _buffers;
    private readonly LandscapeGenerator _landscape;
    private readonly ObjectMap _objectMap;

    // Yaw/pitch delta per frame when key held (full circle = 0x40000000 * 4)
    private const int YAW_DELTA = 0x08000000;
    private const int PITCH_DELTA = 0x06000000;

    public PlayerController(GameState state, GraphicsBuffers buffers, LandscapeGenerator landscape, ObjectMap objectMap)
    {
        _state = state;
        _buffers = buffers;
        _landscape = landscape;
        _objectMap = objectMap;
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

        // --- Read keyboard and update orientation ---
        ReadKeyboardInput(input);
        ComputeRotationMatrix();
        UpdatePhysics();

        // --- Check for landing / collision ---
        if (!CheckCollisionAndLanding())
            return false;  // World-space collision — the original jumps straight to LoseLife without drawing the ship

        // --- Draw the ship ---
        DrawShip();

        // --- Vertex/shadow crash flag: the original checks it immediately after
        // drawing the ship (Lander.arm:2214-2217: BL DrawObject / LDRB R10,
        // [R11, #crashedFlag] / CMP R10, #0 / BLNE LoseLife), so the crash
        // registers in the same frame. The previous code read the flag left by
        // the PREVIOUS frame's draw, giving the ship an extra physics step past
        // the penetration point.
        if (_state.CrashedFlag != 0)
        {
            _state.CrashedFlag = 0;
            return false;
        }

        return true;
    }

    private void ReadKeyboardInput(IGameInput input)
    {
        // Set fuel burn rate from keys
        int burnRate = 0;
        if (input.Fire) burnRate |= 1;    // Fire (N)
        if (input.Hover) burnRate |= 2;   // Hover (H)
        if (input.Thrust) burnRate |= 4;  // Full thrust (M)

        _state.FuelBurnRate = burnRate;

        // Zero burn rate if out of fuel
        if (_state.FuelLevel <= 0)
            _state.FuelBurnRate = 0;

        // Keyboard yaw control (A/D)
        if (input.YawLeft)
            _state.ShipDirection -= YAW_DELTA;
        if (input.YawRight)
            _state.ShipDirection += YAW_DELTA;

        // Keyboard pitch control (W/S)
        // Pitch up = nose goes down (more negative y) = increase pitch angle
        if (input.PitchUp)
            _state.ShipPitch += PITCH_DELTA;
        if (input.PitchDown)
            _state.ShipPitch -= PITCH_DELTA;
    }

    // ---- Rotation matrix (Lander.arm:6311-6562) ----

    public void ComputeRotationMatrix()
    {
        ComputeMatrix(_state, _state.ShipPitch, _state.ShipDirection);
    }

    /// <summary>
    /// Rotation matrix for the rocks: angles derived from the main loop counter
    /// (Lander.arm:12507-12518: R0 = mainLoopCount &lt;&lt; 24, R1 = mainLoopCount &lt;&lt; 25),
    /// so rocks spin at a steady speed independent of the player's orientation.
    /// Called each frame before the rocks fall, overwriting the player's matrix —
    /// as in the original, where CalculateRotationMatrix stores to the same
    /// workspace (static objects never read it).
    /// </summary>
    public void ComputeRockRotationMatrix()
    {
        ComputeMatrix(_state, _state.MainLoopCount << 24, _state.MainLoopCount << 25);
    }

    private static void ComputeMatrix(GameState state, int a, int b)
    {
        // sin/cos via sine table (cos = sin at index + 256 = +90 degrees)
        int sinA = SinLookup(a);
        int cosA = SinLookup(a + 0x40000000);  // +90 degrees
        int sinB = SinLookup(b);
        int cosB = SinLookup(b + 0x40000000);  // +90 degrees

        // Rotation matrix (row vectors):
        // [ xNoseV xRoofV xSideV ]   [  cosA*cosB  -sinA*cosB   sinB ]
        // [ yNoseV yRoofV ySideV ] = [     sinA        cosA        0  ]
        // [ zNoseV zRoofV zSideV ]   [ -cosA*sinB   sinA*sinB   cosB ]

        state.XNoseV = FixedPoint.Multiply(cosA, cosB);
        state.XRoofV = -FixedPoint.Multiply(sinA, cosB);
        state.XSideV = sinB;
        state.YNoseV = sinA;
        state.YRoofV = cosA;
        state.YSideV = 0;
        state.ZNoseV = -FixedPoint.Multiply(cosA, sinB);
        state.ZRoofV = FixedPoint.Multiply(sinA, sinB);
        state.ZSideV = cosB;
    }

    private static int SinLookup(int angle)
    {
        // Table[angle >> 22] = sin(2π * index / 1024) * (2^31 - 1)
        // Callers add 0x40000000 (90°) for cosine: SinLookup(angle + 0x40000000) = cos(angle)
        int index = (int)((uint)angle >> 22) & 0x3FF;
        return SineTable.Data[index];
    }

    // Fixed-point multiply: the original's quirky shift-and-add routine
    // (Lander.arm:6412-6447) — see FixedPoint.Multiply.

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

        // Cut engines above highest altitude and persist the cut rate so the
        // exhaust gate sees it (Lander.arm:1904-1907: STRLTB R9, [R11, #fuelBurnRate]).
        // The previous code only cleared a local copy, so the exhaust plume kept
        // spawning every frame above the ceiling while the physics correctly
        // ignored the thrust.
        if (-y > FixedPoint.HIGHEST_ALTITUDE)
        {
            burnRate &= ~6;  // Clear hover and thrust bits
            _state.FuelBurnRate = burnRate;
        }

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

        // --- World-space terrain height under the ship ---
        // Always sample terrain — no SAFE_HEIGHT early-out, because at high speed the
        // ship can travel more than SAFE_HEIGHT per frame and skip the check entirely.
        int terrainAlt = _landscape.GetAltitude(x, z);

        // Altitude at which the undercarriage touches the ground
        // (terrainAlt is Y coord of ground surface; UNDERCARRIAGE_Y is the ship half-height)
        int groundContact = terrainAlt - FixedPoint.UNDERCARRIAGE_Y;

        // --- Ship vs ground objects (Lander.arm:2114-2135) ---
        // When the undercarriage is within SAFE_HEIGHT of the ground, any live
        // object (types 1-11) on the ship's tile destroys the ship.
        if ((uint)(groundContact - y) < (uint)FixedPoint.SAFE_HEIGHT)
        {
            int objType = _objectMap.GetObjectAt(x, z);
            if (objType >= ObjectTypes.FIRST_LIVE_TYPE && objType <= ObjectTypes.LAST_LIVE_TYPE)
                return false;
        }

        // Landing and crash checks only run once the ship has descended below the
        // contact altitude (Lander.arm:2167-2168: CMP R1, R0 / BLGT LandOnLaunchpad).
        // The original never consults the launchpad at higher altitudes, so flying
        // high over the pad neither lands the ship nor crashes it.
        if (y > groundContact)
        {
            if ((uint)x < FixedPoint.LAUNCHPAD_SIZE && (uint)z < FixedPoint.LAUNCHPAD_SIZE)
            {
                int totalSpeed = global::System.Math.Abs(vx) + global::System.Math.Abs(vy) + global::System.Math.Abs(vz);

                // Too fast to land: the original returns without landing or crashing
                // (Lander.arm:2526-2532: CMP R3, #LANDING_SPEED / MOVHS PC, R14) — a
                // fast low pass over the pad flies on, and the vertex/shadow test
                // decides whether it crashes.
                if ((uint)totalSpeed >= FixedPoint.LANDING_SPEED)
                    return true;

                // Safe landing — snap to pad and refuel. STRLO semantics
                // (Lander.arm:2547-2550: ADD R3, R3, #&20 / CMP R3, #&1400 /
                // STRLO R3): the new level is stored only when fuel + 0x20 is
                // below 0x1400 — once refuelling would reach the cap the fuel
                // freezes where it is instead of saturating at 5120.
                _state.YPlayer = FixedPoint.LAUNCHPAD_Y;
                _state.XVelocity = 0;
                _state.YVelocity = 0;
                _state.ZVelocity = 0;
                int newFuel = _state.FuelLevel + FixedPoint.FUEL_REFUEL_RATE;
                if (newFuel < FixedPoint.MAX_FUEL_LEVEL)
                    _state.FuelLevel = newFuel;
                return true;
            }

            // Below the contact altitude off the pad: crash
            return false;
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
                // Multiply vertex by rotation matrix (row vectors):
                // rx = v · row0 = vx*xNoseV + vy*xRoofV + vz*xSideV
                // ry = v · row1 = vx*yNoseV + vy*yRoofV + vz*ySideV
                // rz = v · row2 = vx*zNoseV + vy*zRoofV + vz*zSideV
                rvx = DotProduct(vert.X, vert.Y, vert.Z, _state.XNoseV, _state.XRoofV, _state.XSideV);
                rvy = DotProduct(vert.X, vert.Y, vert.Z, _state.YNoseV, _state.YRoofV, _state.YSideV);
                rvz = DotProduct(vert.X, vert.Y, vert.Z, _state.ZNoseV, _state.ZRoofV, _state.ZSideV);
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

            // Crash test: vertex has penetrated to its ground shadow or below
            // (Lander.arm:5246-5259: CMP R14, R1 / MVNHS R14, #0 / STRHSB — the
            // unsigned HS condition includes equality). The previous strict >
            // missed the exact-graze frame; the "normal landed state" reasoning
            // in the old comment is moot because a landed ship's undercarriage
            // sits 2 * UNDERCARRIAGE_Y above the pad terrain, never at equality.
            if (projectedVertices[v].y >= projectedVertices[v].shadowY)
                _state.CrashedFlag = -1;
        }

        // Draw faces
        foreach (var face in blueprint.Faces)
        {
            // Rotated normal in world space — used for culling, shading and shadowing
            int rnx = face.Normal.X, rny = face.Normal.Y, rnz = face.Normal.Z;
            if (isRotating)
            {
                int nx = face.Normal.X, ny = face.Normal.Y, nz = face.Normal.Z;
                rnx = DotProduct(nx, ny, nz, _state.XNoseV, _state.XRoofV, _state.XSideV);
                rny = DotProduct(nx, ny, nz, _state.YNoseV, _state.YRoofV, _state.YSideV);
                rnz = DotProduct(nx, ny, nz, _state.ZNoseV, _state.ZRoofV, _state.ZSideV);
            }

            // Get projected vertices
            var pv1 = projectedVertices[face.V1];
            var pv2 = projectedVertices[face.V2];
            var pv3 = projectedVertices[face.V3];

            // Shadow first (Lander.arm:5385-5418): drawn BEFORE the visibility
            // test, and only for faces whose rotated normal points up (y < 0) —
            // so up-pointing back-facing faces still cast shadows and
            // down-pointing faces (like the ship's undercarriage) never do.
            if (blueprint.HasShadow && rny < 0)
            {
                int shadowIdx = _buffers.GetShadowBufferIndex(objZ);
                _buffers.AddTriangle(shadowIdx,
                    pv1.shadowX, pv1.shadowY,
                    pv2.shadowX, pv2.shadowY,
                    pv3.shadowX, pv3.shadowY,
                    0);  // Black shadow
            }

            // Back-face culling for rotating objects
            if (isRotating)
            {
                // The original scales the object coordinates up and uses
                // GetDotProduct (Lander.arm:5024-5081) — the quirky multiply is
                // linear in its second operand, so the sign of the exact 64-bit
                // sum of unscaled products equals the sign of the original's
                // scaled 32-bit accumulation (which never overflows by design).
                long dot = (long)FixedPoint.Multiply(rnx, objX)
                         + (long)FixedPoint.Multiply(rny, objY)
                         + (long)FixedPoint.Multiply(rnz, objZ);
                if (dot >= 0) continue;  // Face points away from camera
            }

            // Shading uses the ROTATED normal (Lander.arm:5504-5508: yVertex and
            // xVertex hold the normal after MultiplyVectorByMatrix), so the ship's
            // brightness changes as it pitches and yaws. The previous code shaded
            // from the local normal, making the ship look flat.
            int brightness = (int)((0x80000000u - (uint)rny) >> 28);
            if (rnx < 0) brightness++;
            brightness = global::System.Math.Max(0, brightness - 5);

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
        }
    }

    private static int DotProduct(int x, int y, int z, int mx, int my, int mz)
    {
        // The original's GetDotProduct (Lander.arm:6116-6187) uses the quirky
        // shift-and-add multiply and accumulates in a wrapping 32-bit register.
        return unchecked(FixedPoint.Multiply(x, mx) + FixedPoint.Multiply(y, my) + FixedPoint.Multiply(z, mz));
    }
}
