using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CxShell.Models;
using CxShell.Services;
using Renci.SshNet;

namespace CxShell.Tests;

/// <summary>
/// Exercises the SSH-passthrough proxy against a real bastion. Skipped unless
/// CXSHELL_TEST_SSH_HOST/USER/PASSWORD are set, so no credential lives in the
/// repository and CI stays green without a server.
/// </summary>
public sealed class SshPassthroughProxyIntegrationTests
{
    [Fact]
    public async Task SshPassthrough_ReachesTargetThroughTheBastion()
    {
        var host = Environment.GetEnvironmentVariable("CXSHELL_TEST_SSH_HOST");
        var user = Environment.GetEnvironmentVariable("CXSHELL_TEST_SSH_USER");
        var password = Environment.GetEnvironmentVariable("CXSHELL_TEST_SSH_PASSWORD");
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
            return;

        var port = int.TryParse(Environment.GetEnvironmentVariable("CXSHELL_TEST_SSH_PORT"), out var parsed)
            ? parsed
            : 22;
        var proxy = new ProxySettings
        {
            Name = "test bastion",
            Protocol = ProxyProtocol.SshPassthrough,
            Host = host,
            Port = port,
            Username = user,
            Password = PasswordEncryptionService.Encrypt(password),
            AuthMethod = AuthMethod.Password
        };

        // The SSH path is what "connect to a server through a bastion" means in
        // practice; the destination is resolved by the bastion, so 127.0.0.1:22
        // here is the bastion's own sshd seen from inside the tunnel.
        var session = new SessionInfo
        {
            Name = "passthrough target",
            Protocol = SessionProtocol.SSH,
            Host = "127.0.0.1",
            Port = port,
            Username = user,
            AuthMethod = AuthMethod.Password,
            Password = PasswordEncryptionService.Encrypt(password),
            Proxy = proxy,
            ProxyServers = [proxy],
            SshAcceptAndSaveHostKey = true
        };

        var authMethods = SshAgentAuthService.CreateAuthenticationMethods(
            user, password, AuthMethod.Password, string.Empty, string.Empty, useAgent: false, addPasswordForPasswordAuth: true);
        var context = ProxyConnectionFactory.CreateSshConnectionContext(session, authMethods);

        using (var client = new SshClient(context.ConnectionInfo))
        {
            client.Connect();
            using var command = client.CreateCommand("echo passthrough-ok");
            Assert.Equal("passthrough-ok", command.Execute().Trim());
            client.Disconnect();
        }

        context.Dispose();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var tcp = await ProxyConnectionFactory.ConnectTcpAsync(
            "127.0.0.1", port, proxy, cts.Token);

        Assert.True(tcp.Connected);
        var banner = new byte[4];
        await tcp.GetStream().ReadAsync(banner, cts.Token);
        Assert.Equal("SSH-", Encoding.ASCII.GetString(banner));
    }
}
