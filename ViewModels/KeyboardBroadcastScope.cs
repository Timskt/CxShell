namespace CxShell.ViewModels;

/// <summary>
/// Which tabs accept broadcast keystrokes. Kept apart from MainWindowViewModel so
/// the difference between the two global scopes is testable without a window.
/// </summary>
internal static class KeyboardBroadcastScope
{
    /// <summary>
    /// Every open terminal that has not opted out, connected or not. Used by
    /// AllSessions, where a tab mid-reconnect is still a target once it returns.
    /// </summary>
    internal static bool IsOpenReceiver(TerminalTabViewModel tab)
        => tab.IsTerminalSession && tab.IsKeyboardBroadcastEnabled;

    /// <summary>An open receiver with a live connection right now.</summary>
    internal static bool IsConnectedReceiver(TerminalTabViewModel tab)
        => IsOpenReceiver(tab) && tab.Terminal.IsConnected;
}
