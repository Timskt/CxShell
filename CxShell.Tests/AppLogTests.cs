using System;
using System.IO;
using CxShell.Services;

namespace CxShell.Tests;

public sealed class AppLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "cxshell-applog-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Warn_WritesContextAndExceptionType()
    {
        var context = "disk check failed " + Guid.NewGuid().ToString("N");
        AppLog.Warn(context, new InvalidOperationException("no room"));

        var line = Assert.Single(File.ReadAllLines(AppLogPath()).Where(text => text.Contains(context)));
        Assert.Contains("WARN", line);
        Assert.Contains("InvalidOperationException: no room", line);
    }

    [Fact]
    public void RepeatedIdenticalMessages_AreCollapsed()
    {
        var context = "flapping tunnel " + Guid.NewGuid().ToString("N");
        AppLog.Warn(context);
        AppLog.Warn(context);
        AppLog.Warn(context);
        AppLog.Warn("flush marker " + Guid.NewGuid().ToString("N"));

        var lines = File.ReadAllLines(AppLogPath());
        // One line for the message itself, plus a summary of what was dropped.
        Assert.Contains(lines, text => text.Contains(context));
        Assert.Contains(lines, text => text.Contains("repeated 2 more time(s)"));
        Assert.Equal(1, lines.Count(text => text.Contains(context)));
    }

    private string AppLogPath() => Path.Combine(_directory, "diagnostics.log");

    public AppLogTests() => AppLog.DirectoryOverride = _directory;

    public void Dispose()
    {
        AppLog.DirectoryOverride = null;
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }
}
