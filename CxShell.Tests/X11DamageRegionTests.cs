using CxShell.Services.X11;
using VelaShell.XServer;

namespace CxShell.Tests;

public sealed class X11DamageRegionTests
{
    [Fact]
    public void Take_ClipsAndConsumesAccumulatedRectangles()
    {
        X11DamageRegion region = new();
        region.Add([new XRect(-2, 4, 8, 5), new XRect(30, 30, 4, 4)]);

        XRect[] first = region.Take(10, 10);
        XRect[] second = region.Take(10, 10);

        Assert.Equal([new XRect(0, 4, 6, 5)], first);
        Assert.Empty(second);
    }

    [Fact]
    public void MarkFull_ReplacesPartialDamageAndReturnsWholeSurfaceOnce()
    {
        X11DamageRegion region = new();
        region.Add([new XRect(2, 3, 4, 5)]);
        region.MarkFull();

        Assert.Equal([new XRect(0, 0, 20, 10)], region.Take(20, 10));
        Assert.Empty(region.Take(20, 10));
    }

    [Fact]
    public void Add_CollapsesExcessRectanglesToBoundedRegion()
    {
        X11DamageRegion region = new();
        region.Add(Enumerable.Range(0, 33).Select(index => new XRect(index * 3, index, 1, 1)).ToArray());

        Assert.Equal([new XRect(0, 0, 97, 33)], region.Take(200, 100));
    }

    [Fact]
    public void TakeWithInvalidDimensions_DoesNotDiscardPendingDamage()
    {
        X11DamageRegion region = new();
        region.Add([new XRect(1, 1, 2, 2)]);

        Assert.Empty(region.Take(0, 10));
        Assert.Equal([new XRect(1, 1, 2, 2)], region.Take(10, 10));
    }
}
