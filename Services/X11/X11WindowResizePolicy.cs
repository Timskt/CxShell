using Avalonia.Controls;

namespace CxShell.Services.X11;

internal static class X11WindowResizePolicy
{
    public static bool ShouldReportSize(
        WindowResizeReason reason,
        bool stateChanged,
        bool applyingProperties,
        bool minimized)
    {
        return !applyingProperties
            && !minimized
            && (reason == WindowResizeReason.User || stateChanged);
    }
}
