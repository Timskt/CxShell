using CxShell.Services.X11;
using VelaShell.XServer;

namespace CxShell.Tests;

public sealed class X11PixelFormatTests
{
    [Fact]
    public void CopyDamage_UpdatesOnlyRequestedPixelsAndHonorsStride()
    {
        uint[] source =
        [
            0x00112233, 0x00445566, 0x00778899, 0x00AABBCC,
            0x00DDEEFF, 0x00010203, 0x00040506, 0x00070809
        ];
        uint[] destination = Enumerable.Repeat(0xDEADBEEF, 12).ToArray();

        X11PixelFormat.CopyDamage(source, width: 4, height: 2,
            [new XRect(1, 0, 2, 2)], destination, destinationStride: 6, hasAlpha: false);

        Assert.Equal(0xDEADBEEF, destination[0]);
        Assert.Equal(0xFF445566, destination[1]);
        Assert.Equal(0xFF778899, destination[2]);
        Assert.Equal(0xDEADBEEF, destination[3]);
        Assert.Equal(0xFF010203, destination[7]);
        Assert.Equal(0xFF040506, destination[8]);
        Assert.Equal(0xDEADBEEF, destination[10]);
        Assert.Equal(0xDEADBEEF, destination[11]);
    }

    [Fact]
    public void CopyDamage_PreservesPremultipliedAlphaPixels()
    {
        uint[] source = [0x80402010, 0x00000000];
        uint[] destination = [0xFFFFFFFF, 0xFFFFFFFF];

        X11PixelFormat.CopyDamage(source, width: 2, height: 1,
            [new XRect(0, 0, 2, 1)], destination, destinationStride: 2, hasAlpha: true);

        Assert.Equal(source, destination);
    }

    [Fact]
    public void CopyDamage_ClipsRectanglesToSurfaceBounds()
    {
        uint[] source = [1, 2, 3, 4];
        uint[] destination = new uint[4];

        X11PixelFormat.CopyDamage(source, width: 2, height: 2,
            [new XRect(-1, -1, 2, 2)], destination, destinationStride: 2, hasAlpha: true);

        Assert.Equal(new uint[] { 1, 0, 0, 0 }, destination);
    }
}
