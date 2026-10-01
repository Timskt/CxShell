using System.Reflection;
using Renci.SshNet;

namespace CxShell.Tests;

public sealed class SshNetX11ApiSurfaceTests
{
    [Fact]
    public void X11RequestIsNotExposedThroughSupportedSshClientApi()
    {
        Assembly assembly = typeof(SshClient).Assembly;
        Type? channelSession = assembly.GetType("Renci.SshNet.Channels.ChannelSession");
        MethodInfo? requestMethod = channelSession?.GetMethod(
            "SendX11ForwardingRequest",
            BindingFlags.Public | BindingFlags.Instance);
        bool publicClientHasX11EntryPoint = typeof(SshClient)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Any(method => method.Name.Contains("X11", StringComparison.OrdinalIgnoreCase))
            || typeof(SshClient)
                .GetEvents(BindingFlags.Public | BindingFlags.Instance)
                .Any(@event => @event.Name.Contains("X11", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(channelSession);
        Assert.False(channelSession!.IsVisible);
        Assert.NotNull(requestMethod);
        Assert.True(requestMethod!.IsPublic);
        Assert.False(publicClientHasX11EntryPoint);
    }
}
