using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using CxShell.Models;
using Renci.SshNet;

namespace CxShell.Services.X11;

internal interface IX11ForwardingBackend
{
    string? RemoteDisplay { get; }

    string? RemoteAuthorityPath { get; }

    string? StatusMessage { get; }

    Task StartAsync(CancellationToken cancellationToken);

    void Stop();
}

internal interface IX11ForwardingTransport
{
    bool IsConnected { get; }

    IDisposable StartRemoteForward(
        uint remotePort,
        string localHost,
        uint localPort,
        Action<Exception> errorCallback);

    Task<SshCommandExecutionResult> RunCommandAsync(
        string command,
        ReadOnlyMemory<byte> standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    void RemoveRemoteFile(string path);

    void ReportError(string message);

    void Trace(string message);
}

internal sealed record X11DisplayEndpoint(
    string LocalHost,
    uint LocalPort,
    string RemoteDisplayHost,
    Func<int, string> CreateStatusMessage,
    string? RemoteAuthorityPath = null,
    Func<int, X11RemoteAuthorityInstall>? CreateRemoteAuthorityInstall = null,
    IAsyncDisposable? Owner = null);

internal sealed class X11ForwardingBackend(
    IX11ForwardingTransport transport,
    Func<CancellationToken, Task<X11DisplayEndpoint>> createEndpoint) : IX11ForwardingBackend
{
    private X11DisplayEndpoint? _endpoint;
    private IDisposable? _remoteForward;
    private int _stopped;

    public string? RemoteDisplay { get; private set; }

    public string? RemoteAuthorityPath { get; private set; }

    public string? StatusMessage { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopped) != 0, this);
        if (!transport.IsConnected)
        {
            throw new InvalidOperationException("The SSH connection is not active.");
        }

        try
        {
            _endpoint = await createEndpoint(cancellationToken).ConfigureAwait(false);
            Exception? lastError = null;
            uint selectedDisplayNumber = 0;

            for (uint displayNumber = 10; displayNumber <= 19; displayNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    _remoteForward = transport.StartRemoteForward(
                        6000 + displayNumber,
                        _endpoint.LocalHost,
                        _endpoint.LocalPort,
                        exception => transport.ReportError($"SSH X11 forwarding failed: {exception.Message}"));
                    selectedDisplayNumber = displayNumber;
                    break;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    lastError = exception;
                }
            }

            if (_remoteForward is null)
            {
                throw new InvalidOperationException(
                    $"No remote X11 display port is available: {lastError?.Message ?? "unknown forwarding error"}",
                    lastError);
            }

            RemoteDisplay = $"{_endpoint.RemoteDisplayHost}:{selectedDisplayNumber}.0";
            RemoteAuthorityPath = _endpoint.RemoteAuthorityPath;

            X11RemoteAuthorityInstall? authorityInstall =
                _endpoint.CreateRemoteAuthorityInstall?.Invoke((int)selectedDisplayNumber);
            if (authorityInstall is not null)
            {
                SshCommandExecutionResult result;
                try
                {
                    result = await transport.RunCommandAsync(
                        authorityInstall.Command,
                        authorityInstall.Payload,
                        TimeSpan.FromSeconds(10),
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(authorityInstall.Payload);
                }

                if (!result.Succeeded)
                {
                    string details = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(details)
                            ? $"Remote Xauthority setup exited with status {result.ExitStatus}."
                            : details.Trim());
                }
            }

            StatusMessage = _endpoint.CreateStatusMessage((int)selectedDisplayNumber);
            transport.Trace($"started X11 display {RemoteDisplay} -> {_endpoint.LocalHost}:{_endpoint.LocalPort}");
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(RemoteAuthorityPath) && transport.IsConnected)
        {
            try
            {
                transport.RemoveRemoteFile(RemoteAuthorityPath);
            }
            catch
            {
                // The remote shell also removes the temporary authority file on exit.
            }
        }

        try
        {
            _remoteForward?.Dispose();
        }
        catch
        {
            // SSH may already be disconnecting.
        }

        _remoteForward = null;
        RemoteDisplay = null;
        RemoteAuthorityPath = null;
        StatusMessage = null;

        IAsyncDisposable? owner = _endpoint?.Owner;
        _endpoint = null;
        if (owner is not null)
        {
            _ = Task.Run(() => DisposeOwnerAsync(owner));
        }
    }

    private static async Task DisposeOwnerAsync(IAsyncDisposable owner)
    {
        try
        {
            await owner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"X11 display backend shutdown failed: {exception.Message}");
        }
    }
}

internal static class X11ForwardingBackendFactory
{
    public static IX11ForwardingBackend Create(SessionInfo session, IX11ForwardingTransport transport)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(transport);

