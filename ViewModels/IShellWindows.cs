using System;
using System.Threading.Tasks;
using CxShell.Models;

namespace CxShell.ViewModels;

/// <summary>
/// Windows the shell opens on user intent. Exists so view models stop
/// constructing views; the implementation owns single-instance tracking and
/// resolves its own owner window.
/// </summary>
public interface IShellWindows
{
    Task<SessionEditOutcome?> EditSessionAsync(SessionInfo session, Action<SessionInfo> onSaved);

    void ShowSessionManager(SessionTreeViewModel sessionTree, Action? onClosed = null);

    void CloseSessionManager();

    void ShowSettingsCenter(SettingsCenterViewModel viewModel, SettingsSection section);

    void ShowSshTunnelCenter(SshTunnelCenterViewModel viewModel);

    void ShowRecentConnections(RecentConnectionsViewModel viewModel);

    void CloseRecentConnections();

    Task<SshTunnelRule?> ShowSshTunnelRuleDialogAsync(SshTunnelRule? source);

    Task ShowConnectionDiagnosticsAsync(ConnectionDiagnosticsViewModel viewModel);
}

/// <summary>
/// The session a dialog ended on, and whether the user asked to connect to it.
/// </summary>
public sealed record SessionEditOutcome(SessionInfo Session, bool ShouldConnect);
