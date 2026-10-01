using VelaShell.XServer;

namespace CxShell.Services.X11;

internal static class X11PixelFormat
{
    public static void CopyDamage(
        ReadOnlySpan<uint> source,
        int width,
        int height,
        IReadOnlyList<XRect> damage,
        Span<uint> destination,
        int destinationStride,
        bool hasAlpha)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationStride);
        if (destinationStride < width || source.Length < checked(width * height)
            || destination.Length < checked(destinationStride * height))
        {
            throw new ArgumentException("Pixel buffers do not match the requested dimensions.");
        }

        XRect bounds = new(0, 0, width, height);
        foreach (XRect requested in damage)
        {
            XRect rect = requested.Intersect(bounds);
            if (rect.IsEmpty)
            {
                continue;
            }

            for (int y = rect.Y; y < rect.Bottom; y++)
            {
                CopyRectangle(source, width, height, rect, destination, destinationStride,
                    destinationHeight: destination.Length / destinationStride,
                    destinationX: rect.X, destinationY: rect.Y, hasAlpha);
            }
        }
    }

    public static unsafe void CopyRectangleToFramebuffer(
        ReadOnlySpan<uint> source,
        int sourceWidth,
        int sourceHeight,
        XRect sourceRectangle,
        IntPtr address,
        int rowBytes,
        int destinationHeight,
        int destinationX,
        int destinationY,
        bool hasAlpha)
    {
        if (address == IntPtr.Zero || rowBytes <= 0 || rowBytes % sizeof(uint) != 0)
        {
            throw new ArgumentException("The framebuffer layout is invalid.");
        }

        int stride = rowBytes / sizeof(uint);
        Span<uint> destination = new((void*)address, checked(stride * destinationHeight));
        CopyRectangle(source, sourceWidth, sourceHeight, sourceRectangle, destination, stride,
            destinationHeight, destinationX, destinationY, hasAlpha);
    }

    private static void CopyRectangle(
        ReadOnlySpan<uint> source,
        int sourceWidth,
        int sourceHeight,
        XRect sourceRectangle,
        Span<uint> destination,
        int destinationStride,
        int destinationHeight,
        int destinationX,
        int destinationY,
        bool hasAlpha)
    {
        if (sourceRectangle.IsEmpty || sourceRectangle.X < 0 || sourceRectangle.Y < 0
            || sourceRectangle.Right > sourceWidth || sourceRectangle.Bottom > sourceHeight
            || destinationX < 0 || destinationY < 0
            || destinationX + sourceRectangle.Width > destinationStride
            || destinationY + sourceRectangle.Height > destinationHeight
            || source.Length < checked(sourceWidth * sourceHeight)
            || destination.Length < checked(destinationStride * destinationHeight))
        {
            throw new ArgumentException("The pixel rectangle does not fit the source or destination.");
        }

        for (int row = 0; row < sourceRectangle.Height; row++)
        {
            ReadOnlySpan<uint> sourceRow = source.Slice(
                ((sourceRectangle.Y + row) * sourceWidth) + sourceRectangle.X,
                sourceRectangle.Width);
            Span<uint> destinationRow = destination.Slice(
                ((destinationY + row) * destinationStride) + destinationX,
                sourceRectangle.Width);
            if (hasAlpha)
            {
                sourceRow.CopyTo(destinationRow);
                continue;
            }

            for (int column = 0; column < sourceRectangle.Width; column++)
            {
                destinationRow[column] = sourceRow[column] | 0xFF000000;
            }
        }
    }
}
