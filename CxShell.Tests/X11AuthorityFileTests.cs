using System.Buffers.Binary;
using System.Text;
using CxShell.Services.X11;

namespace CxShell.Tests;

public sealed class X11AuthorityFileTests
{
    [Fact]
    public void CreateRecord_UsesWildcardFamilyAndMitMagicCookie()
    {
        byte[] cookie = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();

        byte[] record = X11AuthorityFile.CreateRecord(12, cookie);
        int offset = 0;

        Assert.Equal(ushort.MaxValue, ReadUInt16(record, ref offset));
        Assert.Empty(ReadField(record, ref offset));
        Assert.Equal("12", Encoding.ASCII.GetString(ReadField(record, ref offset)));
        Assert.Equal("MIT-MAGIC-COOKIE-1", Encoding.ASCII.GetString(ReadField(record, ref offset)));
        Assert.Equal(cookie, ReadField(record, ref offset));
        Assert.Equal(record.Length, offset);
    }

    [Fact]
    public void BuildInstallCommand_RestrictsFileAndDoesNotEmbedAuthorizationData()
    {
        string command = X11AuthorityFile.BuildInstallCommand("/tmp/cxshell-xauthority-test");

        Assert.Contains("umask 077", command, StringComparison.Ordinal);
        Assert.Contains("cat > '/tmp/cxshell-xauthority-test'", command, StringComparison.Ordinal);
        Assert.Contains("chmod 600 '/tmp/cxshell-xauthority-test'", command, StringComparison.Ordinal);
        Assert.DoesNotContain("MIT-MAGIC-COOKIE-1", command, StringComparison.Ordinal);
        Assert.DoesNotContain("\\000", command, StringComparison.Ordinal);
    }

    [Fact]
    public void QuotePosix_EscapesSingleQuotes()
    {
        Assert.Equal("'a'\\''b'", X11AuthorityFile.QuotePosix("a'b"));
    }

    [Fact]
    public void CreateRemotePath_UsesUniqueUnpredictableNamesInPrivateTempNamespace()
    {
        string first = X11AuthorityFile.CreateRemotePath();
        string second = X11AuthorityFile.CreateRemotePath();

        Assert.StartsWith("/tmp/cxshell-xauthority-", first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.Equal(32, Path.GetFileName(first)["cxshell-xauthority-".Length..].Length);
    }

    private static ushort ReadUInt16(byte[] record, ref int offset)
    {
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(record.AsSpan(offset, 2));
        offset += 2;
        return value;
    }

    private static byte[] ReadField(byte[] record, ref int offset)
    {
        int length = ReadUInt16(record, ref offset);
        byte[] value = record.AsSpan(offset, length).ToArray();
        offset += length;
        return value;
    }
}
