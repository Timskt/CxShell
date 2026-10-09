using System;
using System.IO;
using System.Security.Cryptography;

namespace CxShell.Services;

/// <summary>
/// Supplies the key that encrypts saved credentials. The previous key was
/// SHA256 of a string literal in this repository, so any copy of the data file
/// could be decrypted offline. The key is now 32 random bytes generated on first
/// use and stored beside the database with owner-only permissions.
/// </summary>
internal static class CredentialKeyProvider
{
    internal const int KeySize = 32;
    internal const string KeyFileName = "credential.key";

    private static readonly object Gate = new();
    private static string? _cachedDirectory;
    private static byte[]? _cachedKey;

    /// <summary>Test seam; matches the override AppLog uses for its log directory.</summary>
    internal static string? DirectoryOverride { get; set; }

    /// <summary>
    /// Key for this installation's storage directory. Returns false when the key
    /// file exists but cannot be read or has the wrong size; callers must then
    /// refuse to encrypt rather than fall back to a throwaway key, because a
    /// password written under that key looks saved but can never be read back.
    /// </summary>
    internal static bool TryGetKey(out byte[] key)
    {
        var directory = ResolveDirectory();

        lock (Gate)
        {
            if (_cachedKey != null && string.Equals(_cachedDirectory, directory, StringComparison.Ordinal))
            {
                key = _cachedKey;
                return true;
            }

            if (!TryGetKeyForDirectory(directory, out key))
            {
                _cachedKey = null;
                _cachedDirectory = null;
                return false;
            }

            _cachedDirectory = directory;
            _cachedKey = key;
            return true;
        }
    }

    /// <summary>
    /// Reads the key beside <paramref name="directory"/>, creating it on first use.
    /// Uncached, so a directory's key can be examined without touching process state.
    /// </summary>
    internal static bool TryGetKeyForDirectory(string directory, out byte[] key)
    {
        var path = Path.Combine(directory, KeyFileName);

        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length == KeySize)
                {
                    key = existing;
                    return true;
                }

                AppLog.Error($"credential key at {path} is {existing.Length} bytes, expected {KeySize}");
                key = Array.Empty<byte>();
                return false;
            }

            Directory.CreateDirectory(directory);
            var created = RandomNumberGenerator.GetBytes(KeySize);
            // CreateNew lets a racing process win the write; the loser reads the
            // winner's key on its next call instead of clobbering it.
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ProtectForOwnerOnly(path);
                stream.Write(created);
            }

            key = created;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("credential key load or create failed", ex);
            key = Array.Empty<byte>();
            return false;
        }
    }

    private static void ProtectForOwnerOnly(string path)
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsBrowser())
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex)
        {
            // A filesystem without permission bits still holds the key; failing the
            // save over the missing mode would lose the user's password instead.
            AppLog.Warn("could not restrict credential key permissions", ex);
        }
    }

    private static string ResolveDirectory()
    {
        if (!string.IsNullOrWhiteSpace(DirectoryOverride))
        {
            Directory.CreateDirectory(DirectoryOverride);
            return DirectoryOverride;
        }

        return SessionStorageService.GetStorageDirectory();
    }
}
