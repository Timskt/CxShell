using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using VelaShell.XServer;

namespace CxShell.Services.X11;

internal sealed class X11RemoteWindow : Window
{
    private readonly X11WindowHost _host;
    private readonly X11DamageSurface _surface;
    private readonly HashSet<byte> _heldKeys = [];
    private readonly HashSet<int> _heldButtons = [];
    private (int X, int Y) _lastPointer;
    private XFrameExtents _frameExtents;
    private XFrameExtents? _reportedFrameExtents;
    private X11Server? _frameExtentsServer;
    private bool _applyingProperties;
    private bool _closingByHost;
    private WindowState _resizeState = WindowState.Normal;
    private double _wheelRemainderX;
    private double _wheelRemainderY;

    public X11RemoteWindow(X11WindowHost host, XTopLevelWindow handle)
    {
        _host = host;
        Handle = handle;
        _surface = new X11DamageSurface(handle, () => Scale,
            () => IsVisible && WindowState is not WindowState.Minimized);
        Content = _surface;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Focusable = true;

        Opened += (_, _) =>
        {
            UpdateFrameExtents();
            ApplyGeometry();
            _surface.Start();
        };
        PositionChanged += (_, _) => ReportPosition();
        Resized += OnResized;
        Activated += (_, _) => _host.WindowActivated(this);
        Deactivated += (_, _) =>
        {
            ReleaseInput();
            _host.WindowDeactivated();
        };
        ScalingChanged += (_, _) =>
        {
            UpdateFrameExtents();
            ApplyGeometry();
            _surface.InvalidateAll();
        };
        Closing += (_, args) =>
        {
            if (!_closingByHost)
            {
                args.Cancel = true;
                _host.RequestClose(Handle);
            }
        };
        Closed += (_, _) =>
        {
            _host.RemoveWindow(this);
            _surface.Release();
        };
    }

    public XTopLevelWindow Handle { get; }

    public void ApplyProperties(XTopLevelChanges changes)
    {
        XTopLevelSnapshot snapshot = Handle.Snapshot;
        _applyingProperties = true;
        try
        {
            if ((changes & XTopLevelChanges.Title) != 0)
            {
                Title = snapshot.Title.Length > 0 ? snapshot.Title : snapshot.ClassName;
            }

            if ((changes & (XTopLevelChanges.Hints | XTopLevelChanges.States | XTopLevelChanges.Shape)) != 0)
            {
                WindowDecorations = snapshot.OverrideRedirect || !snapshot.Decorated
                    ? WindowDecorations.None
                    : WindowDecorations.Full;
                ShowInTaskbar = !snapshot.OverrideRedirect && snapshot.TransientFor is null
                    && snapshot.WindowType is XWindowType.Normal or XWindowType.Dialog;
                Topmost = snapshot.OverrideRedirect || (snapshot.States & XWindowStates.Above) != 0;
                Opacity = Math.Clamp(snapshot.Opacity, 0.05, 1);
                ApplyWindowState(snapshot.States);
            }

            if ((changes & (XTopLevelChanges.Geometry | XTopLevelChanges.Hints)) != 0)
            {
                ApplyGeometry();
                UpdateFrameExtents();
            }
        }
        finally
        {
            _applyingProperties = false;
        }

        if ((changes & (XTopLevelChanges.Geometry | XTopLevelChanges.States
            | XTopLevelChanges.Hints | XTopLevelChanges.Shape)) != 0)
        {
            _surface.InvalidateAll();
        }
    }

    public void ScheduleFrame(IReadOnlyList<XRect> damage) => _surface.AddDamage(damage);

    public void ApplyCursor(XCursor cursor)
    {
        StandardCursorType type = cursor.Shape switch
        {
            XCursorShape.Hidden => StandardCursorType.None,
            XCursorShape.Text => StandardCursorType.Ibeam,
            XCursorShape.Wait => StandardCursorType.Wait,
            XCursorShape.Help => StandardCursorType.Help,
            XCursorShape.Hand => StandardCursorType.Hand,
            XCursorShape.Crosshair => StandardCursorType.Cross,
            XCursorShape.Move => StandardCursorType.SizeAll,
            XCursorShape.ResizeNorth or XCursorShape.ResizeSouth => StandardCursorType.SizeNorthSouth,
            XCursorShape.ResizeEast or XCursorShape.ResizeWest => StandardCursorType.SizeWestEast,
            _ => StandardCursorType.Arrow
        };
        Cursor = new Cursor(type);
    }

