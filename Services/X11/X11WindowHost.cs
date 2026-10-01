using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using VelaShell.XServer;

namespace CxShell.Services.X11;

internal sealed class X11WindowHost : IX11ServerHost
{
    private readonly Dictionary<uint, X11RemoteWindow> _windows = [];
    private readonly X11ClipboardOfferGate<X11Server> _clipboardOfferGate = new();
    private X11Server? _server;
    private Window? _mainWindow;
    private Screens? _screens;
    private EventHandler? _screensChanged;

    public X11Server? Server => _server;

    public (int X, int Y) RootOrigin { get; private set; }

    public async Task AttachAsync(X11Server server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _mainWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: { } mainWindow
            }
                ? mainWindow
                : throw new InvalidOperationException("The main application window is not available.");

            _clipboardOfferGate.Invalidate();
            Volatile.Write(ref _server, server);
            if (!ReferenceEquals(_screens, _mainWindow.Screens))
            {
                if (_screens is not null && _screensChanged is not null)
                {
                    _screens.Changed -= _screensChanged;
                }

                _screens = _mainWindow.Screens;
                _screensChanged = (_, _) => UpdateScreenLayout();
                _screens.Changed += _screensChanged;
            }

            UpdateScreenLayout();
        }, DispatcherPriority.Normal, cancellationToken);
    }

    public void Detach()
    {
        _clipboardOfferGate.Invalidate();
        Volatile.Write(ref _server, null);
        Dispatcher.UIThread.Post(() =>
        {
            if (_screens is not null && _screensChanged is not null)
            {
                _screens.Changed -= _screensChanged;
            }

            _screens = null;
            _screensChanged = null;
            foreach (X11RemoteWindow window in _windows.Values.ToArray())
            {
                window.CloseByHost();
            }

            _windows.Clear();
        });
    }

    public void TopLevelMapped(XTopLevelWindow window) => Dispatcher.UIThread.Post(() => Map(window));

    public void TopLevelUnmapped(XTopLevelWindow window) => Dispatcher.UIThread.Post(() =>
    {
        if (_windows.Remove(window.Id, out X11RemoteWindow? native))
        {
            native.CloseByHost();
        }
    });

    public void TopLevelChanged(XTopLevelWindow window, XTopLevelChanges changes) => Dispatcher.UIThread.Post(() =>
    {
        if (_windows.TryGetValue(window.Id, out X11RemoteWindow? native))
        {
            native.ApplyProperties(changes);
        }
    });

    public void TopLevelDamaged(XTopLevelWindow window, IReadOnlyList<XRect> damage) => Dispatcher.UIThread.Post(() =>
    {
        if (_windows.TryGetValue(window.Id, out X11RemoteWindow? native))
        {
            native.ScheduleFrame(damage);
        }
    }, DispatcherPriority.Render);

    public void CursorChanged(XTopLevelWindow? window, XCursor cursor) => Dispatcher.UIThread.Post(() =>
    {
        if (window is not null && _windows.TryGetValue(window.Id, out X11RemoteWindow? native))
        {
            native.ApplyCursor(cursor);
        }
    });

    public void BellRequested(int volume) => Trace.WriteLine($"[X11] Remote application bell requested ({volume}).");

    public void ClipboardChanged(string text) => Dispatcher.UIThread.Post(() => _ = WriteClipboardAsync(text));

    public void WindowManagerRequested(XWindowManagerRequest request) => Dispatcher.UIThread.Post(() =>
    {
        if (!_windows.TryGetValue(request.Window.Id, out X11RemoteWindow? native))
        {
            return;
        }

        switch (request)
        {
            case XStateChangeRequest state:
                native.ApplyStateRequest(state);
                break;
            case XActivateRequest:
                native.ActivateRemote();
                break;
            case XCloseRequest:
                _server?.CloseTopLevel(request.Window);
                break;
            case XMinimizeRequest:
                native.WindowState = WindowState.Minimized;
                break;
        }
    });

    internal void RemoveWindow(X11RemoteWindow window)
    {
        if (_windows.TryGetValue(window.Handle.Id, out X11RemoteWindow? current) && ReferenceEquals(window, current))
        {
            _windows.Remove(window.Handle.Id);
        }
    }

    internal void WindowActivated(X11RemoteWindow window)
    {
        X11Server? server = _server;
        if (server is null)
        {
            return;
        }

        server.FocusTopLevel(window.Handle);
        _ = OfferClipboardAsync(server);
    }

    internal void WindowDeactivated()
    {
        if (_server is { } server && !_windows.Values.Any(window => window.IsActive))
        {
            server.FocusTopLevel(null);
        }
    }

    internal void RequestClose(XTopLevelWindow window) => _server?.CloseTopLevel(window);

    internal PixelPoint ClampOuterPosition(PixelPoint position, PixelSize size)
    {
        PixelRect[] bounds = _screens?.All.Select(screen => screen.Bounds).ToArray() ?? [];
        return X11WindowPlacement.ClampOuterPosition(position, size, bounds);
    }

    private void Map(XTopLevelWindow handle)
    {
        if (_server is null || _windows.ContainsKey(handle.Id))
        {
            return;
        }

        X11RemoteWindow window = new(this, handle);
        _windows.Add(handle.Id, window);
        window.ApplyProperties(XTopLevelChanges.All);

        X11RemoteWindow? owner = handle.Snapshot.TransientFor is { } parent && _windows.TryGetValue(parent.Id, out X11RemoteWindow? parentWindow)
            ? parentWindow
            : null;
        if (owner is null)
        {
            window.Show();
        }
        else
        {
            window.Show(owner);
        }
    }

    private void UpdateScreenLayout()
    {
        if (_server is not { } server || _screens is not { } screens || screens.All.Count == 0)
        {
            return;
        }

        IReadOnlyList<Screen> all = screens.All;
        int minX = all.Min(screen => screen.Bounds.X);
        int minY = all.Min(screen => screen.Bounds.Y);
        int maxX = all.Max(screen => screen.Bounds.Right);
        int maxY = all.Max(screen => screen.Bounds.Bottom);
        RootOrigin = (minX, minY);

        List<XMonitor> monitors = [];
        for (int i = 0; i < all.Count; i++)
        {
            Screen screen = all[i];
            PixelRect bounds = screen.Bounds;
            double dpi = 96 * Math.Max(1, screen.Scaling);
            monitors.Add(new XMonitor(bounds.X - minX, bounds.Y - minY, bounds.Width, bounds.Height)
            {
                Name = string.IsNullOrWhiteSpace(screen.DisplayName) ? $"SCREEN-{i + 1}" : screen.DisplayName,
                Primary = screen.IsPrimary,
                WidthMillimeters = (int)Math.Round(bounds.Width / dpi * 25.4),
                HeightMillimeters = (int)Math.Round(bounds.Height / dpi * 25.4)
            });
        }

        server.SetScreenLayout(maxX - minX, maxY - minY, monitors);
        double scaling = (screens.Primary ?? all[0]).Scaling;
        server.SetDisplayScale(Math.Max(1, (int)Math.Round(96 * scaling)), scaling >= 2 ? (int)Math.Floor(scaling) : 1);
    }

    private async Task OfferClipboardAsync(X11Server server)
    {
        if (!ReferenceEquals(Volatile.Read(ref _server), server) || _mainWindow?.Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            await _clipboardOfferGate.OfferAsync(
                server,
                clipboard.TryGetTextAsync,
                candidate => ReferenceEquals(Volatile.Read(ref _server), candidate),
                server.SetClipboardText);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[X11] Clipboard read failed: {exception.Message}");
        }
    }

    private async Task WriteClipboardAsync(string text)
    {
        try
        {
            if (_mainWindow?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[X11] Clipboard write failed: {exception.Message}");
        }
    }
}
