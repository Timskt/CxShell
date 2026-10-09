using System;
using System.Text;
using System.Threading.Tasks;
using CxShell.Models;
using CxShell.Services;

namespace CxShell.Tests;

/// <summary>
/// Drives a real pseudo-terminal without any UI. This is the only coverage the
/// local shell has end to end, and it is what a screenshot cannot show: whether
/// bytes actually come back.
/// </summary>
public sealed class LocalTerminalConnectionServiceTests
{
    [Fact]
    public async Task PseudoTerminal_RoundTripsWhatItIsGiven()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        var marker = "pty-roundtrip-" + Guid.NewGuid().ToString("N");
        using var service = new LocalTerminalConnectionService();
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new StringBuilder();
        string? failure = null;

        service.DataReceived += chunk =>
        {
            lock (received)
            {
                received.Append(chunk);
                if (received.ToString().Contains(marker, StringComparison.Ordinal))
                    seen.TrySetResult();
            }
        };
        service.ErrorOccurred += message => failure ??= message;

        await service.ConnectAsync(LocalSession(), password: null, columns: 80, rows: 24);
        Assert.True(service.IsConnected);

        service.SendData(marker + "\n");

        var completed = await Task.WhenAny(seen.Task, Task.Delay(5000));
        Assert.True(
            completed == seen.Task,
            $"the shell never echoed the marker back; output so far: {received}, error: {failure ?? "none"}");
    }

    [Fact]
    public async Task Disconnecting_RaisesClosedOnceFromTheReaderAndNeverAgain()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        using var service = new LocalTerminalConnectionService();
        var closeCount = 0;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ConnectionClosed += _ =>
        {
            closeCount++;
            closed.TrySetResult();
        };

        await service.ConnectAsync(LocalSession(), password: null);
        service.Disconnect();

        Assert.False(service.IsConnected);

        // The reader notices on its own thread, so the notice has to be awaited.
        var completed = await Task.WhenAny(closed.Task, Task.Delay(5000));
        Assert.True(completed == closed.Task, "disconnecting never reported the shell as closed");

        service.Dispose();
        Assert.Equal(1, closeCount);
    }

    private static SessionInfo LocalSession() => new()
    {
        Id = Guid.NewGuid(),
        Name = "pty-test",
        Protocol = SessionProtocol.Local,
        // cat, not a login shell: no rc files, no prompt, nothing to race against.
        LocalTerminalProfile = new LocalTerminalProfile("cat", "cat", "/bin/cat")
    };
}
