using VelaShell.XServer.Server;

namespace CxShell.Tests;

public sealed class X11ResizeContentionTests
{
    [Fact]
    public void CoalescedValueBatch_KeepsLatestValueAndQueuesOnlyOneFlushPerBatch()
    {
        CoalescedValueBatch<string, int> batch = new();

        Assert.True(batch.Set("window-a", 640));
        Assert.False(batch.Set("window-a", 800));
        Assert.False(batch.Set("window-b", 480));

        Assert.Equal(
            new Dictionary<string, int> { ["window-a"] = 800, ["window-b"] = 480 },
            batch.Take().ToDictionary(item => item.Key, item => item.Value));

        Assert.True(batch.Set("window-a", 1024));
    }

    [Fact]
    public async Task PixelGate_TryEnterHostDoesNotWaitForServerLock()
    {
        PixelGate gate = new();
        using ManualResetEventSlim lockTaken = new();
        using ManualResetEventSlim releaseLock = new();
        Task holder = Task.Run(() =>
        {
            lock (gate.Lock)
            {
                lockTaken.Set();
                releaseLock.Wait();
            }
        });

        await Task.Run(lockTaken.Wait).WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            Assert.False(gate.TryEnterHost());
            Assert.False(gate.HostWaiting);
        }
        finally
        {
            releaseLock.Set();
        }

        await holder.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(gate.TryEnterHost());
        Assert.True(gate.HostWaiting);
        gate.ExitHost();
        Assert.False(gate.HostWaiting);
    }
}
