# Relander

A faithful .NET + Raylib conversion of David Braben's 1987 Acorn Archimedes game **Lander** — the first game ever written for the ARM platform.

The conversion is based on the [fully documented source code](https://lander.bbcelite.com/) reconstructed by Mark Moxon from a disassembly of the original binaries.

## Rules of Implementation

### Fidelity to the Original

The conversion must look and play identically to the original. This means:

1. **Object blueprints must match the original source exactly.** The 13 object models (rock, pyramid, player ship, trees, gazebo, building, rocket, smoking remains) are defined in `Lander.arm` as explicit vertex lists and face data. These must be ported verbatim — never hand-edited or "improved." The source of truth is `Lander.arm` lines 12718–13277.

2. **Math tables must match the original source exactly.** The sine table (1024 entries), arctan table (128 entries), square root table (1024 entries), and division table (64×64 entries) are defined as hard-coded `EQUD` values in `Lander.arm`. These encode Archimedes-specific floating-point precision and cannot be regenerated with modern math functions. The source of truth is `Lander.arm` lines 13311–15070.

3. **The random number generator must match the original algorithm.** The original uses a specific LFSR-based PRNG (`GetRandomNumbers` at `Lander.arm:7830–7854`) with two 32-bit seeds and a carry-flag-dependent rotation. The C# implementation must reproduce the exact same sequence for a given seed pair.

4. **The landscape generation formula must be exact.** `GetLandscapeAltitude` (`Lander.arm:1285–1465`) uses Fourier synthesis with six sine terms at specific frequencies. The integer math (sine table lookups with logical shifts, amplitudes divided by 128 or 256) must be reproduced exactly for identical terrain.

5. **Game constants must match.** All configuration from `Lander.arm` lines 42–178 (`TILE_SIZE`, `SEA_LEVEL`, `LAUNCHPAD_ALTITUDE`, `SAFE_HEIGHT`, etc.) must be preserved. These define the coordinate system, physics, and rendering parameters.

### Testing

**Write tests to investigate bugs, not to guess fixes.** When something looks wrong:

1. Write a test that isolates the suspect subsystem
2. Use the test output to trace the exact data flow
3. Compare against the original source code behavior
4. Fix the root cause, then keep the test as a regression guard

Tests should cover:
- Math table integrity (correct sizes, known values)
- Landscape generation (launchpad flatness, sea level clamping)
- Projection math (known 3D → 2D mappings)
- Object blueprint integrity (vertex counts, face counts)
- Rendering pipeline (pixels in framebuffer after a frame)
- Physics behavior (thrust changes velocity in expected direction)
- Particle lifecycle (spawn, move, draw)

### Architecture Constraints

The solution is three projects:

- **Relander.Core** — pure C# library, zero external dependencies. All game logic lives here. Works with `byte[]` / `Span<byte>` for graphics.
- **Relander** — console app, depends on `Raylib-cs` and `Relander.Core`. Handles window, input, and texture upload.
- **Relander.Tests** — NUnit tests, depends on `Relander.Core`. Tests can simulate input and inspect framebuffer bytes without a GPU.

The Core library must never reference Raylib. Input and output are abstracted through interfaces (`IGameInput`, `IScreen`, `IRandomSource`).

## Implementation Architecture

### Project Structure

```
Relander.Core/
  Math/
    SineTable.cs          — 1024-entry sine lookup (Lander.arm:13311–13568)
    ArctanTable.cs        — 128-entry arctan lookup (Lander.arm:13604–13637)
    SquareRootTable.cs    — 1024-entry sqrt lookup (Lander.arm:13673–13930)
    DivisionTable.cs      — 64×64 division lookup (Lander.arm:13977–15070)
    FixedPoint.cs         — All game constants (Lander.arm:42–178)
  Data/
    ObjectBlueprint.cs    — Data structures: Vector3Int, Face, ObjectBlueprint
    ObjectBlueprints.cs   — All 13 object models (Lander.arm:12718–13277)
    ObjectTypes.cs        — Type→blueprint mapping (Lander.arm:4638–4666)
  Engine/
    GameState.cs          — All workspace variables (Lander.arm:228–498)
    RandomGenerator.cs    — LFSR-based PRNG (Lander.arm:7830–7854)
    LandscapeGenerator.cs — Fourier synthesis + tile colours (Lander.arm:1285–1724)
    VidcColour.cs         — VIDC 8-bit colour encode/decode (Lander.arm:3908–3947)
    GraphicsBuffers.cs    — 12 depth-sorted command buffers (Lander.arm:8904–9038)
    ViewConfig.cs         — Optional view-depth configuration (extra depth rows)
    Projection.cs         — Perspective projection (Lander.arm:7119–7492)
    TriangleRasterizer.cs — Scanline triangle fill (Lander.arm:9278–11502)
    ObjectMap.cs          — 256×256 object placement (Lander.arm:12276–12413)
    PlayerController.cs   — Input, physics, collision, ship drawing (Lander.arm:1734–2600)
    ParticleSystem.cs     — Particle lifecycle + drawing (Lander.arm:2780–3843)
    GameEngine.cs         — Main loop orchestrator (Lander.arm:12485–12549)
  Interfaces/
    IGameInput.cs         — Keyboard abstraction
    IScreen.cs            — Framebuffer abstraction
    IRandomSource.cs      — RNG abstraction

Relander/
  Program.cs              — Raylib init, main loop, texture upload
  RaylibInput.cs          — IGameInput via Raylib-cs keyboard
  RaylibScreen.cs         — IScreen with byte[320×256]

Relander.Tests/
  DataIntegrityTests.cs   — Math table sizes, blueprint counts
  ProjectionTests.cs      — 3D → 2D projection math
  LandscapeTests.cs       — Altitude generation, tile colours
  RenderingTests.cs       — Triangle rasterizer, graphics buffers, VIDC
  GameEngineIntegrationTests.cs — Full pipeline integration
  DiagnosticTests.cs      — Player/object visibility probes
  PlacementDebugTests.cs  — PRNG distribution, object density
  ShadingDiagnosticTests.cs — Face colours, rocket pixel inspection
  PlayerOrientationTests.cs — Pitch, thrust direction, vertex projection
  ParticleTests.cs        — Particle lifecycle, visibility, buffer writing
```

### Coordinate System

The original game uses 32-bit fixed-point coordinates where the top byte is the integer part and the lower 3 bytes are fractional. `TILE_SIZE = 0x01000000 = 1.0` tile units.

- **X-axis**: positive = right on screen
- **Y-axis**: positive = DOWN on screen (inverted from standard math convention)
- **Z-axis**: positive = INTO the screen (increasing z = further from viewer)

Key landmarks:
- Launchpad origin at `(0, 0)` in XZ, extends to `(LAUNCHPAD_SIZE, LAUNCHPAD_SIZE)` = 8×8 tiles
- Sea level at `SEA_LEVEL = 0x05500000` (∼5.3 tiles below the zero plane)
- Camera at `zCamera = zPlayer + CAMERA_PLAYER_Z` (5 tiles behind the player, at the back of the visible landscape)
- Landscape visible from `z = LANDSCAPE_Z_FRONT` (10 tiles) to `z = LANDSCAPE_Z` (20 tiles)
- Ship drawn at fixed screen depth `z = LANDSCAPE_Z_MID` (15 tiles)

### Main Loop

```
GameEngine.Update(input):
  1. PlayerController.Update(input)
     - ReadKeyboardInput: sets fuelBurnRate, adjusts shipDirection/pitch
     - ComputeRotationMatrix: builds 3×3 orientation matrix from angles
     - UpdatePhysics: thrust, hover, friction, gravity, fuel consumption
     - CheckCollisionAndLanding: terrain check, launchpad landing, camera update
     - DrawShip: projects ship vertices, draws faces into graphics buffers
  2. SpawnExhaust: if engines firing, add particles to buffer
  3. ParticleSystem.UpdateAndDraw: move particles, check terrain, project, draw
  4. DrawVisibleObjects: iterate object map, draw 3D objects into buffers
  5. GraphicsBuffers.AddTerminators: write terminators, reset for next frame
  6. DrawLandscapeAndBuffers: back-to-front landscape tile grid + buffer contents
  7. CopyToScreen: framebuffer → screen interface
  8. RenderScoreBar: title, fuel bar and stats onto the top 16 rows
```

### Rotation Matrix

The ship's orientation is defined by two angles:
- `shipPitch` (angle `a`): rotation around the X-axis (nose up/down)
- `shipDirection` (angle `b`): rotation around the Y-axis (yaw)

The 3×3 rotation matrix is stored row-major in `GameState`:

```
[ xNoseV  xRoofV  xSideV ]   [  cos(a)cos(b)  -sin(a)cos(b)   sin(b)  ]
[ yNoseV  yRoofV  ySideV ] = [     sin(a)         cos(a)         0     ]
[ zNoseV  zRoofV  zSideV ]   [ -cos(a)sin(b)   sin(a)sin(b)    cos(b) ]
```

- **Row 0 (nose)**: the ship's forward direction
- **Row 1 (roof)**: the ship's "up" direction (also the thrust/exhaust vector)
- **Row 2 (side)**: perpendicular to nose and roof

Vertices are rotated by computing the dot product with each matrix row:
```
rx = vertex · row0
ry = vertex · row1
rz = vertex · row2
```

Angles are in fixed-point units where a full circle = `2^32`. The sine table has 1024 entries covering 0 to 2π. Cosine is obtained by adding `0x40000000` (90°) to the angle before lookup: `cos(θ) = sin(θ + 90°)`.

### Depth Sorting via Graphics Buffers

The game does not use a Z-buffer. Instead, it uses 12 graphics buffers (one per landscape tile row) for depth sorting:

1. Objects are drawn into buffers based on their screen-depth z-coordinate
2. Landscape tiles are drawn back-to-front (row 0 to row 10)
3. After landscape row N is drawn, graphics buffer N-2 is drawn on top (objects appear 2 rows behind their landscape position)
4. Remaining buffers (9, 10) are drawn after the landscape loop

Each buffer stores drawing commands:
- **Triangle (command 18)**: 8 words — `[18, x1, y1, x2, y2, x3, y3, colour]`
- **Particle (commands 0–8)**: 2 words — `[cmd, packed(x|colour|y)]`
- **Shadow (commands 9–17)**: 2 words — same format, colour = 0
- **Terminator (command 19)**: 1 word — ends the buffer

The buffer index for an object at screen-depth z is: `(z + TILE_SIZE) >> 24`, clamped to `LANDSCAPE_Z_DEPTH` (10). Closer objects get higher buffer numbers (drawn later, on top). Shadows go one buffer lower (without the `+ TILE_SIZE` offset).

### Projection

3D camera-relative coordinates `(x, y, z)` are projected to screen coordinates:

```
screenX = 160 + x * 256 / z
screenY = 64 + y * 256 / z
```

This matches the original's 10-bit ratio approach: the ratio `x/z * 1024` is computed, then `pixel_offset = ratio >> 2 = x/z * 256`. Points with `z >= 0x80000000` (unsigned) are behind the camera and rejected.

The play area is 320×240 pixels (rows 16–255 of the 320×256 mode 13 screen). The top 16 rows are the score bar.

### Landscape Generation

Terrain altitude is computed by Fourier synthesis at `LandscapeGenerator.GetAltitude(x, z)`:

```
altitude = LAND_MID_HEIGHT - (
    2*sin(x - 2z)      / 256 +
    2*sin(4x + 3z)     / 256 +
    2*sin(3z - 5x)     / 256 +
    2*sin(7x + 5z)     / 256 +
    1*sin(5x + 11z)    / 256 +
    1*sin(10x + 7z)    / 256
)
```

The result is clamped to `SEA_LEVEL`, and the launchpad area (8×8 tiles at origin) is forced to `LAUNCHPAD_ALTITUDE`. The sine terms are looked up from the 1024-entry table using `(value >> 22) & 0x3FF` as the index.

Tile colours are computed from altitude bits and slope:
- Green channel: bit 3 of altitude → 4 or 8
- Red channel: bit 2 of altitude → 0 or 4
- Blue channel: 0 (only used for sea tiles)
- Brightness: `tileCornerRow + (slope >> 22)`, added to all channels, clipped to 15
- Launchpad tiles: grey (all channels = 4)
- Sea tiles: blue (B = 4, R = G = 0)

### VIDC Colour Encoding

The Archimedes uses 8-bit palette indices in mode 13 (256 colours). Colours are encoded from 12-bit RGB (4 bits per channel) into an 8-bit VIDC byte:

```
VIDC bit 7 = blue bit 3
VIDC bit 6 = green bit 3
VIDC bit 5 = green bit 2
VIDC bit 4 = red bit 3
VIDC bit 3 = blue bit 2
VIDC bit 2 = red bit 2
VIDC bit 1 = OR of red, green, blue bit 1
VIDC bit 0 = OR of red, green, blue bit 0
```

Since bits 0 and 1 are shared across channels (lossy), the encoding is not perfectly invertible for the lower 2 bits. The byte is replicated 4 times into a 32-bit word for fast 4-pixel writes in 8bpp mode.

### Object Map

A 256×256 byte grid stores object types for every tile on the map. Objects are placed randomly at game start:
- 2048 random positions, avoiding sea level and launchpad tiles
- Object types 1–8 with weighted probabilities (trees most common)
- Three rockets at fixed positions along the launchpad edge (7,1), (7,3), (7,5)
- PRNG produces 64 unique position pairs (by design of the original algorithm)
- Typical object density: ~34–37 objects on the entire map

### Physics

Per-frame updates to the player's ship:

- **Friction**: `velocity -= velocity / 64` on each axis
- **Full thrust** (key M): `velocity -= exhaust / 2048` on each axis
- **Hover** (key H): `velocity -= exhaust / 8192` (quarter thrust, applied after position update for inertia)
- **Gravity**: `yVelocity += gravity` (gravity starts at `0x30000`, increases with score)
- **Position**: `position += velocity`

The exhaust vector is the roof row of the rotation matrix — the direction of the ship's thrust plume.

### Collision Detection

- Ship altitude checked against landscape altitude below the ship
- Safe altitude = `landscape_altitude - UNDERCARRIAGE_Y` (0.39 tiles below ship center)
- If within `SAFE_HEIGHT` (1.5 tiles) of terrain: check for landing or collision
- Launchpad landing: requires ship over launchpad area AND total velocity < `LANDING_SPEED`
- On successful landing: ship stops, refuels at `FUEL_REFUEL_RATE` per frame

### Particles

Up to 484 particles, each 8 words (32 bytes): position (3), velocity (3), lifespan counter, flags.

Flag bits control behavior:
- Bit 16: colour fades white→red over lifespan
- Bit 17: is a rock (3D object, collides with ship)
- Bit 18: splash on sea impact
- Bit 19: bounce on ground (halve velocity, negate y)
- Bit 20: gravity applies
- Bit 21: destroys objects on contact
- Bit 23: big splash (65 spray particles)
- Bit 24: explode on ground impact

Particles are drawn as single pixels (or small blocks for close particles) into depth-sorted graphics buffers. Shadows use commands 9–17 and go to a buffer one step further back.

### Fuel and Scoring

- Initial fuel: 3413 units (`0x0D55`)
- Max fuel: 5120 units (`0x1400`)
- Fuel burn: `fuelBurnRate` units per frame (0=none, 1=fire, 2=hover, 4=full thrust)
- Fire consumes 1 fuel unit: the original subtracts the full burn rate
  including bit 0 (`SUBS R1, R1, R2`, `Lander.arm:5892-5897`)
- Refuel on launchpad: +32 units/frame
- Initial score: 500 (also the bullet count)
- -1 per bullet fired, +20 per object destroyed
- Gravity increases at score thresholds: 1024 → `0x50000`, 1488 → `0x70000`
- Rocks start falling when score ≥ 800

### Input

Keyboard controls (mapped from the original's mouse):

| Key | Action |
|-----|--------|
| A / D | Yaw left / right |
| W / S | Pitch up (nose down) / pitch down (nose up) |
| M | Full thrust |
| H | Hover |
| N | Fire bullets |
| C | Cycle view depth (Original → +10 → +20 → +30 extra rows) |
| Escape | Quit |

### Screen Layout

The original game uses Acorn Mode 13: 320×256 pixels, 256 colours, with double buffering via shadow memory. Two 80K screen banks at Archimedes addresses `0x01FD8000` and `0x01FEC000`.

The conversion uses a single `byte[320×256]` framebuffer. The top 16 pixel rows (score bar area) and the bottom 240 rows (play area) are combined into one buffer. Each byte is a palette index (0–255). The `VidcColour.BuildPalette()` method generates a 256-entry RGBA palette for conversion to modern 32-bit colour.

### Known Differences from Original

1. **Controls**: Keyboard instead of mouse (original used Archimedes mouse with polar coordinate conversion and damping)
2. **Frame rate**: Locked to the original's ~12.5 FPS via a fixed-step accumulator in `Program.cs`; the display runs at its own refresh rate
3. **Sound**: None (original had no sound either)
4. **Game over**: The original blocks until a key press before restarting; the port shows the same message and waits for a key
5. **Minimap**: A Zarch/Virus-inspired radar overlay (Tab/R to toggle) has no original counterpart
6. **View depth**: An opt-in deviation — pressing C cycles the visible landscape depth (Original → +10 → +20 → +30 extra tile-corner rows). The extra far rows sample terrain behind the camera tile and use the darkest brightness shade, the same fix as the original's BigLander variant (Lander.arm `big-landscape` branch: `SUBS R8, R8, #TILES_Z-11` / `MOVLT R8, #0`). The default is the original 11-corner-row view and extended modes are never enabled without the key; the original rows render identically in every mode


