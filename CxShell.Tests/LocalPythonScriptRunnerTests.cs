using System.Collections.Concurrent;
using CxShell.Models;
using CxShell.Services;

namespace CxShell.Tests;

public sealed class LocalPythonScriptRunnerTests
{
    [Fact]
    public void ExistingLoginScriptBehaviorRemainsRemoteByDefault()
    {
        Assert.Equal(LoginScriptExecutionTarget.Remote, new SessionInfo().LoginScriptExecutionTarget);
    }

    [Fact]
    public void ParseArgumentsPreservesQuotedValuesAndWindowsPaths()
    {
        var arguments = LocalPythonScriptRunner.ParseArguments("\"two words\" C:\\Temp\\scripts\\setup.py");

        Assert.Equal(["two words", "C:\\Temp\\scripts\\setup.py"], arguments);
    }

    [Fact]
    public void ParseArgumentsRejectsUnclosedQuotes()
    {
        Assert.Throws<FormatException>(() => LocalPythonScriptRunner.ParseArguments("\"unfinished value"));
    }

    [Fact]
    public void FindPythonCommandPrefersThePackagedRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cxshell-python-test-{Guid.NewGuid():N}");
        var executable = Path.Combine(root, "python-runtime", "cpython", "bin", "python3");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, string.Empty);

        try
        {
            var command = LocalPythonScriptRunner.FindPythonCommand(root, isWindows: false);

            Assert.NotNull(command);
            Assert.Equal(executable, command.ExecutablePath);
            Assert.True(command.IsBundled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsyncCapturesScriptOutputAndExitCode()
    {
        var output = new ConcurrentQueue<(string Line, bool IsError)>();
        var sourcePath = Path.Combine(Path.GetTempPath(), "cxshell-local-login.py");
        var result = await LocalPythonScriptRunner.RunAsync(
            sourcePath,
            "import sys\nprint('stdout:' + sys.argv[1], flush=True)\nprint('stderr-line', file=sys.stderr, flush=True)\n",
            "\"two words\"",
            (line, isError) =>
            {
                output.Enqueue((line, isError));
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(output, item => item == ("stdout:two words", false));
        Assert.Contains(output, item => item == ("stderr-line", true));
    }

    [Fact]
    public async Task RunAsyncTerminatesThePythonProcessWhenCancelled()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var runTask = LocalPythonScriptRunner.RunAsync(
            Path.Combine(Path.GetTempPath(), "cxshell-cancelled-login.py"),
            "import time\nprint('started', flush=True)\ntime.sleep(30)\n",
            string.Empty,
            (line, _) =>
            {
                if (line == "started")
                    started.TrySetResult();
                return Task.CompletedTask;
            },
            cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
    }
}
