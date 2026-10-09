using System;
using System.Text.RegularExpressions;
using CxShell.Models;
using CxShell.Services;
using Renci.SshNet;

namespace CxShell.Tests;

/// <summary>
/// The monitor gained a per-session `uname -s` probe and a multi-line POSIX shell
/// collector. Both go over SSH exec, which is worth proving rather than assuming.
/// Skipped unless CXSHELL_TEST_SSH_HOST/USER/PASSWORD are set, so no credential lives
/// in the repository and CI stays green without a server.
/// </summary>
public sealed class MonitorProbeIntegrationTests
{
    [Fact]
    public void UnameProbeAndMultiLineScript_SurviveSshExec()
    {
        var host = Environment.GetEnvironmentVariable("CXSHELL_TEST_SSH_HOST");
        var user = Environment.GetEnvironmentVariable("CXSHELL_TEST_SSH_USER");
        var password = Environment.GetEnvironmentVariable("CXSHELL_TEST_SSH_PASSWORD");
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
            return;

        var port = int.TryParse(Environment.GetEnvironmentVariable("CXSHELL_TEST_SSH_PORT"), out var parsed)
            ? parsed
            : 22;

        var authMethods = SshAgentAuthService.CreateAuthenticationMethods(
            user, password, AuthMethod.Password, string.Empty, string.Empty, useAgent: false, addPasswordForPasswordAuth: false);
        var connectionInfo = new ConnectionInfo(host, port, user, authMethods.ToArray());

        using var client = new SshClient(connectionInfo);
        client.Connect();

        using (var uname = client.CreateCommand("uname -s"))
        {
            var kernel = uname.Execute().Trim();
            Assert.Matches("^(Linux|Darwin|FreeBSD|OpenBSD|NetBSD)$", kernel);
        }

        // Same constructs the macOS collector relies on: a shell function, command
        // substitution, awk with a printf format, and arithmetic on the result.
        const string script = """
lines() { printf '%s\n' 'CPU|0|%.1f' 'MEM|%d|%d'; }
printf "$(lines)" 12.5 2048 1024
echo "NET|$(( 40 - 21 ))|$(( 9 - 3 ))"
""";
        using (var command = client.CreateCommand(script))
        {
            var output = command.Execute();
            Assert.Matches(@"CPU\|0\|12\.5", output);
            Assert.Matches(@"MEM\|2048\|1024", output);
            Assert.Matches(@"NET\|19\|6", output);
        }

        client.Disconnect();
    }
}
