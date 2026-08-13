using Relander.Core.Engine;
using Relander.Core.Interfaces;
using Relander.Core.Math;
using Relander.Core.Data;

namespace Relander.Tests;

[TestFixture]
public class PlayerOrientationTests
{
    [Test]
    public void InitialPitch_Is180Degrees()
    {
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();

        Assert.That(state.ShipPitch, Is.EqualTo(0),
            "Initial pitch=0: yRoofV=+1 (thrust up). Model Y-flipped so canopy (-Y) maps to world UP.");
    }

    [Test]
    public void InitialDirection_IsZero()
    {
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();

        Assert.That(state.ShipDirection, Is.EqualTo(0));
    }

    [Test]
    public void Thrust_PushesShipUpward()
    {
        // yRoofV=+1 (roof down). Thrust subtracts roof: vy -= +1 → vy more negative → UP.
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();

        var gen = new LandscapeGenerator(state);
        var buffers = new GraphicsBuffers();
        var objectMap = new ObjectMap(gen, new RandomGenerator(42));
        var player = new PlayerController(state, buffers, gen, objectMap);

        // Move off launchpad to avoid landing/refuel interference
        state.XPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;
        state.ZPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;

        int vyBefore = state.YVelocity;

        var input = new TestInput { Thrust = true };
        player.Update(input);

        // yVelocity should be modified by thrust (plus gravity)
        Assert.That(state.YVelocity, Is.Not.EqualTo(vyBefore),
            $"Thrust should change yVelocity. Before: 0x{vyBefore:X8}, After: 0x{state.YVelocity:X8}");
    }

    [Test]
    public void ShipVertices_ProjectToScreen()
    {
        // Original model: canopy at +Y local (v0, y=+0x00500000), undercarriage at -Y local (v5, y=-0x00780000).
        // Pitch=0, yRoofV=+1: canopy maps to world +Y (DOWN, lower on screen), undercarriage maps to -Y (UP, higher).
        // This is the authentic original appearance — viewer from above sees the belly/undercarriage.
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();

        // Verify model matches original source
        var ship = ObjectBlueprints.PlayerShip;
        Assert.That(ship.Vertices[0].Y, Is.GreaterThan(0),
            "v0 (canopy) has positive local Y in original model");
        Assert.That(ship.Vertices[5].Y, Is.LessThan(0),
            "v5 (undercarriage) has negative local Y in original model");
        Assert.That(state.ShipPitch, Is.EqualTo(0));

        var gen = new LandscapeGenerator(state);
        var buffers = new GraphicsBuffers();
        var objectMap = new ObjectMap(gen, new RandomGenerator(42));
        var player = new PlayerController(state, buffers, gen, objectMap);
        player.ComputeRotationMatrix();

        // yRoofV should be positive (roof = world DOWN, thrust subtracts → pushes UP)
        Assert.That(state.YRoofV, Is.GreaterThan(0), "yRoofV should be positive for thrust-up physics");

        // Camera as set by CheckCollisionAndLanding
        state.XCamera = state.XPlayer;
        state.YCamera = 0;
        state.ZCamera = state.ZPlayer + FixedPoint.CAMERA_PLAYER_Z;

        int objX = 0;
        int objY = state.YPlayer - state.YCamera;
        int objZ = FixedPoint.LANDSCAPE_Z_MID;

        int DotY(Relander.Core.Data.Vector3Int v) => (int)(((long)v.X * state.YNoseV + (long)v.Y * state.YRoofV + (long)v.Z * state.YSideV) >> 31);
        int DotX(Relander.Core.Data.Vector3Int v) => (int)(((long)v.X * state.XNoseV + (long)v.Y * state.XRoofV + (long)v.Z * state.XSideV) >> 31);
        int DotZ(Relander.Core.Data.Vector3Int v) => (int)(((long)v.X * state.ZNoseV + (long)v.Y * state.ZRoofV + (long)v.Z * state.ZSideV) >> 31);

        int canopyWY = DotY(ship.Vertices[0]) + objY;
        int underWY = DotY(ship.Vertices[5]) + objY;

        Projection.Project(objX + DotX(ship.Vertices[0]), canopyWY, objZ + DotZ(ship.Vertices[0]), out _, out int canopySY);
        Projection.Project(objX + DotX(ship.Vertices[5]), underWY, objZ + DotZ(ship.Vertices[5]), out _, out int underSY);

        TestContext.WriteLine($"Canopy screenY={canopySY}, Undercarriage screenY={underSY}");
        TestContext.WriteLine($"yRoofV=0x{state.YRoofV:X8}, yNoseV=0x{state.YNoseV:X8}");
        // Both should be on screen; canopy will be lower than undercarriage in original model
        Assert.That(canopySY, Is.InRange(0, 239), "Canopy should be on screen");
        Assert.That(underSY, Is.InRange(0, 239), "Undercarriage should be on screen");
    }

