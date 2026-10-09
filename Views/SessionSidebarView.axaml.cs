using Avalonia.Controls;
using Avalonia.Input;
using CxShell.ViewModels;

namespace CxShell.Views;

public partial class SessionSidebarView : UserControl
{
    public SessionSidebarView()
    {
        InitializeComponent();
    }

    private void OnSessionNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SessionNodeTree.SelectedItem is not SessionNodeViewModel node ||
            node.Session is not { } session ||
            TopLevel.GetTopLevel(this)?.DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        _ = vm.ConnectSession(session);
        e.Handled = true;
    }
}
