using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CxShell.Models;
using CxShell.ViewModels;

namespace CxShell.Views;

public sealed class ShellWindowService : IShellWindows
{
    private SessionManagerWindow? _sessionManagerWindow;
    private SettingsCenterWindow? _settingsCenterWindow;
    private SshTunnelCenterWindow? _sshTunnelCenterWindow;
    private RecentConnectionsWindow? _recentConnectionsWindow;

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

    private static AtomUI.Desktop.Controls.Window? Owner =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?
            .MainWindow as AtomUI.Desktop.Controls.Window;

    private static async Task ShowAsDialogAsync(Avalonia.Controls.Window dialog)
    {
        if (Owner is { } owner)
            await dialog.ShowDialog(owner);
    }
}
