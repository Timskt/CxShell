using System;
using System.IO;

namespace CxShell.Services;

/// <summary>
/// File-backed diagnostics for failures the UI deliberately survives. Many
/// catch sites swallow exceptions to keep sessions running; without this they
/// are also invisible. Writes next to the session logs.
/// </summary>
public static class AppLog
{
    private const long MaxFileBytes = 512 * 1024;

    private static readonly object Gate = new();
    private static string? _lastMessage;
    private static int _repeats;

    public static void Warn(string context, Exception? exception = null)
        => Write("WARN", context, exception);

    public static void Error(string context, Exception? exception = null)
        => Write("ERROR", context, exception);

    private static void Write(string level, string context, Exception? exception)
    {
        var message = exception is null
            ? $"{level} {context}"
            : $"{level} {context} :: {exception.GetType().Name}: {exception.Message}";

        lock (Gate)
        {
            if (message == _lastMessage)
            {
                _repeats++;
                return;
            }

            _lastMessage = message;
            message = _repeats > 0 ? $"{level} {context} (+{_repeats} repeated)" : message;
            _repeats = 0;

            try
            {
                var path = ResolveLogPath();
                if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
                    File.Move(path, path + ".old", true);

                using var writer = File.AppendText(path);
                writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
            }
            catch
            {
                // Diagnostics must never fail the operation being diagnosed.
            }
        }
    }

    private static string ResolveLogPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = AppContext.BaseDirectory;

        var directory = Path.Combine(root, "CxShell", "Logs");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "diagnostics.log");
    }
}
