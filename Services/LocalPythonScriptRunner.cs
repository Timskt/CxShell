using System.Diagnostics;
using System.Text;

namespace CxShell.Services;

internal sealed record LocalPythonExecutionResult(int ExitCode, bool IsBundled, string ExecutablePath);

internal sealed record PythonCommand(string ExecutablePath, IReadOnlyList<string> PrefixArguments, bool IsBundled);

internal static class LocalPythonScriptRunner
{
    private const string RuntimeDirectoryName = "python-runtime";

    public static async Task<LocalPythonExecutionResult> RunAsync(
        string originalScriptPath,
        string scriptText,
        string parameters,
        Func<string, bool, Task> onOutput,
        CancellationToken cancellationToken)
    {
        var command = FindPythonCommand(AppContext.BaseDirectory, OperatingSystem.IsWindows())
            ?? throw new InvalidOperationException(
                "The bundled Python runtime is missing and no local Python interpreter was found.");

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "CxShell", "LocalPython");
        Directory.CreateDirectory(temporaryDirectory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporaryDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var temporaryScriptPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.py");

        try
        {
            await File.WriteAllTextAsync(temporaryScriptPath, scriptText, new UTF8Encoding(false), cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = command.ExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(originalScriptPath)) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            var originalDirectory = Path.GetDirectoryName(Path.GetFullPath(originalScriptPath));
            if (!string.IsNullOrWhiteSpace(originalDirectory))
            {
                var existingPythonPath = Environment.GetEnvironmentVariable("PYTHONPATH");
                startInfo.Environment["PYTHONPATH"] = string.IsNullOrWhiteSpace(existingPythonPath)
                    ? originalDirectory
                    : string.Join(Path.PathSeparator, originalDirectory, existingPythonPath);
            }
            foreach (var argument in command.PrefixArguments)
                startInfo.ArgumentList.Add(argument);
            startInfo.ArgumentList.Add(temporaryScriptPath);
            foreach (var argument in ParseArguments(parameters))
                startInfo.ArgumentList.Add(argument);
            startInfo.Environment["PYTHONUTF8"] = "1";
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                throw new InvalidOperationException("Could not start the local Python interpreter.");

            process.StandardInput.Close();
            var stdoutTask = ForwardOutputAsync(process.StandardOutput, false, onOutput);
            var stderrTask = ForwardOutputAsync(process.StandardError, true, onOutput);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                await process.WaitForExitAsync();
                await Task.WhenAll(stdoutTask, stderrTask);
                throw;
            }

            await Task.WhenAll(stdoutTask, stderrTask);
            return new LocalPythonExecutionResult(process.ExitCode, command.IsBundled, command.ExecutablePath);
        }
        finally
        {
            try
            {
                File.Delete(temporaryScriptPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    internal static PythonCommand? FindPythonCommand(string applicationDirectory, bool isWindows)
    {
        var runtimeDirectory = Path.Combine(applicationDirectory, RuntimeDirectoryName);
        if (Directory.Exists(runtimeDirectory))
        {
            var names = isWindows ? new[] { "python.exe" } : new[] { "python3", "python3.12", "python" };
            foreach (var name in names)
            {
                var executable = Directory.EnumerateFiles(runtimeDirectory, name, SearchOption.AllDirectories)
                    .OrderBy(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ? 0 : 1)
                    .FirstOrDefault();
                if (executable != null)
                    return new PythonCommand(executable, Array.Empty<string>(), IsBundled: true);
            }
        }

        var pathCandidates = isWindows
            ? new[] { ("python.exe", Array.Empty<string>()), ("py.exe", new[] { "-3" }) }
            : new[] { ("python3", Array.Empty<string>()), ("python", Array.Empty<string>()) };
        foreach (var (name, prefixArguments) in pathCandidates)
        {
            var path = FindOnPath(name);
            if (path != null)
                return new PythonCommand(path, prefixArguments, IsBundled: false);
        }

        return isWindows
            ? new PythonCommand("python.exe", Array.Empty<string>(), IsBundled: false)
            : new PythonCommand("python3", Array.Empty<string>(), IsBundled: false);
    }

    internal static IReadOnlyList<string> ParseArguments(string? parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
            return Array.Empty<string>();

        var arguments = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';
        var escaping = false;

        for (var index = 0; index < parameters.Length; index++)
        {
            var character = parameters[index];
            if (escaping)
            {
                current.Append(character);
                escaping = false;
                continue;
            }

            if (character == '\\' && quote != '\'' && index + 1 < parameters.Length &&
                (parameters[index + 1] == '"' || parameters[index + 1] == '\\'))
            {
                escaping = true;
                continue;
            }

            if (quote != '\0')
            {
                if (character == quote)
                    quote = '\0';
                else
                    current.Append(character);
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(character);
        }

        if (escaping)
            current.Append('\\');
        if (quote != '\0')
            throw new FormatException("A quoted Python script parameter is not closed.");
        if (current.Length > 0)
            arguments.Add(current.ToString());

        return arguments;
    }

    private static string? FindOnPath(string executableName)
    {
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : new[] { string.Empty };
        var baseNameHasExtension = Path.HasExtension(executableName);

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidateName = baseNameHasExtension || extension.Length == 0
                    ? executableName
                    : executableName + extension.ToLowerInvariant();
                var candidate = Path.Combine(directory.Trim('"'), candidateName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static async Task ForwardOutputAsync(
        StreamReader reader,
        bool isError,
        Func<string, bool, Task> onOutput)
    {
        while (await reader.ReadLineAsync() is { } line)
            await onOutput(line, isError);
    }
}
