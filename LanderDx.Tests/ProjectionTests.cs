using LanderDx.Core.Engine;

namespace LanderDx.Tests;

[TestFixture]
public class ProjectionTests
{
    // Projection reads the static Viewport; reset to the original 320×240 in
    // case a widescreen test ran earlier in the (unordered) fixture sequence.
    [SetUp]
    public void ResetViewport() => Viewport.Configure(320, 240);

    [Test]
    public void CenterPoint_ProjectsToCenter()
    {
        // A point at z=1 tile, x=0, y=0 should be at screen center
        bool ok = Projection.Project(0, 0, 0x01000000, out int sx, out int sy);
        Assert.That(ok, Is.True);
        Assert.That(sx, Is.EqualTo(160), "Center X");
        Assert.That(sy, Is.EqualTo(64), "Center Y");
    }

    [Test]
    public void PointToRight_ProjectsRightOfCenter()
    {
        // x = 1 tile, z = 10 tiles → offset = 1/10 * 256 = 25.6 ≈ 25 pixels right
        bool ok = Projection.Project(0x01000000, 0, 0x0A000000, out int sx, out int sy);
        Assert.That(ok, Is.True);
        Assert.That(sx, Is.GreaterThan(160));
        Assert.That(sx, Is.EqualTo(160 + 26).Within(2)); // ~25.6 pixels
    }

    [Test]
    public void PointToLeft_ProjectsLeftOfCenter()
    {
        // x = -1 tile, z = 10 tiles
        bool ok = Projection.Project(-0x01000000, 0, 0x0A000000, out int sx, out int sy);
        Assert.That(ok, Is.True);
        Assert.That(sx, Is.LessThan(160));
    }

    [Test]
    public void PointBelow_ProjectsBelowCenter()
    {
        // y increases downward in original; positive y = below center
        bool ok = Projection.Project(0, 0x01000000, 0x01000000, out int sx, out int sy);
        Assert.That(ok, Is.True);
        Assert.That(sy, Is.GreaterThan(64));
    }

    [Test]
    public void BehindCamera_ReturnsFalse()
    {
        // z = 0x80000000 or larger (unsigned) is behind camera
        bool ok = Projection.Project(0, 0, unchecked((int)0x80000000), out _, out _);
        Assert.That(ok, Is.False);
    }

    [Test]
    public void NegativeZ_ReturnsFalse()
    {
        bool ok = Projection.Project(0, 0, -1, out _, out _);
        Assert.That(ok, Is.False);
    }

    [Test]
    public void ZeroZ_ReturnsFalse()
    {
        bool ok = Projection.Project(0, 0, 0, out _, out _);
        Assert.That(ok, Is.False);
    }

    [Test]
    public void CloserObjects_AreLarger()
    {
        // Same x offset, different z: closer = larger screen offset
        int x = 0x01000000; // 1 tile
        Projection.Project(x, 0, 0x05000000, out int sxNear, out _);  // z = 5 tiles
        Projection.Project(x, 0, 0x14000000, out int sxFar, out _);   // z = 20 tiles

        int offsetNear = sxNear - 160;
        int offsetFar = sxFar - 160;

        Assert.That(offsetNear, Is.GreaterThan(offsetFar),
            $"Near offset {offsetNear} should be > far offset {offsetFar}");
    }

    [Test]
    public void ShipZDepth_ProjectsShipVerticesCorrectly()
    {
        // Ship at z = LANDSCAPE_Z_MID = 15 tiles
        // Ship vertex at x = 0x01000000 (1.0 tile)
        int z = LanderDx.Core.Math.FixedPoint.LANDSCAPE_Z_MID;
        bool ok = Projection.Project(0x01000000, 0, z, out int sx, out _);
        Assert.That(ok, Is.True);

        // offset = (1/15) * 256 ≈ 17 pixels
        int expectedOffset = 256 / 15;
        Assert.That(sx, Is.EqualTo(160 + expectedOffset).Within(2),
            $"Ship vertex at x=1,z=15 should be at ~{160 + expectedOffset}, got {sx}");
    }

    [Test]
    public void LandscapeBackRow_ProjectsCorrectly()
    {
        // Landscape back row: z = LANDSCAPE_Z ≈ 20 tiles
        // A tile at x = 6 tiles (right edge of landscape half-width)
        int z = LanderDx.Core.Math.FixedPoint.LANDSCAPE_Z;
        int halfWidth = LanderDx.Core.Math.FixedPoint.LANDSCAPE_X;
        bool ok = Projection.Project(halfWidth, 0, z, out int sx, out _);
        Assert.That(ok, Is.True);
        // Should be on the right side of the screen
        Assert.That(sx, Is.GreaterThan(160));
        Assert.That(sx, Is.LessThan(320)); // Not off-screen
    }

    [Test]
    public void IsOnScreen_RejectsOutOfBounds()
    {
        Assert.That(Projection.IsOnScreen(160, 64), Is.True);
        Assert.That(Projection.IsOnScreen(0, 0), Is.True);
        // SCREEN_MAX_Y = 239 (rasterizer renders up to y=239 inclusive)
        Assert.That(Projection.IsOnScreen(319, 239), Is.True,  "y=239 is the last valid row");
        Assert.That(Projection.IsOnScreen(319, 238), Is.True);
        Assert.That(Projection.IsOnScreen(-1, 0), Is.False);
        Assert.That(Projection.IsOnScreen(0, -1), Is.False);
        Assert.That(Projection.IsOnScreen(320, 0), Is.False);
        Assert.That(Projection.IsOnScreen(0, 240), Is.False, "y=240 is one past the last valid row");
    }
}
