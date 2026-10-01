using VelaShell.XServer;

namespace CxShell.Services.X11;

internal sealed class X11DamageRegion
{
    private const int MaxRectangles = 32;

    private readonly object _gate = new();
    private readonly List<XRect> _rectangles = [];
    private bool _full;

    public void MarkFull()
    {
        lock (_gate)
        {
            _full = true;
            _rectangles.Clear();
        }
    }

    public void Add(IReadOnlyList<XRect> rectangles)
    {
        lock (_gate)
        {
            if (_full)
            {
                return;
            }

            foreach (XRect rectangle in rectangles)
            {
                if (!rectangle.IsEmpty)
                {
                    _rectangles.Add(rectangle);
                }
            }

            if (_rectangles.Count > MaxRectangles)
            {
                CollapseToBounds();
            }
        }
    }

    public XRect[] Take(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return [];
        }

        lock (_gate)
        {
            if (_full)
            {
                _full = false;
                _rectangles.Clear();
                return [new XRect(0, 0, width, height)];
            }

            if (_rectangles.Count == 0)
            {
                return [];
            }

            XRect bounds = new(0, 0, width, height);
            XRect[] result = _rectangles
                .Select(rectangle => rectangle.Intersect(bounds))
                .Where(rectangle => !rectangle.IsEmpty)
                .ToArray();
            _rectangles.Clear();
            return result;
        }
    }

    private void CollapseToBounds()
    {
        XRect first = _rectangles[0];
        int left = first.X;
        int top = first.Y;
        int right = first.Right;
        int bottom = first.Bottom;
        for (int i = 1; i < _rectangles.Count; i++)
        {
            XRect rectangle = _rectangles[i];
            left = Math.Min(left, rectangle.X);
            top = Math.Min(top, rectangle.Y);
            right = Math.Max(right, rectangle.Right);
            bottom = Math.Max(bottom, rectangle.Bottom);
        }

        _rectangles.Clear();
        _rectangles.Add(new XRect(left, top, right - left, bottom - top));
    }
}
