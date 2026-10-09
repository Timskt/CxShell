using System;
using System.Security.Cryptography;
using System.Text;

namespace CxShell.Tests;

/// <summary>
/// Writes data in the format used before the per-install key existed, whose key was
/// SHA256 of a literal published in this repository. Tests need it to prove that real
/// user files still open and get upgraded.
/// </summary>
internal static class LegacyCipher
{
    internal const string Prefix = "cxaes:";
    internal static readonly byte[] Key = SHA256.HashData(
        Encoding.UTF8.GetBytes("CxShell.Session.Password.v1"));

    internal static string Encrypt(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[bytes.Length];

        using var aes = new AesGcm(Key, 16);
        aes.Encrypt(nonce, bytes, cipher, tag);

        var payload = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);
        return Prefix + Convert.ToBase64String(payload);
    }
}
