using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using VelaShell.XServer;

namespace CxShell.Services.X11;

internal sealed class BuiltinX11ServerSession : IAsyncDisposable
{
    private readonly X11WindowHost _host;
    private readonly X11Server _server;
    private readonly byte[] _authorizationCookie;
    private int _disposed;

    private BuiltinX11ServerSession(X11WindowHost host, X11Server server, byte[] authorizationCookie)
    {
        _host = host;
        _server = server;
        _authorizationCookie = authorizationCookie;
        DisplayNumber = server.DisplayNumber;
        Port = server.Port;
        RemoteAuthorityPath = X11AuthorityFile.CreateRemotePath();
    }

    public int DisplayNumber { get; }

    public int Port { get; }

    public string RemoteAuthorityPath { get; }

    public static async Task<BuiltinX11ServerSession> StartAsync(CancellationToken cancellationToken = default)
    {
        byte[] cookie = RandomNumberGenerator.GetBytes(16);
        SocketException? lastSocketException = null;
        try
        {
            for (int displayNumber = 100; displayNumber < 200; displayNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                X11WindowHost host = new();
                X11Server server = new(new X11ServerOptions
                {
                    DisplayNumber = displayNumber,
                    ListenAddress = IPAddress.Loopback,
                    UnixSocketPath = string.Empty,
                    AuthorizationCookie = cookie,
                    Vendor = "CxShell",
                    WindowManagerName = "CxShell",
                    SyncClipboard = true
                }, host);

                try
                {
                    await server.StartAsync(cancellationToken).ConfigureAwait(false);
                    await host.AttachAsync(server, cancellationToken).ConfigureAwait(false);
                    return new BuiltinX11ServerSession(host, server, cookie);
                }
                catch (SocketException exception)
                {
                    lastSocketException = exception;
                    host.Detach();
                    await server.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    host.Detach();
                    await server.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            throw new InvalidOperationException(
                "Could not allocate an available local X11 display in the range 100-199.",
                lastSocketException);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(cookie);
            throw;
        }
    }

    public X11RemoteAuthorityInstall BuildRemoteAuthorityInstall(int remoteDisplayNumber) =>
        new(
            X11AuthorityFile.BuildInstallCommand(RemoteAuthorityPath),
            X11AuthorityFile.CreateRecord(remoteDisplayNumber, _authorizationCookie));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _host.Detach();
        try
        {
            await _server.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(_authorizationCookie);
        }
    }
}
