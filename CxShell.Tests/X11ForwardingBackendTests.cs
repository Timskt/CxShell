using CxShell.Services;
using CxShell.Services.X11;
using CxShell.Models;

namespace CxShell.Tests;

public sealed class X11ForwardingBackendTests
{
    [Fact]
    public async Task ExternalBackend_UsesConfiguredDisplayAndStartsSharedForwardingLifecycle()
    {
        SessionInfo session = new()
        {
            SshX11UseBuiltinServer = false,
            SshX11UseXmanager = false,
            SshX11Display = "192.0.2.10:3.1"
        };
        FakeForwardingTransport transport = new();

        IX11ForwardingBackend backend = X11ForwardingBackendFactory.Create(session, transport);
        await backend.StartAsync(CancellationToken.None);

        Assert.Equal("localhost:10.0", backend.RemoteDisplay);
        Assert.Contains("DISPLAY=localhost:10.0", backend.StatusMessage);
        Assert.Equal(("192.0.2.10", 6003u), Assert.Single(transport.ForwardTargets));
        Assert.Empty(transport.Commands);

        backend.Stop();

        Assert.Equal(1, transport.DisposedForwardCount);
    }

    [Fact]
    public async Task StartAsync_TriesNextRemoteDisplayWhenEarlierPortsAreUnavailable()
    {
        FakeForwardingTransport transport = new()
        {
            UnavailableRemotePorts = [6010, 6011]
        };
        X11ForwardingBackend backend = CreateBackend(transport);

        await backend.StartAsync(CancellationToken.None);

        Assert.Equal(new uint[] { 6010, 6011, 6012 }, transport.AttemptedRemotePorts);
        Assert.Equal("127.0.0.1:12.0", backend.RemoteDisplay);
        backend.Stop();
    }

    [Fact]
    public async Task StartAsync_RemoteAuthorityFailureRollsBackTunnelAndDisplayServer()
    {
        FakeForwardingTransport transport = new()
        {
            CommandResult = new SshCommandExecutionResult("", "permission denied", 1, true)
        };
        TrackingAsyncDisposable owner = new();
        X11ForwardingBackend backend = CreateBackend(transport, owner, includeAuthority: true);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => backend.StartAsync(CancellationToken.None));

        Assert.Contains("permission denied", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            new[] { "umask 077; cat > '/tmp/cxshell-xauthority-test' && chmod 600 '/tmp/cxshell-xauthority-test'" },
            transport.Commands);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(transport.CommandPayloads));
        Assert.Equal(new[] { "/tmp/cxshell-xauthority-test" }, transport.RemovedRemoteFiles);
        Assert.Equal(1, transport.DisposedForwardCount);
        await owner.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(backend.RemoteDisplay);
        Assert.Null(backend.StatusMessage);
    }

    [Fact]
    public async Task Stop_IsIdempotentAndDisposesOwnedServerOnce()
    {
        FakeForwardingTransport transport = new();
        TrackingAsyncDisposable owner = new();
        X11ForwardingBackend backend = CreateBackend(transport, owner, includeAuthority: true);
        await backend.StartAsync(CancellationToken.None);

        backend.Stop();
        backend.Stop();
        await owner.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, transport.DisposedForwardCount);
        Assert.Equal(1, owner.DisposeCount);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(transport.CommandPayloads));
        Assert.Equal(new[] { "/tmp/cxshell-xauthority-test" }, transport.RemovedRemoteFiles);
    }

    [Fact]
    public async Task StartAsync_ClearsTemporaryAuthorityPayloadAfterSending()
    {
        byte[] payload = [1, 2, 3];
        FakeForwardingTransport transport = new();
        X11ForwardingBackend backend = CreateBackend(
            transport,
            includeAuthority: true,
            authorityPayload: payload);

        await backend.StartAsync(CancellationToken.None);

        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(transport.CommandPayloads));
        Assert.Equal(new byte[] { 0, 0, 0 }, payload);
        backend.Stop();
    }

    private static X11ForwardingBackend CreateBackend(
        FakeForwardingTransport transport,
        TrackingAsyncDisposable? owner = null,
        bool includeAuthority = false,
        byte[]? authorityPayload = null)
    {
        byte[] payload = authorityPayload ?? [1, 2, 3];
        X11DisplayEndpoint endpoint = new(
            "127.0.0.1",
            6000,
            "127.0.0.1",
            displayNumber => $"built-in display {displayNumber}",
            includeAuthority ? "/tmp/cxshell-xauthority-test" : null,
            includeAuthority
                ? _ => new X11RemoteAuthorityInstall(
                    X11AuthorityFile.BuildInstallCommand("/tmp/cxshell-xauthority-test"),
                    payload)
                : null,
            owner);

        return new X11ForwardingBackend(transport, _ => Task.FromResult(endpoint));
    }

    private sealed class FakeForwardingTransport : IX11ForwardingTransport
    {
        public bool IsConnected { get; set; } = true;

        public HashSet<uint> UnavailableRemotePorts { get; init; } = [];

        public SshCommandExecutionResult CommandResult { get; set; } = new("", "", 0, true);

        public List<uint> AttemptedRemotePorts { get; } = [];

        public List<(string Host, uint Port)> ForwardTargets { get; } = [];

        public List<string> Commands { get; } = [];

        public List<byte[]> CommandPayloads { get; } = [];

        public List<string> RemovedRemoteFiles { get; } = [];

        public int DisposedForwardCount { get; private set; }

        public IDisposable StartRemoteForward(
            uint remotePort,
            string localHost,
            uint localPort,
            Action<Exception> errorCallback)
        {
            AttemptedRemotePorts.Add(remotePort);
            if (UnavailableRemotePorts.Contains(remotePort))
            {
                throw new InvalidOperationException($"Remote port {remotePort} is unavailable.");
            }

            ForwardTargets.Add((localHost, localPort));
            return new CallbackDisposable(() => DisposedForwardCount++);
        }

        public Task<SshCommandExecutionResult> RunCommandAsync(
            string command,
            ReadOnlyMemory<byte> standardInput,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            CommandPayloads.Add(standardInput.ToArray());
            return Task.FromResult(CommandResult);
        }

        public void RemoveRemoteFile(string path) => RemovedRemoteFiles.Add(path);

        public void ReportError(string message)
        {
        }

        public void Trace(string message)
        {
        }

        private sealed class CallbackDisposable(Action callback) : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    callback();
                }
            }
        }
    }

    private sealed class TrackingAsyncDisposable : IAsyncDisposable
    {
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
