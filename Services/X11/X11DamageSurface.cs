using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using VelaShell.XServer;

namespace CxShell.Services.X11;

internal sealed class X11DamageSurface : Control
{
    private const int TileSize = 256;
    private const int MaxFrameRetries = 3;
    private const long MaxSurfacePixels = 32L * 1024 * 1024;

    private readonly XTopLevelWindow _handle;
    private readonly Func<double> _getScale;
    private readonly Func<bool> _canRender;
    private readonly X11DamageRegion _pendingDamage = new();
    private readonly DispatcherTimer _retryTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly List<int> _lockedTiles = [];
    private WriteableBitmap?[] _tiles = [];
    private ILockedFramebuffer?[] _locks = [];
    private XRect[] _frameDamage = [];
    private (int Width, int Height, bool HasAlpha)? _resizeTo;
    private (int Width, int Height, bool HasAlpha)? _bufferSizeOverride;
    private int _width;
    private int _height;
    private int _columns;
    private int _rows;
    private int _frameQueued;
    private bool _hasAlpha;
    private bool _started;
    private bool _released;

    public X11DamageSurface(XTopLevelWindow handle, Func<double> getScale, Func<bool> canRender)
    {
        _handle = handle;
        _getScale = getScale;
        _canRender = canRender;
        _retryTimer.Tick += OnRetryTimerTick;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    public void Start()
    {
        _started = true;
        InvalidateAll();
    }

    public void AddDamage(IReadOnlyList<XRect> damage)
    {
        if (_released)
        {
            return;
        }

        _pendingDamage.Add(damage);
        RequestFrame();
    }

    public void InvalidateAll()
    {
        if (_released)
        {
            return;
        }

        _pendingDamage.MarkFull();
        RequestFrame();
    }

    public void Release()
    {
        _released = true;
        _retryTimer.Stop();
        UnlockTiles();
        foreach (WriteableBitmap? tile in _tiles)
        {
            tile?.Dispose();
        }

        _tiles = [];
        _locks = [];
        _frameDamage = [];
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double scale = Math.Max(1, _getScale());
        for (int index = 0; index < _tiles.Length; index++)
        {
            if (_tiles[index] is not { } tile)
            {
                continue;
            }

            PixelSize size = tile.PixelSize;
            int x = (index % _columns) * TileSize;
            int y = (index / _columns) * TileSize;
            context.DrawImage(tile, new Rect(0, 0, size.Width, size.Height),
                new Rect(x / scale, y / scale, size.Width / scale, size.Height / scale));
        }
    }

    private void RequestFrame()
    {
        if (!_started || _released || !_canRender() || Interlocked.Exchange(ref _frameQueued, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(RenderPendingFrame, DispatcherPriority.Render);
    }

    private void OnRetryTimerTick(object? sender, EventArgs args)
    {
        _retryTimer.Stop();
        RequestFrame();
    }

    private void ScheduleRetry()
    {
        if (!_retryTimer.IsEnabled && !_released)
        {
            _retryTimer.Start();
        }
    }

    private void RenderPendingFrame()
    {
        Interlocked.Exchange(ref _frameQueued, 0);
        if (!_started || _released || !_canRender())
        {
            return;
        }

        try
        {
            for (int attempt = 0; attempt < MaxFrameRetries; attempt++)
            {
                XTopLevelSnapshot snapshot = _handle.Snapshot;
                if (_bufferSizeOverride is { } observed
                    && snapshot.Width == observed.Width && snapshot.Height == observed.Height
                    && snapshot.HasAlpha == observed.HasAlpha)
                {
                    _bufferSizeOverride = null;
                }

                (int Width, int Height, bool HasAlpha) target = _bufferSizeOverride
                    ?? (snapshot.Width, snapshot.Height, snapshot.HasAlpha);
                if (target.Width <= 0 || target.Height <= 0
                    || (long)target.Width * target.Height > MaxSurfacePixels)
                {
                    return;
                }

                if (target.Width != _width || target.Height != _height || target.HasAlpha != _hasAlpha)
                {
                    Resize(target.Width, target.Height);
                    _hasAlpha = target.HasAlpha;
                    _pendingDamage.MarkFull();
                }

                _frameDamage = _pendingDamage.Take(_width, _height);
                if (_frameDamage.Length == 0)
                {
                    return;
                }

                bool read;
                try
                {
                    LockDamagedTiles();
                    _resizeTo = null;
                    bool acquired = _handle.TryReadPixels(CopyDamage, out read);
                    if (!acquired)
                    {
                        _pendingDamage.Add(_frameDamage);
                        ScheduleRetry();
                        return;
                    }
                }
                finally
                {
                    UnlockTiles();
                }

                if (!read)
                {
                    return;
                }

                if (_resizeTo is { } size)
                {
                    _resizeTo = null;
                    if (size.Width > 0 && size.Height > 0
                        && (long)size.Width * size.Height <= MaxSurfacePixels)
                    {
                        _bufferSizeOverride = size;
                        if (size.Width != _width || size.Height != _height)
                        {
                            Resize(size.Width, size.Height);
                        }

                        _hasAlpha = size.HasAlpha;
                        _pendingDamage.MarkFull();
                        continue;
                    }

                    return;
                }

                InvalidateVisual();
                return;
            }

            RequestFrame();
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException
            or OutOfMemoryException or ArgumentException or OverflowException)
        {
            Trace.WriteLine($"[X11] Window {_handle.Id} surface update failed: {exception.Message}");
        }
        finally
        {
            _frameDamage = [];
        }
    }

    private void Resize(int width, int height)
    {
        UnlockTiles();
        foreach (WriteableBitmap? tile in _tiles)
        {
            tile?.Dispose();
        }

        _width = width;
        _height = height;
        _columns = (width + TileSize - 1) / TileSize;
        _rows = (height + TileSize - 1) / TileSize;
        _tiles = new WriteableBitmap?[_columns * _rows];
        _locks = new ILockedFramebuffer?[_tiles.Length];
    }

    private void LockDamagedTiles()
    {
        foreach (XRect damage in _frameDamage)
        {
            XRect clipped = damage.Intersect(new XRect(0, 0, _width, _height));
            if (clipped.IsEmpty)
            {
                continue;
            }

            int firstColumn = clipped.X / TileSize;
            int lastColumn = (clipped.Right - 1) / TileSize;
            int firstRow = clipped.Y / TileSize;
            int lastRow = (clipped.Bottom - 1) / TileSize;
            for (int tileY = firstRow; tileY <= lastRow; tileY++)
            {
                for (int tileX = firstColumn; tileX <= lastColumn; tileX++)
                {
                    int index = (tileY * _columns) + tileX;
                    if (_locks[index] is not null)
                    {
                        continue;
                    }

                    int originX = tileX * TileSize;
                    int originY = tileY * TileSize;
                    WriteableBitmap tile = _tiles[index] ??= new WriteableBitmap(
                        new PixelSize(Math.Min(TileSize, _width - originX), Math.Min(TileSize, _height - originY)),
                        new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                    _locks[index] = tile.Lock();
                    _lockedTiles.Add(index);
                }
            }
        }
    }

    private void UnlockTiles()
    {
        foreach (int index in _lockedTiles)
        {
            _locks[index]?.Dispose();
            _locks[index] = null;
        }

        _lockedTiles.Clear();
    }

    private void CopyDamage(ReadOnlySpan<uint> pixels, int width, int height)
    {
        XTopLevelSnapshot snapshot = _handle.Snapshot;
        if (width != _width || height != _height || snapshot.HasAlpha != _hasAlpha)
        {
            _resizeTo = (width, height, snapshot.HasAlpha);
            return;
        }

        XRect bounds = new(0, 0, width, height);
        foreach (XRect damage in _frameDamage)
        {
            XRect clipped = damage.Intersect(bounds);
            if (clipped.IsEmpty)
            {
                continue;
            }

            int firstColumn = clipped.X / TileSize;
            int lastColumn = (clipped.Right - 1) / TileSize;
            int firstRow = clipped.Y / TileSize;
            int lastRow = (clipped.Bottom - 1) / TileSize;
            for (int tileY = firstRow; tileY <= lastRow; tileY++)
            {
                for (int tileX = firstColumn; tileX <= lastColumn; tileX++)
                {
                    int index = (tileY * _columns) + tileX;
                    if (_locks[index] is not { } framebuffer)
                    {
                        continue;
                    }

                    XRect tileBounds = new(tileX * TileSize, tileY * TileSize,
                        Math.Min(TileSize, width - (tileX * TileSize)),
                        Math.Min(TileSize, height - (tileY * TileSize)));
                    XRect part = clipped.Intersect(tileBounds);
                    X11PixelFormat.CopyRectangleToFramebuffer(
                        pixels,
                        width,
                        height,
                        part,
                        framebuffer.Address,
                        framebuffer.RowBytes,
                        framebuffer.Size.Height,
                        part.X - tileBounds.X,
                        part.Y - tileBounds.Y,
                        _hasAlpha);
                }
            }
        }
    }
}
