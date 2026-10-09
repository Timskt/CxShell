using System;
using System.Linq;
using System.Threading.Tasks;
using AtomUI.Desktop.Controls;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CxShell.Models;
using CxShell.ViewModels;

namespace CxShell.Views;

internal sealed class ShellWindowService : IShellWindows
{
    private SessionManagerWindow? _sessionManagerWindow;
    private SettingsCenterWindow? _settingsCenterWindow;
    private SshTunnelCenterWindow? _sshTunnelCenterWindow;
    private RecentConnectionsWindow? _recentConnectionsWindow;
    private UpdateProgressWindow? _updateProgressWindow;

    public async Task<SessionEditOutcome?> EditSessionAsync(SessionInfo session, Action<SessionInfo> onSaved)
    {
        var dialog = new SessionEditDialog
        {
            DataContext = new SessionEditViewModel(session)
        };

        SessionInfo? saved = null;
        dialog.SessionSaved += result =>
        {
            saved = result;
            onSaved(result);
        };

        await ShowAsDialogAsync(dialog);
        return saved is null ? null : new SessionEditOutcome(saved, dialog.ShouldConnect);
    }

    public void ShowSessionManager(SessionTreeViewModel sessionTree, Action? onClosed = null)
    {
        if (Owner is not { } owner)
            return;

        if (_sessionManagerWindow != null)
        {
            _sessionManagerWindow.Activate();
            return;
        }

        var window = new SessionManagerWindow(sessionTree)
        {
            ShowInTaskbar = false
        };
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_sessionManagerWindow, window))
                return;

            _sessionManagerWindow = null;
            onClosed?.Invoke();
        };
        _sessionManagerWindow = window;
        window.Show(owner);
    }

    public void CloseSessionManager()
    {
        var window = _sessionManagerWindow;
        if (window == null)
            return;

        _sessionManagerWindow = null;
        window.Close();
    }

    public void ShowSettingsCenter(SettingsCenterViewModel viewModel, SettingsSection section)
    {
        if (Owner is not { } owner)
            return;

        if (_settingsCenterWindow != null)
        {
            if (_settingsCenterWindow.DataContext is SettingsCenterViewModel existingViewModel)
                existingViewModel.Select(section);
            _settingsCenterWindow.Activate();
            return;
        }

        viewModel.Select(section);
        var window = new SettingsCenterWindow(viewModel);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settingsCenterWindow, window))
                _settingsCenterWindow = null;
        };
        _settingsCenterWindow = window;
        window.Show(owner);
    }

    public void ShowSshTunnelCenter(SshTunnelCenterViewModel viewModel)
    {
        if (Owner is not { } owner)
            return;

        if (_sshTunnelCenterWindow != null)
        {
            _sshTunnelCenterWindow.Activate();
            return;
        }

        var window = new SshTunnelCenterWindow(viewModel);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_sshTunnelCenterWindow, window))
                _sshTunnelCenterWindow = null;
        };
        _sshTunnelCenterWindow = window;
        window.Show(owner);
    }

    public void ShowRecentConnections(RecentConnectionsViewModel viewModel)
    {
        if (Owner is not { } owner)
            return;

        if (_recentConnectionsWindow != null)
        {
            _recentConnectionsWindow.Activate();
            return;
        }

        var window = new RecentConnectionsWindow
        {
            DataContext = viewModel,
            ShowInTaskbar = false
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_recentConnectionsWindow, window))
                _recentConnectionsWindow = null;
        };
        _recentConnectionsWindow = window;
        window.Show(owner);
    }

    public void CloseRecentConnections()
    {
        var window = _recentConnectionsWindow;
        _recentConnectionsWindow = null;
        window?.Close();
    }

    public Task<SshTunnelRule?> ShowSshTunnelRuleDialogAsync(SshTunnelRule? source)
    {
        if (Owner is not { } owner)
            return Task.FromResult<SshTunnelRule?>(null);

        var dialog = new SshTunnelRuleDialogWindow(new SshTunnelRuleDialogViewModel(source));
        return dialog.ShowRuleDialogAsync(owner);
    }

    public async Task ShowConnectionDiagnosticsAsync(ConnectionDiagnosticsViewModel viewModel)
    {
        var dialog = new ConnectionDiagnosticsWindow
        {
            DataContext = viewModel
        };
        await ShowAsDialogAsync(dialog);
    }

    public async Task ShowMessageAsync(string title, string message, ShellMessageKind kind)
    {
        if (DialogOwner is not { } owner)
            return;

        await AtomUiDialogService.ShowMessageAsync(
            owner,
            title,
            message,
            kind switch
            {
                ShellMessageKind.Success => MessageBoxStyle.Success,
                ShellMessageKind.Warning => MessageBoxStyle.Warning,
                ShellMessageKind.Error => MessageBoxStyle.Error,
                _ => MessageBoxStyle.Information
            });
    }

    public async Task<bool> ShowConfirmAsync(string title, string message, string? okText, string? cancelText)
    {
        if (DialogOwner is not { } owner)
            return false;

        return await AtomUiDialogService.ShowConfirmAsync(owner, title, message, okText, cancelText);
    }

    public async Task<ExternalLaunchConfirmation> ShowExternalLaunchConfirmAsync(
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
        string trustText)
    {
        if (DialogOwner is not { } owner)
            return new ExternalLaunchConfirmation(false, false);

        return await AtomUiDialogService.ShowExternalLaunchConfirmAsync(
            owner,
            title,
            sourceLabel,
            origin,
            protocolLabel,
            protocol,
            targetLabel,
            target,
            credentialLabel,
            credentialSupplied,
            credentialSuppliedText,
            credentialNoneText,
            connectText,
            cancelText,
            trustText);
    }

    public async Task ShowAboutAsync(
        string title,
        string appName,
        string versionText,
        string description,
        string builtWith,
        string githubLabel,
        string githubUrl)
    {
        if (DialogOwner is not { } owner)
            return;

        await AtomUiDialogService.ShowAboutAsync(
            owner,
            title,
            appName,
            versionText,
            description,
            builtWith,
            githubLabel,
            githubUrl);
    }

    public void ShowUpdateProgress(UpdateProgressViewModel viewModel, Action cancelRequested)
    {
        CloseUpdateProgress();

        var window = new UpdateProgressWindow
        {
            DataContext = viewModel
        };
        window.CancelRequested += (_, _) => cancelRequested();
        _updateProgressWindow = window;

        if (DialogOwner is { } owner)
            window.Show(owner);
        else
            window.Show();
    }

    public void CloseUpdateProgress()
    {
        var window = _updateProgressWindow;
        if (window == null)
            return;

        try
        {
            window.CloseForCompletion();
        }
        catch
        {
            // Ignore close failures during shutdown or update restart.
        }
        finally
        {
            if (ReferenceEquals(_updateProgressWindow, window))
                _updateProgressWindow = null;
        }
    }

    /// <summary>
    /// A modal belongs to the window the user is actually in, which is not
    /// always the main window when an update or tunnel window has focus.
    /// </summary>
    private static Avalonia.Controls.Window? DialogOwner =>
        Lifetime?.Windows.FirstOrDefault(window => window.IsActive) ?? Lifetime?.MainWindow;

    private static IClassicDesktopStyleApplicationLifetime? Lifetime =>
        Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    private static AtomUI.Desktop.Controls.Window? Owner =>
        Lifetime?.MainWindow as AtomUI.Desktop.Controls.Window;

    private static async Task ShowAsDialogAsync(Avalonia.Controls.Window dialog)
    {
        if (Owner is { } owner)
            await dialog.ShowDialog(owner);
    }
}