        return session.SshX11UseBuiltinServer
            ? new X11ForwardingBackend(transport, CreateBuiltinEndpointAsync)
            : new X11ForwardingBackend(transport, cancellationToken => Task.FromResult(CreateExternalEndpoint(session)));
    }

    private static async Task<X11DisplayEndpoint> CreateBuiltinEndpointAsync(CancellationToken cancellationToken)
    {
        BuiltinX11ServerSession server = await BuiltinX11ServerSession.StartAsync(cancellationToken).ConfigureAwait(false);
        return new X11DisplayEndpoint(
            "127.0.0.1",
            (uint)server.Port,
            "127.0.0.1",
            displayNumber => $"[CxShell built-in X server enabled: DISPLAY=127.0.0.1:{displayNumber}.0]",
            server.RemoteAuthorityPath,
            server.BuildRemoteAuthorityInstall,
            server);
    }

    private static X11DisplayEndpoint CreateExternalEndpoint(SessionInfo session)
    {
        (string host, uint port) = ResolveLocalDisplay(session);
        return new X11DisplayEndpoint(
            host,
            port,
            "localhost",
            displayNumber =>
                $"[SSH X11 forwarding enabled: DISPLAY=localhost:{displayNumber}.0, local target={host}:{port}]");
    }

    private static (string Host, uint Port) ResolveLocalDisplay(SessionInfo session)
    {
        string display = session.SshX11UseXmanager || string.IsNullOrWhiteSpace(session.SshX11Display)
            ? "localhost:0.0"
            : session.SshX11Display.Trim();

        string host = "localhost";
        string displayPart = display;
        int separatorIndex = display.LastIndexOf(':');
        if (separatorIndex >= 0)
        {
            host = string.IsNullOrWhiteSpace(display[..separatorIndex])
                ? "localhost"
                : display[..separatorIndex];
            displayPart = display[(separatorIndex + 1)..];
        }

        int screenSeparator = displayPart.IndexOf('.');
        string displayNumberText = screenSeparator >= 0 ? displayPart[..screenSeparator] : displayPart;
        uint displayNumber = uint.TryParse(displayNumberText, NumberStyles.None, CultureInfo.InvariantCulture, out uint parsed)
            ? parsed
            : 0;

        return (host, 6000 + displayNumber);
    }
}

internal sealed class SshNetX11ForwardingTransport : IX11ForwardingTransport
{
    private readonly SshClient _client;
    private readonly Func<string, ReadOnlyMemory<byte>, TimeSpan, CancellationToken, Task<SshCommandExecutionResult>> _runCommand;
    private readonly Action<string> _reportError;
    private readonly Action<string> _trace;

    public SshNetX11ForwardingTransport(
        SshClient client,
        Func<string, ReadOnlyMemory<byte>, TimeSpan, CancellationToken, Task<SshCommandExecutionResult>> runCommand,
        Action<string> reportError,
        Action<string> trace)
    {
        _client = client;
        _runCommand = runCommand;
        _reportError = reportError;
        _trace = trace;
    }

    public bool IsConnected => _client.IsConnected;

    public IDisposable StartRemoteForward(
        uint remotePort,
        string localHost,
        uint localPort,
        Action<Exception> errorCallback)
    {
        ForwardedPortRemote forwardedPort = new("127.0.0.1", remotePort, localHost, localPort);
        forwardedPort.Exception += (_, args) => errorCallback(args.Exception);
        try
        {
            _client.AddForwardedPort(forwardedPort);
            forwardedPort.Start();
            return new ForwardedPortLease(_client, forwardedPort);
        }
        catch
        {
            try
            {
                _client.RemoveForwardedPort(forwardedPort);
            }
            catch
            {
                // A failed start may not have registered the port.
            }

            forwardedPort.Dispose();
            throw;
        }
    }

    public Task<SshCommandExecutionResult> RunCommandAsync(
        string command,
        ReadOnlyMemory<byte> standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken) => _runCommand(command, standardInput, timeout, cancellationToken);

    public void RemoveRemoteFile(string path)
    {
        using SshCommand command = _client.CreateCommand($"rm -f {X11AuthorityFile.QuotePosix(path)}");
        command.CommandTimeout = TimeSpan.FromSeconds(2);
        command.Execute();
    }

    public void ReportError(string message) => _reportError(message);

    public void Trace(string message) => _trace(message);

    private sealed class ForwardedPortLease(SshClient client, ForwardedPortRemote port) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (port.IsStarted)
                {
                    port.Stop();
                }
            }
            catch
            {
                // Ignore forwarding shutdown failures during disconnect.
            }

            try
            {
                client.RemoveForwardedPort(port);
            }
            catch
            {
                // The client may already be disconnecting.
            }

            port.Dispose();
        }
    }
}
