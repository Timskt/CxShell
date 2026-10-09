using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace CxShell.Services;

/// <summary>
/// Records the crashes the application cannot recover from, and survives the ones it
/// should not die for. A packaged desktop build has no console, so without this a
/// user's crash leaves no trace anywhere.
/// </summary>
internal static class CrashGuard
{
    /// <summary>Process-wide hooks. Safe to call before Avalonia is initialised.</summary>
    internal static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Error("fatal unhandled exception, application is terminating", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>
    /// The UI-thread hook. Separate from Install() because it needs a dispatcher to
    /// exist, which is only true once Avalonia is up.
    /// </summary>
    internal static void InstallDispatcherGuard()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            AppLog.Error("unhandled dispatcher exception", e.Exception);
            if (IsInputPipelineFailure(e.Exception))
                e.Handled = true;
        };
    }

    /// <summary>
    /// True for failures thrown by the input router or a tooltip service. Those are
    /// worth surviving: the alternative is losing every live SSH session because a
    /// hover effect faulted inside the UI toolkit. Anything deeper is left to crash,
    /// because masking a real bug in a terminal client is worse than closing it.
    /// </summary>
    internal static bool IsInputPipelineFailure(Exception? exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            var site = current.TargetSite?.DeclaringType?.FullName;
            if (site is null)
                continue;

            if (site.StartsWith("Avalonia.Input", StringComparison.Ordinal) ||
                site.EndsWith("ToolTipService", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
