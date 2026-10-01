using Avalonia;

namespace CxShell.Services.X11;

internal static class X11WindowPlacement
{
    public static PixelPoint ClampOuterPosition(PixelPoint position, PixelSize size, IReadOnlyList<PixelRect> screens)
    {
        if (screens.Count == 0)
        {
            return position;
        }

        PixelRect screen = screens.MinBy(bounds => DistanceSquared(position, bounds));
        int maxX = Math.Max(screen.X, screen.Right - Math.Min(size.Width, screen.Width));
        int maxY = Math.Max(screen.Y, screen.Bottom - Math.Min(size.Height, screen.Height));
        return new PixelPoint(Math.Clamp(position.X, screen.X, maxX), Math.Clamp(position.Y, screen.Y, maxY));
    }

    private static long DistanceSquared(PixelPoint point, PixelRect bounds)
    {
        long dx = point.X < bounds.X ? (long)bounds.X - point.X
            : point.X >= bounds.Right ? (long)point.X - bounds.Right + 1
            : 0;
        long dy = point.Y < bounds.Y ? (long)bounds.Y - point.Y
            : point.Y >= bounds.Bottom ? (long)point.Y - bounds.Bottom + 1
            : 0;
        return dx * dx + dy * dy;
    }
}
