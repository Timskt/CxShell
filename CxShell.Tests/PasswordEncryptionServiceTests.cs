using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CxShell.Services;

namespace CxShell.Tests;

public sealed class PasswordEncryptionServiceTests
{
    [Fact]
    public void Encrypt_RoundTripsUnderThePerInstallKey()
    {
        var secret = "s3cret-" + Guid.NewGuid().ToString("N");

        var encrypted = PasswordEncryptionService.Encrypt(secret);

        Assert.StartsWith("cxsec:", encrypted, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, encrypted, StringComparison.Ordinal);
        Assert.Equal(secret, PasswordEncryptionService.Decrypt(encrypted));
        Assert.True(PasswordEncryptionService.HasSavedPassword(encrypted));
        Assert.False(PasswordEncryptionService.IsLegacySecret(encrypted));
    }

    [Fact]
    public void SamePlainText_EncryptsToDifferentCiphertextEachTime()
    {
        var secret = "nonce-" + Guid.NewGuid().ToString("N");

        Assert.NotEqual(PasswordEncryptionService.Encrypt(secret), PasswordEncryptionService.Encrypt(secret));
    }

    [Fact]
    public void LegacySecret_EncryptedWithThePublishedKey_StillDecrypts()
    {
        var secret = "legacy-" + Guid.NewGuid().ToString("N");
        var legacy = LegacyCipher.Encrypt(secret);

        Assert.StartsWith("cxaes:", legacy, StringComparison.Ordinal);
        Assert.True(PasswordEncryptionService.IsLegacySecret(legacy));
        Assert.Equal(secret, PasswordEncryptionService.Decrypt(legacy));
        Assert.Equal(secret, PasswordEncryptionService.DecryptEncrypted(legacy));
        Assert.True(PasswordEncryptionService.IsEncryptedValue(legacy));
    }

    [Fact]
    public void PlainTextValues_PassThroughUnchanged()
    {
        var value = "raw-" + Guid.NewGuid().ToString("N");

        Assert.Equal(value, PasswordEncryptionService.Decrypt(value));
        Assert.Equal(string.Empty, PasswordEncryptionService.DecryptEncrypted(value));
        Assert.False(PasswordEncryptionService.IsEncryptedValue(value));
        Assert.Equal(string.Empty, PasswordEncryptionService.Encrypt(null));
        Assert.Equal(string.Empty, PasswordEncryptionService.Encrypt(string.Empty));
    }

    [Fact]
    public void Upgrader_MovesLegacyOntoCurrentKeyAndLeavesOthersAlone()
    {
        var secret = "migrate-" + Guid.NewGuid().ToString("N");
        var legacy = LegacyCipher.Encrypt(secret);
        var plain = "not-a-secret-" + Guid.NewGuid().ToString("N");
        var current = PasswordEncryptionService.Encrypt(secret);

        var upgraded = LegacySecretUpgrader.Upgrade(legacy);

        Assert.False(PasswordEncryptionService.IsLegacySecret(upgraded));
        Assert.Equal(secret, PasswordEncryptionService.Decrypt(upgraded));
        Assert.True(LegacySecretUpgrader.DidUpgrade(legacy, upgraded));
        Assert.Equal(plain, LegacySecretUpgrader.Upgrade(plain));
        Assert.Equal(current, LegacySecretUpgrader.Upgrade(current));
    }

    [Fact]
    public void Upgrader_WritesThroughOnlyWhenTheFieldChanged()
    {
        var legacy = LegacyCipher.Encrypt("field-" + Guid.NewGuid().ToString("N"));
        var written = 0;

        Assert.True(LegacySecretUpgrader.UpgradeField(legacy, _ => written++));
        Assert.False(LegacySecretUpgrader.UpgradeField("plain", _ => written++));
        Assert.Equal(1, written);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "cxshell-credential-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    [Fact]
    public void KeyFile_IsGeneratedOnceWithOwnerOnlyPermissions()
    {
        using var temp = new TempDirectory();
        var keyPath = System.IO.Path.Combine(temp.Path, CredentialKeyProvider.KeyFileName);
        Assert.False(File.Exists(keyPath));

        Assert.True(CredentialKeyProvider.TryGetKeyForDirectory(temp.Path, out var first));
        Assert.True(CredentialKeyProvider.TryGetKeyForDirectory(temp.Path, out var second));

        Assert.Equal(CredentialKeyProvider.KeySize, first.Length);
        Assert.Equal(first, second);

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(keyPath);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }
    }

    [Fact]
    public void TwoInstalls_GetDifferentKeysAndCannotReadEachOther()
    {
        using var a = new TempDirectory();
        using var b = new TempDirectory();

        Assert.True(CredentialKeyProvider.TryGetKeyForDirectory(a.Path, out var keyA));
        Assert.True(CredentialKeyProvider.TryGetKeyForDirectory(b.Path, out var keyB));

        Assert.NotEqual(keyA, keyB);

        // The whole point of the change: a data file copied to another machine, or
        // read by someone who only has the repository, yields nothing.
        var payload = EncryptWithKey("portable-secret", keyA);
        Assert.Equal(string.Empty, DecryptWithKey(payload, keyB));
        Assert.Equal("portable-secret", DecryptWithKey(payload, keyA));
        Assert.Equal(string.Empty, DecryptWithKey(payload, LegacyCipher.Key));
    }

    [Fact]
    public void DamagedKeyFile_IsRefusedRatherThanReplaced()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Path);
        var keyPath = System.IO.Path.Combine(temp.Path, CredentialKeyProvider.KeyFileName);
        File.WriteAllText(keyPath, "truncated");

        Assert.False(CredentialKeyProvider.TryGetKeyForDirectory(temp.Path, out var key));
        Assert.Empty(key);
        Assert.Equal("truncated", File.ReadAllText(keyPath));
    }

    private static string EncryptWithKey(string plainText, byte[] key)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[bytes.Length];

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, bytes, cipher, tag);

        var payload = nonce.Concat(tag).Concat(cipher).ToArray();
        return "cxsec:" + Convert.ToBase64String(payload);
    }

    private static string DecryptWithKey(string value, byte[] key)
    {
        var payload = Convert.FromBase64String(value["cxsec:".Length..]);
        var plain = new byte[payload.Length - 28];

        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(payload[..12], payload[28..], payload[12..28], plain);
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(plain);
    }
}
