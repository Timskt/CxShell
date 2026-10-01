using Avalonia.Input;
using CxShell.Services.X11;
using VelaShell.XServer;

namespace CxShell.Tests;

public sealed class X11InputMapperTests
{
    [Theory]
    [InlineData(PhysicalKey.A, XKeycodes.A)]
    [InlineData(PhysicalKey.Digit0, XKeycodes.D0)]
    [InlineData(PhysicalKey.ControlRight, XKeycodes.ControlRight)]
    [InlineData(PhysicalKey.ArrowLeft, XKeycodes.Left)]
    [InlineData(PhysicalKey.F12, XKeycodes.F12)]
    public void Keycode_MapsPhysicalKeysToXKeycodes(PhysicalKey key, byte expected)
    {
        Assert.Equal(expected, X11InputMapper.Keycode(key));
    }

    [Theory]
    [InlineData(MouseButton.Left, 1)]
    [InlineData(MouseButton.Middle, 2)]
    [InlineData(MouseButton.Right, 3)]
    [InlineData(MouseButton.XButton1, 8)]
    [InlineData(MouseButton.XButton2, 9)]
    public void Button_MapsMouseButtonsToXButtonNumbers(MouseButton button, int expected)
    {
        Assert.Equal(expected, X11InputMapper.Button(button));
    }
}
