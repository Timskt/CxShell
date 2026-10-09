using System;

namespace CxShell.Services;

/// <summary>
/// Moves a secret off the published legacy key and onto the per-install key.
/// Anything that cannot be upgraded is returned untouched, because losing a
/// recoverable password is worse than leaving it one migration pass behind.
/// </summary>
internal static class LegacySecretUpgrader
{
    internal static string Upgrade(string? value)
    {
        if (!PasswordEncryptionService.IsLegacySecret(value))
            return value ?? string.Empty;

        var plainText = PasswordEncryptionService.Decrypt(value);
        if (string.IsNullOrEmpty(plainText))
            return value!;

        var upgraded = PasswordEncryptionService.Encrypt(plainText);
        return string.IsNullOrEmpty(upgraded) ? value! : upgraded;
    }

    internal static bool DidUpgrade(string? before, string? after)
        => !string.Equals(before ?? string.Empty, after ?? string.Empty, StringComparison.Ordinal);

    /// <summary>Upgrades a secret in place and reports whether it changed.</summary>
    internal static bool UpgradeField(string? value, Action<string> write)
    {
        var upgraded = Upgrade(value);
        if (!DidUpgrade(value, upgraded))
            return false;

        write(upgraded);
        return true;
    }
}
