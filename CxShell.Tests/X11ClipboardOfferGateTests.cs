using CxShell.Services.X11;

namespace CxShell.Tests;

public sealed class X11ClipboardOfferGateTests
{
    [Fact]
    public async Task OfferAsync_AppliesEmptyClipboardToClearRemoteValue()
    {
        X11ClipboardOfferGate<object> gate = new();
        object source = new();
        string? appliedText = "stale clipboard contents";

        await gate.OfferAsync(
            source,
            () => Task.FromResult<string?>(null),
            candidate => ReferenceEquals(candidate, source),
            text => appliedText = text);

        Assert.Equal(string.Empty, appliedText);
    }

    [Fact]
    public async Task OfferAsync_DoesNotApplyOlderReadAfterNewerRead()
    {
        X11ClipboardOfferGate<object> gate = new();
        object source = new();
        TaskCompletionSource<string?> olderRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<string?> newerRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> appliedTexts = [];

        Task olderOffer = gate.OfferAsync(source, () => olderRead.Task, _ => true, appliedTexts.Add);
        Task newerOffer = gate.OfferAsync(source, () => newerRead.Task, _ => true, appliedTexts.Add);

        newerRead.SetResult("new clipboard contents");
        await newerOffer;
        olderRead.SetResult("old clipboard contents");
        await olderOffer;

        Assert.Equal(new[] { "new clipboard contents" }, appliedTexts);
    }

    [Fact]
    public async Task Invalidate_PreventsPendingReadFromUpdatingDetachedServer()
    {
        X11ClipboardOfferGate<object> gate = new();
        object source = new();
        TaskCompletionSource<string?> pendingRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> appliedTexts = [];

        Task offer = gate.OfferAsync(source, () => pendingRead.Task, _ => true, appliedTexts.Add);
        gate.Invalidate();
        pendingRead.SetResult("late clipboard contents");
        await offer;

        Assert.Empty(appliedTexts);
    }
}
