using System.Buffers.Binary;
using System.Text;

namespace CxShell.Services.X11;

internal static class X11AuthorityFile
{
    private const ushort FamilyWild = ushort.MaxValue;
    private const string CookieProtocol = "MIT-MAGIC-COOKIE-1";

    public static string CreateRemotePath() => $"/tmp/cxshell-xauthority-{Guid.NewGuid():N}";

    public static byte[] CreateRecord(int displayNumber, ReadOnlySpan<byte> cookie)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(displayNumber);
        if (cookie.IsEmpty)
        {
            throw new ArgumentException("The authorization cookie cannot be empty.", nameof(cookie));
        }

        byte[] display = Encoding.ASCII.GetBytes(displayNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        byte[] protocol = Encoding.ASCII.GetBytes(CookieProtocol);
        byte[] record = new byte[2 + 2 + 2 + display.Length + 2 + protocol.Length + 2 + cookie.Length];
        int offset = 0;

        WriteField(record, ref offset, FamilyWild);
        WriteField(record, ref offset, ReadOnlySpan<byte>.Empty);
        WriteField(record, ref offset, display);
        WriteField(record, ref offset, protocol);
        WriteField(record, ref offset, cookie);
        return record;
    }

    public static string BuildInstallCommand(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string escapedPath = QuotePosix(path);
        return $"umask 077; cat > {escapedPath} && chmod 600 {escapedPath}";
    }

    public static string QuotePosix(string value) => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static void WriteField(byte[] destination, ref int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination.AsSpan(offset, 2), value);
        offset += 2;
    }

    private static void WriteField(byte[] destination, ref int offset, ReadOnlySpan<byte> value)
    {
        if (value.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination.AsSpan(offset, 2), (ushort)value.Length);
        offset += 2;
        value.CopyTo(destination.AsSpan(offset));
        offset += value.Length;
    }
}

internal sealed record X11RemoteAuthorityInstall(string Command, byte[] Payload);
