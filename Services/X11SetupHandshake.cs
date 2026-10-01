using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CxShell.Services;

internal static class X11SetupHandshakeReader
{
    public const int HeaderLength = 12;
    public const int MaxAuthorizationFieldLength = 256;

    public static async Task<X11SetupHandshake> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        byte[] header = new byte[HeaderLength];
        await stream.ReadExactlyAsync(header, cancellationToken);

        bool bigEndian = header[0] switch
        {
            (byte)'B' => true,
            (byte)'l' => false,
            _ => throw new InvalidDataException("The X11 setup byte order marker is invalid.")
        };

        int nameLength = ReadUInt16(header.AsSpan(6, 2), bigEndian);
        int dataLength = ReadUInt16(header.AsSpan(8, 2), bigEndian);
        if (nameLength > MaxAuthorizationFieldLength || dataLength > MaxAuthorizationFieldLength)
        {
            throw new InvalidDataException("An X11 authorization field exceeds the allowed size.");
        }

        int paddedNameLength = Pad4(nameLength);
        int paddedDataLength = Pad4(dataLength);
        byte[] packet = new byte[HeaderLength + paddedNameLength + paddedDataLength];
        header.CopyTo(packet, 0);
        await stream.ReadExactlyAsync(packet.AsMemory(HeaderLength), cancellationToken);

        string protocol = Encoding.ASCII.GetString(packet, HeaderLength, nameLength);
        byte[] authorizationData = packet.AsSpan(HeaderLength + paddedNameLength, dataLength).ToArray();
        return new X11SetupHandshake(packet, protocol, authorizationData);
    }

    private static int ReadUInt16(ReadOnlySpan<byte> value, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(value) : BinaryPrimitives.ReadUInt16LittleEndian(value);

    private static int Pad4(int value) => (value + 3) & ~3;
}

internal sealed class X11SetupHandshake(byte[] packet, string protocol, byte[] authorizationData)
{
    private readonly byte[] _packet = packet;

    public string Protocol { get; } = protocol;

    public byte[] AuthorizationData { get; } = authorizationData;

    public bool MatchesCookie(ReadOnlySpan<byte> expectedCookie) =>
        string.Equals(Protocol, "MIT-MAGIC-COOKIE-1", StringComparison.Ordinal)
        && expectedCookie.Length > 0
        && CryptographicOperations.FixedTimeEquals(AuthorizationData, expectedCookie);

    public Stream CreateReplayStream(Stream inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return new X11SetupReplayStream(_packet, inner);
    }
}

internal sealed class X11SetupReplayStream(byte[] prefix, Stream inner) : Stream
{
    private int _prefixOffset;
    private bool _disposed;

    public override bool CanRead => !_disposed && inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => !_disposed && inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int copied = CopyPrefix(buffer);
        return copied > 0 ? copied : inner.Read(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int copied = CopyPrefix(buffer.Span);
        return copied > 0 ? ValueTask.FromResult(copied) : inner.ReadAsync(buffer, cancellationToken);
    }

    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        inner.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        inner.WriteAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await inner.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    private int CopyPrefix(Span<byte> destination)
    {
        int remaining = prefix.Length - _prefixOffset;
        int count = Math.Min(remaining, destination.Length);
        if (count > 0)
        {
            prefix.AsSpan(_prefixOffset, count).CopyTo(destination);
            _prefixOffset += count;
        }

        return count;
    }
}