    public void ApplyStateRequest(XStateChangeRequest request)
    {
        XWindowStates target = (Handle.Snapshot.States & ~request.Remove) | request.Add;
        _applyingProperties = true;
        try
        {
            ApplyWindowState(target);
            Topmost = (target & XWindowStates.Above) != 0;
        }
        finally
        {
            _applyingProperties = false;
        }

        _host.Server?.SetTopLevelStates(Handle, target);
    }

    public void ActivateRemote() => Activate();

    public void CloseByHost()
    {
        _closingByHost = true;
        Close();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (PointerPosition(e) is { } position)
        {
            _lastPointer = position;
            _host.Server?.InjectPointerMotion(Handle, position.X, position.Y);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        int button = X11InputMapper.Button(e.GetCurrentPoint(this).Properties.PointerUpdateKind);
        if (button == 0 || PointerPosition(e) is not { } position)
        {
            return;
        }

        _lastPointer = position;
        _heldButtons.Add(button);
        e.Pointer.Capture(this);
        _host.Server?.InjectPointerMotion(Handle, position.X, position.Y);
        _host.Server?.InjectPointerButton(Handle, position.X, position.Y, button, pressed: true);
        _host.Server?.FocusTopLevel(Handle);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        int button = X11InputMapper.Button(e.InitialPressMouseButton);
        if (button == 0)
        {
            return;
        }

        _heldButtons.Remove(button);
        _host.Server?.InjectPointerButton(Handle, _lastPointer.X, _lastPointer.Y, button, pressed: false);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        ReleaseButtons();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _host.Server?.InjectPointerLeave();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (PointerPosition(e) is not { } position)
        {
            return;
        }

        _lastPointer = position;
        _wheelRemainderY += e.Delta.Y;
        _wheelRemainderX += e.Delta.X;
        while (Math.Abs(_wheelRemainderY) >= 1)
        {
            SendWheel(position, _wheelRemainderY > 0 ? 4 : 5);
            _wheelRemainderY -= Math.Sign(_wheelRemainderY);
        }

        while (Math.Abs(_wheelRemainderX) >= 1)
        {
            SendWheel(position, _wheelRemainderX > 0 ? 6 : 7);
            _wheelRemainderX -= Math.Sign(_wheelRemainderX);
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        InjectKey(e.PhysicalKey, pressed: true, e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        InjectKey(e.PhysicalKey, pressed: false, e);
    }

    private void InjectKey(PhysicalKey physicalKey, bool pressed, KeyEventArgs e)
    {
        byte keycode = X11InputMapper.Keycode(physicalKey);
        if (keycode == 0)
        {
            return;
        }

        if (pressed)
        {
            _heldKeys.Add(keycode);
        }
        else
        {
            _heldKeys.Remove(keycode);
        }

        _host.Server?.InjectKey(keycode, pressed);
        e.Handled = true;
    }

    private void ApplyGeometry()
    {
        XTopLevelSnapshot snapshot = Handle.Snapshot;
        if (WindowState is WindowState.Maximized or WindowState.FullScreen || snapshot.Width <= 0 || snapshot.Height <= 0)
        {
            return;
        }

        double scale = Scale;
        Width = Math.Max(1, snapshot.Width) / scale;
        Height = Math.Max(1, snapshot.Height) / scale;
        (int originX, int originY) = _host.RootOrigin;
        PixelPoint desired = new(snapshot.X + originX - _frameExtents.Left, snapshot.Y + originY - _frameExtents.Top);
        PixelSize outerSize = new(
            snapshot.Width + _frameExtents.Left + _frameExtents.Right,
            snapshot.Height + _frameExtents.Top + _frameExtents.Bottom);
        PixelPoint position = _host.ClampOuterPosition(desired, outerSize);
        Position = position;

        int clientX = position.X + _frameExtents.Left - originX;
        int clientY = position.Y + _frameExtents.Top - originY;
        if (clientX != snapshot.X || clientY != snapshot.Y)
        {
            _host.Server?.MoveTopLevel(Handle, clientX, clientY);
        }
    }

    private void UpdateFrameExtents()
    {
        if (WindowDecorations != WindowDecorations.None && FrameSize is { } outer)
        {
            int side = Math.Max(0, (int)Math.Round((outer.Width - ClientSize.Width) * Scale / 2));
            int top = Math.Max(0, (int)Math.Round((outer.Height - ClientSize.Height) * Scale) - side);
            _frameExtents = new XFrameExtents(side, side, top, side);
        }
        else
        {
            _frameExtents = default;
        }

        X11Server? server = _host.Server;
        if (server is null)
        {
            _frameExtentsServer = null;
            _reportedFrameExtents = null;
            return;
        }

        if (ReferenceEquals(server, _frameExtentsServer) && _reportedFrameExtents == _frameExtents)
        {
            return;
        }

        server.SetTopLevelFrameExtents(Handle, _frameExtents);
        _frameExtentsServer = server;
        _reportedFrameExtents = _frameExtents;
    }

    private void ApplyWindowState(XWindowStates states)
    {
        WindowState = (states & XWindowStates.Fullscreen) != 0 ? WindowState.FullScreen
            : (states & XWindowStates.Hidden) != 0 ? WindowState.Minimized
            : (states & XWindowStates.Maximized) == XWindowStates.Maximized ? WindowState.Maximized
            : WindowState.Normal;
    }

    private void ReportPosition()
    {
        if (_applyingProperties || _host.Server is not { } server)
        {
            return;
        }

        (int originX, int originY) = _host.RootOrigin;
        int x = Position.X + _frameExtents.Left - originX;
        int y = Position.Y + _frameExtents.Top - originY;
        XTopLevelSnapshot snapshot = Handle.Snapshot;
        if (x != snapshot.X || y != snapshot.Y)
        {
            server.MoveTopLevel(Handle, x, y);
        }
    }

    private void OnResized(object? sender, WindowResizedEventArgs args)
    {
        UpdateFrameExtents();
        bool stateChanged = WindowState != _resizeState;
        _resizeState = WindowState;
        if (!X11WindowResizePolicy.ShouldReportSize(args.Reason, stateChanged, _applyingProperties,
                WindowState is WindowState.Minimized)
            || _host.Server is not { } server)
        {
            return;
        }

        int width = (int)Math.Round(args.ClientSize.Width * Scale);
        int height = (int)Math.Round(args.ClientSize.Height * Scale);
        XTopLevelSnapshot snapshot = Handle.Snapshot;
        if (width > 0 && height > 0 && (width != snapshot.Width || height != snapshot.Height))
        {
            server.ResizeTopLevel(Handle, width, height);
        }

        ReportPosition();
    }

    private void SendWheel((int X, int Y) position, int button)
    {
        _host.Server?.InjectPointerButton(Handle, position.X, position.Y, button, pressed: true);
        _host.Server?.InjectPointerButton(Handle, position.X, position.Y, button, pressed: false);
    }

    private (int X, int Y)? PointerPosition(PointerEventArgs e)
    {
        Point point = e.GetPosition(this);
        if (double.IsNaN(point.X) || double.IsNaN(point.Y))
        {
            return null;
        }

        return ((int)Math.Floor(point.X * Scale), (int)Math.Floor(point.Y * Scale));
    }

    private void ReleaseInput()
    {
        foreach (byte keycode in _heldKeys.ToArray())
        {
            _host.Server?.InjectKey(keycode, pressed: false);
        }

        _heldKeys.Clear();
        ReleaseButtons();
    }

    private void ReleaseButtons()
    {
        foreach (int button in _heldButtons.ToArray())
        {
            _host.Server?.InjectPointerButton(Handle, _lastPointer.X, _lastPointer.Y, button, pressed: false);
        }

        _heldButtons.Clear();
    }

    private double Scale => RenderScaling > 0 ? RenderScaling : 1;
}
