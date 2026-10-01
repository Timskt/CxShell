using Avalonia.Controls;
using CxShell.Services.X11;

namespace CxShell.Tests;

public sealed class X11WindowResizePolicyTests
{
    [Theory]
    [InlineData(WindowResizeReason.Application)]
    [InlineData(WindowResizeReason.Layout)]
    [InlineData(WindowResizeReason.DpiChange)]
    [InlineData(WindowResizeReason.Unspecified)]
    public void ShouldReportSize_IgnoresProgrammaticResize(WindowResizeReason reason)
    {
        Assert.False(X11WindowResizePolicy.ShouldReportSize(reason, stateChanged: false,
            applyingProperties: false, minimized: false));
    }

    [Fact]
    public void ShouldReportSize_AcceptsUserResize()
    {
        Assert.True(X11WindowResizePolicy.ShouldReportSize(WindowResizeReason.User, stateChanged: false,
            applyingProperties: false, minimized: false));
    }

    [Fact]
    public void ShouldReportSize_AcceptsStateChangeAfterProgrammaticResizeEvent()
    {
        Assert.True(X11WindowResizePolicy.ShouldReportSize(WindowResizeReason.Application, stateChanged: true,
            applyingProperties: false, minimized: false));
    }

    [Theory]
    [InlineData(WindowResizeReason.User, true, false)]
    [InlineData(WindowResizeReason.User, false, true)]
    [InlineData(WindowResizeReason.Application, true, true)]
    public void ShouldReportSize_RejectsSuppressedResize(WindowResizeReason reason, bool applyingProperties, bool minimized)
    {
        Assert.False(X11WindowResizePolicy.ShouldReportSize(reason, stateChanged: false,
            applyingProperties, minimized));
    }
}
