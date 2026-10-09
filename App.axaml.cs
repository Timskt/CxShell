using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using AtomUI;
using AtomUI.Controls;
using AtomUI.Desktop.Controls;
using AtomUI.Theme;
using AtomUI.Theme.Algorithms;
using AtomUI.Theme.Configuration;
using CxShell.Models;
using CxShell.Services;
using CxShell.ViewModels;
using CxShell.Views;

namespace CxShell;

public partial class App : Application
{
    private TrayIcon? _windowsTrayIcon;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        InstallMacOsApplicationMenu();

        this.UseAtomUI(builder =>
        {
            var initialTheme = LoadInitialTheme();
            builder.WithInitialTheme(IThemeManager.DEFAULT_THEME_ID, initialTheme);
            builder.UseAlibabaSansFont();
            builder.UseDesktopControls();
            builder.UseDesktopColorPicker();
            builder.UseDesktopDataGrid();
        });
    }

    /// <summary>
    /// Avalonia's own FluentTheme resources (used by AvaloniaEdit and LiveMarkdown)
    /// do not read AtomUI's theme manager, so the variant has to mirror it explicitly.
    /// </summary>
    public static void ApplyAvaloniaThemeVariant(bool dark)
    {
        if (Current is { } app)
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private static ThemeConfig LoadInitialTheme()
    {
        var themeMode = ApplicationSettings.DarkThemeMode;

        try
        {
            var legacySettings = new SessionStorageService().Load().Settings;
            themeMode = new ApplicationSettingsStore().Load(legacySettings).ThemeMode;
        }
        catch (Exception ex)
        {
            // Keep startup on the established dark theme if the settings file is unavailable.
            AppLog.Warn("Startup settings load failed; falling back to dark theme", ex);
        }

        var isDark = !string.Equals(
            themeMode,
            ApplicationSettings.LightThemeMode,
            StringComparison.OrdinalIgnoreCase);
        ApplyAvaloniaThemeVariant(isDark);

        var algorithms = isDark
            ? new[] { ThemeAlgorithm.Default, ThemeAlgorithm.Dark }
            : new[] { ThemeAlgorithm.Default };

        return new ThemeConfigBuilder()
            .WithAlgorithms(algorithms)
            .Build();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(desktop.Args ?? Array.Empty<string>());
            InitializeWindowsTrayIcon(desktop);
            desktop.Exit += (_, _) => DisposeWindowsTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeWindowsTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!OperatingSystem.IsWindows() || desktop.MainWindow is not { } mainWindow)
            return;

        var showWindowItem = new NativeMenuItem("显示 CxShell");
        showWindowItem.Click += (_, _) => ShowMainWindow(desktop);

        var exitItem = new NativeMenuItem("退出");
        exitItem.Click += (_, _) => desktop.Shutdown();

        var menu = new NativeMenu();
        menu.Items.Add(showWindowItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        _windowsTrayIcon = new TrayIcon
        {
            Icon = mainWindow.Icon,
            ToolTipText = "CxShell",
            Menu = menu,
            IsVisible = true
        };
        _windowsTrayIcon.Clicked += (_, _) => ShowMainWindow(desktop);

        var trayIcons = new TrayIcons();
        trayIcons.Add(_windowsTrayIcon);
        TrayIcon.SetIcons(this, trayIcons);
    }

    private static void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow is not { } mainWindow)
            return;

        if (mainWindow.WindowState == WindowState.Minimized)
            mainWindow.WindowState = WindowState.Normal;

        mainWindow.Show();
        mainWindow.Activate();
    }

    private void DisposeWindowsTrayIcon()
    {
        _windowsTrayIcon?.Dispose();
        _windowsTrayIcon = null;
        TrayIcon.SetIcons(this, new TrayIcons());
    }

    private void InstallMacOsApplicationMenu()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var aboutItem = new NativeMenuItem
        {
            Header = "About CxShell"
        };
        aboutItem.Click += (_, _) => ShowAboutFromApplicationMenu();

        var appMenu = new NativeMenu();
        appMenu.Items.Add(aboutItem);
        NativeMenu.SetMenu(this, appMenu);
    }

    private void ShowAboutFromApplicationMenu()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.DataContext: MainWindowViewModel vm } &&
            vm.ShowAboutCommand.CanExecute(null))
        {
            vm.ShowAboutCommand.Execute(null);
        }
    }
}
