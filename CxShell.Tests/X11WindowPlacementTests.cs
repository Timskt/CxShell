using Avalonia;
using CxShell.Services.X11;

namespace CxShell.Tests;

public sealed class X11WindowPlacementTests
{
    [Fact]
    public void ClampOuterPosition_KeepsWindowFrameInsideNearestScreen()
    {
        PixelRect screen = new(0, 0, 1920, 1080);

        PixelPoint result = X11WindowPlacement.ClampOuterPosition(
            new PixelPoint(-12, -38),
            new PixelSize(180, 220),
            [screen]);

        Assert.Equal(new PixelPoint(0, 0), result);
    }

    [Fact]
    public void ClampOuterPosition_UsesNearestMonitorForNegativeCoordinates()
    {
        PixelRect left = new(-1920, 0, 1920, 1080);
        PixelRect primary = new(0, 0, 1920, 1080);

        PixelPoint result = X11WindowPlacement.ClampOuterPosition(
            new PixelPoint(-1930, -30),
            new PixelSize(250, 200),
            [left, primary]);

        Assert.Equal(new PixelPoint(-1920, 0), result);
    }

    [Fact]
    public void ClampOuterPosition_LeavesVisibleWindowPositionUnchanged()
    {
        PixelRect screen = new(0, 0, 1920, 1080);
        PixelPoint position = new(240, 160);

        PixelPoint result = X11WindowPlacement.ClampOuterPosition(
            position,
            new PixelSize(180, 220),
            [screen]);

        Assert.Equal(position, result);
    }
}