    [Test]
    public void ShipShading_ChangesWithPitch()
    {
        // Shading must use the rotated normal (Lander.arm:5504-5508): the ship's
        // face brightness changes as it pitches. The previous code shaded from
        // the local normal, so the drawn colour set was constant at every pitch.
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();
        // Move off the pad so the landing logic does not interfere
        state.XPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;
        state.ZPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;
        state.YPlayer = FixedPoint.LAUNCHPAD_Y - 2 * FixedPoint.TILE_SIZE;
        state.XVelocity = 0;
        state.YVelocity = 0;
        state.ZVelocity = 0;

        var gen = new LandscapeGenerator(state);
        var buffers = new GraphicsBuffers();
        var objectMap = new ObjectMap(gen, new RandomGenerator(42));
        var player = new PlayerController(state, buffers, gen, objectMap);

        System.Collections.Generic.HashSet<int> ColoursAtPitch(int pitch)
        {
            state.ShipPitch = pitch;
            state.ShipDirection = 0;
            player.Update(new TestInput());
            buffers.AddTerminators();
            var set = new System.Collections.Generic.HashSet<int>();
            for (int b = 0; b < buffers.BufferCount; b++)
            {
                var data = buffers.GetBufferData(b);
                for (int i = 0; i < data.Length; i += 8)
                {
                    if (data[i] != GraphicsBuffers.COMMAND_TRIANGLE) break;
                    set.Add(data[i + 7] & 0xFF);
                }
            }
            buffers.Clear();
            return set;
        }

        var levelColours = ColoursAtPitch(0);
        var pitchedColours = ColoursAtPitch(0x20000000);  // 45 degrees

        TestContext.WriteLine($"Level: [{string.Join(",", levelColours)}]");
        TestContext.WriteLine($"Pitched: [{string.Join(",", pitchedColours)}]");
        Assert.That(pitchedColours, Is.Not.EqualTo(levelColours),
            "Shading must change when the ship pitches (rotated normal, not local)");
    }

    [Test]
    public void Thrust_ChangesShipPosition()
    {
        var state = new GameState();
        state.Initialize();
        state.PlaceOnLaunchpad();

        var gen = new LandscapeGenerator(state);
        var buffers = new GraphicsBuffers();
        var objectMap = new ObjectMap(gen, new RandomGenerator(42));
        var player = new PlayerController(state, buffers, gen, objectMap);

        // Move off launchpad
        state.XPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;
        state.ZPlayer = FixedPoint.LAUNCHPAD_SIZE + FixedPoint.TILE_SIZE;

        int yBefore = state.YPlayer;

        var input = new TestInput { Thrust = true };
        for (int i = 0; i < 5; i++)
            player.Update(input);

        // Ship position should change with thrust
        Assert.That(state.YPlayer, Is.Not.EqualTo(yBefore),
            $"Ship should move with thrust. Before: 0x{yBefore:X8}, After: 0x{state.YPlayer:X8}");
    }
}
