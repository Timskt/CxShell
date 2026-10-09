using System;
using System.Collections.Generic;
using System.Linq;
using CxShell.Models;
using CxShell.ViewModels;

namespace CxShell.Tests;

public sealed class KeyboardBroadcastScopeTests
{
    [Fact]
    public void AllSessionsKeepsAReconnectingTabThatConnectedSessionsDrops()
    {
        using var connected = Tab("web-01", connected: true);
        using var reconnecting = Tab("db-01", connected: false);

        Assert.True(KeyboardBroadcastScope.IsOpenReceiver(connected));
        Assert.True(KeyboardBroadcastScope.IsConnectedReceiver(connected));

        Assert.True(KeyboardBroadcastScope.IsOpenReceiver(reconnecting));
        Assert.False(KeyboardBroadcastScope.IsConnectedReceiver(reconnecting));
    }

    [Fact]
    public void PerTabOptOutIsHonouredByEveryScope()
    {
        using var optedOut = Tab("noisy-01", connected: true);
        optedOut.IsKeyboardBroadcastEnabled = false;

        Assert.False(KeyboardBroadcastScope.IsOpenReceiver(optedOut));
        Assert.False(KeyboardBroadcastScope.IsConnectedReceiver(optedOut));
    }

    [Fact]
    public void Scopes_ResolveToDifferentTabs()
    {
        using var connected = Tab("web-01", connected: true);
        using var reconnecting = Tab("db-01", connected: false);
        using var optedOut = Tab("noisy-01", connected: true);
        optedOut.IsKeyboardBroadcastEnabled = false;

        var tabs = new[] { connected, reconnecting, optedOut };

        Assert.Equal(
            new[] { "web-01", "db-01" },
            tabs.Where(KeyboardBroadcastScope.IsOpenReceiver).Select(tab => tab.Session.Name));
        Assert.Equal(
            new[] { "web-01" },
            tabs.Where(KeyboardBroadcastScope.IsConnectedReceiver).Select(tab => tab.Session.Name));
    }

    private static TerminalTabViewModel Tab(string name, bool connected)
    {
        var tab = new TerminalTabViewModel(new SessionInfo
        {
            Id = Guid.NewGuid(),
            Name = name,
            Host = "127.0.0.1",
            Port = 22,
            Username = "test",
            Protocol = SessionProtocol.SSH
        });
        tab.Terminal.IsConnected = connected;
        return tab;
    }
}
