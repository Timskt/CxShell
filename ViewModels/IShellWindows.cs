using System;
using System.Threading.Tasks;
using CxShell.Models;

namespace CxShell.ViewModels;

/// <summary>
/// Windows and message boxes the shell opens on user intent. Exists so view
/// models stop constructing views; the implementation owns single-instance
/// tracking and resolves the window a dialog should attach to.
/// </summary>
internal interface IShellWindows
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

    void ShowUpdateProgress(UpdateProgressViewModel viewModel, Action cancelRequested);

    void CloseUpdateProgress();

    Task ShowMessageAsync(string title, string message, ShellMessageKind kind = ShellMessageKind.Information);

    Task<bool> ShowConfirmAsync(string title, string message, string? okText = null, string? cancelText = null);

    Task<ExternalLaunchConfirmation> ShowExternalLaunchConfirmAsync(
        string title,
        string sourceLabel,
        string origin,
        string protocolLabel,
        string protocol,
        string targetLabel,
        string target,
        string credentialLabel,
        bool credentialSupplied,
        string credentialSuppliedText,
        string credentialNoneText,
        string connectText,
        string cancelText,
        string trustText);

    Task ShowAboutAsync(
        string title,
        string appName,
        string versionText,
        string description,
        string builtWith,
        string githubLabel,
        string githubUrl);
}

internal enum ShellMessageKind
{
    Information,
    Success,
    Warning,
    Error
}

internal readonly record struct ExternalLaunchConfirmation(bool Confirmed, bool TrustTarget);

/// <summary>
/// The session a dialog ended on, and whether the user asked to connect to it.
/// </summary>
internal sealed record SessionEditOutcome(SessionInfo Session, bool ShouldConnect);
