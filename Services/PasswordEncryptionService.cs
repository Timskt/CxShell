using System;
using System.Security.Cryptography;
using System.Text;

namespace CxShell.Services;

public static class PasswordEncryptionService
{
    /// <summary>Key derived from a literal in this repository; read-only, for data written before the per-install key.</summary>
    private const string LegacyPrefix = "cxaes:";
    private const string ProtectedPrefix = "cxsec:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] LegacyKey = SHA256.HashData(Encoding.UTF8.GetBytes("CxShell.Session.Password.v1"));

    public static string Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        if (!CredentialKeyProvider.TryGetKey(out var key))
            return string.Empty;

        return ProtectedPrefix + Convert.ToBase64String(EncryptPayload(Encoding.UTF8.GetBytes(plainText), key));
    }

    public static string Decrypt(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        if (cipherText.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
        {
            return CredentialKeyProvider.TryGetKey(out var key)
                ? DecryptPayload(cipherText[ProtectedPrefix.Length..], key)
                : string.Empty;
        }

        if (cipherText.StartsWith(LegacyPrefix, StringComparison.Ordinal))
            return DecryptPayload(cipherText[LegacyPrefix.Length..], LegacyKey);

        return cipherText;
    }

    /// <summary>
    /// True for secrets written before the per-install key existed. Saving the
    /// session again rewrites them under the current key.
    /// </summary>
    public static bool IsLegacySecret(string? value)
        => !string.IsNullOrEmpty(value) && value.StartsWith(LegacyPrefix, StringComparison.Ordinal);

    public static string DecryptEncrypted(string? cipherText)
    {
        if (!IsEncryptedPrefix(cipherText))
            return string.Empty;

        return Decrypt(cipherText);
    }

    public static bool IsEncryptedValue(string? cipherText)
    {
        return IsEncryptedPrefix(cipherText) && !string.IsNullOrEmpty(Decrypt(cipherText));
    }

    public static bool HasSavedPassword(string? cipherText)
        => !string.IsNullOrEmpty(Decrypt(cipherText));

    /// <summary>
    /// True for anything carrying a ciphertext prefix, whether or not this machine
    /// can read it. Callers that must not treat undecryptable data as plaintext use
    /// this instead of hardcoding a prefix.
    /// </summary>
    public static bool IsProtectedValue(string? value) => IsEncryptedPrefix(value);

    private static bool IsEncryptedPrefix(string? value)
        => !string.IsNullOrEmpty(value) &&
           (value.StartsWith(ProtectedPrefix, StringComparison.Ordinal) ||
            value.StartsWith(LegacyPrefix, StringComparison.Ordinal));

    private static byte[] EncryptPayload(byte[] plainBytes, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipherText = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherText, tag);

        var payload = new byte[nonce.Length + tag.Length + cipherText.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherText, 0, payload, nonce.Length + tag.Length, cipherText.Length);
        return payload;
    }

    private static string DecryptPayload(string base64Payload, byte[] key)
    {
        try
        {
            var payload = Convert.FromBase64String(base64Payload);
            if (payload.Length < NonceSize + TagSize)
                return string.Empty;

            var nonce = payload[..NonceSize];
            var tag = payload[NonceSize..(NonceSize + TagSize)];
            var encrypted = payload[(NonceSize + TagSize)..];
            var plain = new byte[encrypted.Length];

            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, encrypted, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            // Wrong key or tampered ciphertext: the authentication tag is what tells
            // those apart from valid data, so there is nothing to distinguish here.
            return string.Empty;
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }
}
