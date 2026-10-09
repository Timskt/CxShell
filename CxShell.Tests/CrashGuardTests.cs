using System;
using CxShell.Services;

namespace CxShell.Tests;

public sealed class CrashGuardTests
{
    [Fact]
    public void TooltipFault_IsSurvived()
    {
        // Mirrors the real one: AtomUI's ToolTipService throwing out of Avalonia's
        // raw-input hook. The site is what makes it identifiable, not the text.
        var failure = CaptureThrownBy(new ToolTipService().Handle);

        Assert.True(CrashGuard.IsInputPipelineFailure(failure));
    }

    [Fact]
    public void BusinessLogicFault_StillCrashes()
    {
        var failure = CaptureThrownBy(() => throw new InvalidOperationException("session store is corrupt"));

        Assert.False(CrashGuard.IsInputPipelineFailure(failure));
    }

    [Fact]
    public void RecognitionUsesTheThrowingSite_NotTheMessage()
    {
        // A user-facing string that happens to mention a tooltip must not be treated
        // as a harmless hover failure.
        var failure = CaptureThrownBy(() =>
            throw new InvalidOperationException("The input root must expose its visual root through IPresentationSource."));

        Assert.False(CrashGuard.IsInputPipelineFailure(failure));
    }

    [Fact]
    public void NestedInputFailure_IsFoundThroughTheInnerChain()
    {
        var inner = CaptureThrownBy(new ToolTipService().Handle)!;
        var outer = new Exception("dispatcher wrapped it", inner);

        Assert.True(CrashGuard.IsInputPipelineFailure(outer));
    }

    [Fact]
    public void NullAndSitelessExceptions_AreNotTreatedAsInputFailures()
    {
        Assert.False(CrashGuard.IsInputPipelineFailure(null));
        Assert.False(CrashGuard.IsInputPipelineFailure(new Exception("no target site")));
    }

    private static Exception? CaptureThrownBy(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }

        return null;
    }

    private sealed class ToolTipService
    {
        public void Handle() => throw new InvalidOperationException(
            "The input root must expose its visual root through IPresentationSource.");
    }
}
