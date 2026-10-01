using System.Buffers.Binary;
using System.Text;
using CxShell.Services;

namespace CxShell.Tests;

public sealed class X11SetupHandshakeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadAsync_AuthenticatesFragmentedSetupAndReplaysIt(bool bigEndian)
    {
        byte[] cookie = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
        byte[] packet = CreateSetupPacket(cookie, bigEndian);
        byte[] trailingData = [0xFA, 0xFB, 0xFC];
        byte[] inputBytes = [.. packet, .. trailingData];

        await using var input = new FragmentingReadStream(new MemoryStream(inputBytes), maxReadSize: 3);
        X11SetupHandshake handshake = await X11SetupHandshakeReader.ReadAsync(input);

        Assert.True(handshake.MatchesCookie(cookie));

        await using Stream replay = handshake.CreateReplayStream(input);
        byte[] replayed = new byte[inputBytes.Length];
        await replay.ReadExactlyAsync(replayed);

        Assert.Equal(inputBytes, replayed);
    }

    [Fact]
    public async Task ReadAsync_RejectsCookieMismatch()
    {
        byte[] packet = CreateSetupPacket(Enumerable.Repeat((byte)1, 16).ToArray(), bigEndian: true);
        await using var input = new MemoryStream(packet);

        X11SetupHandshake handshake = await X11SetupHandshakeReader.ReadAsync(input);

        Assert.False(handshake.MatchesCookie(Enumerable.Repeat((byte)2, 16).ToArray()));
    }

    [Fact]
    public async Task ReadAsync_RejectsUnsupportedAuthorizationProtocol()
    {
        byte[] packet = CreateSetupPacket(Enumerable.Repeat((byte)1, 16).ToArray(), bigEndian: true, protocol: "OTHER-AUTH");
        await using var input = new MemoryStream(packet);

        X11SetupHandshake handshake = await X11SetupHandshakeReader.ReadAsync(input);

        Assert.False(handshake.MatchesCookie(Enumerable.Repeat((byte)1, 16).ToArray()));
    }

    [Fact]
    public async Task ReadAsync_RejectsInvalidByteOrder()
    {
        byte[] header = new byte[X11SetupHandshakeReader.HeaderLength];
        header[0] = (byte)'?';
        await using var input = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(() => X11SetupHandshakeReader.ReadAsync(input));
    }

    [Fact]
    public async Task ReadAsync_RejectsOversizedAuthorizationFieldFromHeader()
    {
        byte[] header = new byte[X11SetupHandshakeReader.HeaderLength];
        header[0] = (byte)'B';
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6, 2), X11SetupHandshakeReader.MaxAuthorizationFieldLength + 1);
        await using var input = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(() => X11SetupHandshakeReader.ReadAsync(input));
    }

    private static byte[] CreateSetupPacket(byte[] cookie, bool bigEndian, string protocol = "MIT-MAGIC-COOKIE-1")
    {
        byte[] protocolBytes = Encoding.ASCII.GetBytes(protocol);
        int paddedProtocolLength = (protocolBytes.Length + 3) & ~3;
        int paddedCookieLength = (cookie.Length + 3) & ~3;
        byte[] packet = new byte[X11SetupHandshakeReader.HeaderLength + paddedProtocolLength + paddedCookieLength];
        packet[0] = bigEndian ? (byte)'B' : (byte)'l';
        WriteUInt16(packet.AsSpan(2, 2), 11, bigEndian);
        WriteUInt16(packet.AsSpan(4, 2), 0, bigEndian);
        WriteUInt16(packet.AsSpan(6, 2), (ushort)protocolBytes.Length, bigEndian);
        WriteUInt16(packet.AsSpan(8, 2), (ushort)cookie.Length, bigEndian);
        protocolBytes.CopyTo(packet, X11SetupHandshakeReader.HeaderLength);
        cookie.CopyTo(packet, X11SetupHandshakeReader.HeaderLength + paddedProtocolLength);
        return packet;
    }

    private static void WriteUInt16(Span<byte> destination, ushort value, bool bigEndian)
    {
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt16BigEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
        }
    }

    private sealed class FragmentingReadStream(Stream inner, int maxReadSize) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, Math.Min(count, maxReadSize));

        public override int Read(Span<byte> buffer) => inner.Read(buffer[..Math.Min(buffer.Length, maxReadSize)]);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer[..Math.Min(buffer.Length, maxReadSize)], cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
