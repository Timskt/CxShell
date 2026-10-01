using CxShell.Controls;
using CxShell.Models;
using CxShell.ViewModels;

namespace CxShell.Tests;

public sealed class TerminalPtySizeTests
{
    [Fact]
    public void CalculatePtySizeUsesCellGridAndRenderScale()
    {
        var logicalSize = TerminalControl.CalculatePtySize(80, 24, 9.5, 18, 1);
        var scaledSize = TerminalControl.CalculatePtySize(80, 24, 9.5, 18, 1.5);

        Assert.Equal(new TerminalPtySize(80, 24, 760, 432), logicalSize);
        Assert.Equal(new TerminalPtySize(80, 24, 1140, 648), scaledSize);
    }

    [Fact]
    public void CalculatePtySizeFallsBackToUnitScaleWhenScaleIsInvalid()
    {
        var size = TerminalControl.CalculatePtySize(100, 30, 8.25, 17.2, double.NaN);

        Assert.Equal(new TerminalPtySize(100, 30, 825, 516), size);
    }

    [Fact]
    public void RowAndColumnResizePreservesKnownPixelDimensions()
    {
        var viewModel = new TerminalViewModel();
        viewModel.Resize(new TerminalPtySize(120, 40, 1440, 800), notifyRemote: false);

        viewModel.Resize(100, 30, notifyRemote: false);

        Assert.Equal(new TerminalPtySize(100, 30, 1440, 800), viewModel.CurrentPtySize);
    }
}
