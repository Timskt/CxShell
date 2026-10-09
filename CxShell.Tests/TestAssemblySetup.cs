using System.Runtime.CompilerServices;
using System.IO;
using CxShell.Services;

namespace CxShell.Tests;

/// <summary>
/// Keeps the suite out of the developer's real credential directory: the key that
/// encrypts saved passwords is per-install, so tests need their own.
/// </summary>
internal static class TestAssemblySetup
{
    [ModuleInitializer]
    internal static void RedirectCredentialKeyDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cxshell-tests-credentials");
        Directory.CreateDirectory(directory);
        CredentialKeyProvider.DirectoryOverride = directory;
    }
}
