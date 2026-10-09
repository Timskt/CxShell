using System;
using System.Linq;
using CxShell.Terminal;

namespace CxShell.Tests;

public sealed class ScrollbackRingTests
{
    [Fact]
    public void Add_EvictsTheOldestOnlyAfterTheCapacityIsReached()
    {
        var ring = new ScrollbackRing<int>(3);

        ring.Add(1);
        ring.Add(2);
        ring.Add(3);
        Assert.Equal(new[] { 1, 2, 3 }, ring.ToArray());

        ring.Add(4);

        Assert.Equal(3, ring.Count);
        Assert.Equal(new[] { 2, 3, 4 }, ring.ToArray());
        Assert.Equal(2, ring[0]);
        Assert.Equal(4, ring[2]);
    }

    [Fact]
    public void WrapsAroundTheBufferEndWithoutLosingOrder()
    {
        var ring = new ScrollbackRing<int>(4);

        for (var value = 0; value < 10; value++)
            ring.Add(value);

        Assert.Equal(new[] { 6, 7, 8, 9 }, ring.ToArray());
        Assert.Equal(7, ring[1]);
    }

    [Fact]
    public void ZeroCapacity_HoldsNothing()
    {
        // maxScrollback 0 disables history; the buffer must still accept pushes.
        var ring = new ScrollbackRing<int>(0);

        ring.Add(1);
        ring.Add(2);

        Assert.Equal(0, ring.Count);
        Assert.Empty(ring);
    }

    [Fact]
    public void ConstructorFromSequence_KeepsTheNewestEntries()
    {
        var source = Enumerable.Range(0, 10).ToArray();

        var ring = new ScrollbackRing<int>(3, source);
        var empty = new ScrollbackRing<int>(0, source);

        Assert.Equal(new[] { 7, 8, 9 }, ring.ToArray());
        Assert.Empty(empty);
    }

    [Fact]
    public void IndexerSet_ReplacesInPlace()
    {
        var ring = new ScrollbackRing<string>(3);
        ring.Add("a");
        ring.Add("b");
        ring.Add("c");
        ring.Add("d");

        // Resize rewrites retained rows for the new column count without reordering.
        ring[1] = "B";

        Assert.Equal(new[] { "b", "B", "d" }, ring.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => ring[3] = "x");
    }

    [Fact]
    public void RemoveNewest_TrimsFromTheEnd()
    {
        var ring = new ScrollbackRing<int>(4);
        foreach (var value in new[] { 1, 2, 3 })
            ring.Add(value);

        ring.RemoveNewest();
        ring.RemoveOldest();

        Assert.Equal(new[] { 2 }, ring.ToArray());

        ring.Clear();
        Assert.Equal(0, ring.Count);
        ring.RemoveOldest();
        ring.RemoveNewest();
        Assert.Equal(0, ring.Count);
    }
}
