namespace HexCrawl.Infrastructure.Assets;

public sealed record RasterImageInfo(string MediaType, int Width, int Height);

/// <summary>
/// Validates supported raster formats without fully decoding image pixels. Format-specific
/// validation lives in partial files while dispatch and shared stream mechanics stay here.
/// </summary>
public static partial class RasterImageInspector
{
    public static async Task<RasterImageInfo> InspectAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var prefix = new byte[12];
        await ReadExactlyAsync(stream, prefix, cancellationToken);

        using var combined = new PrefixStream(prefix, stream);
        if (prefix.AsSpan(0, 8).SequenceEqual(PngSignature))
        {
            return await InspectPngAsync(combined, cancellationToken);
        }

        if (prefix[0] == 0xff && prefix[1] == 0xd8)
        {
            return await InspectJpegAsync(combined, cancellationToken);
        }

        if (prefix.AsSpan(0, 4).SequenceEqual("RIFF"u8) && prefix.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return await InspectWebPAsync(combined, cancellationToken);
        }

        throw new InvalidDataException("Only PNG, JPEG, and WebP raster images are supported.");
    }

    private static RasterImageInfo Validated(string mediaType, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new InvalidDataException("Raster dimensions must be positive.");
        return new RasterImageInfo(mediaType, width, height);
    }

    private static int ReadByteOrThrow(Stream stream, string message)
    {
        var value = stream.ReadByte();
        if (value < 0) throw new InvalidDataException(message);
        return value;
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0) throw new InvalidDataException("Raster file ended before its declared structure was complete.");
            offset += read;
        }
    }

    private static async Task SkipExactlyAsync(Stream stream, long count, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var remaining = count;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0) throw new InvalidDataException("Raster file ended before its declared structure was complete.");
            remaining -= read;
        }
    }

    private sealed class PrefixStream(byte[] prefix, Stream remainder) : Stream
    {
        private int _offset;
        public override int Read(byte[] buffer, int offset, int count)
        {
            var copied = 0;
            if (_offset < prefix.Length)
            {
                copied = Math.Min(count, prefix.Length - _offset);
                Array.Copy(prefix, _offset, buffer, offset, copied);
                _offset += copied;
                if (copied == count) return copied;
            }
            return copied + remainder.Read(buffer, offset + copied, count - copied);
        }
        public override int ReadByte() => _offset < prefix.Length ? prefix[_offset++] : remainder.ReadByte();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var copied = 0;
            if (_offset < prefix.Length)
            {
                copied = Math.Min(buffer.Length, prefix.Length - _offset);
                prefix.AsMemory(_offset, copied).CopyTo(buffer);
                _offset += copied;
                if (copied == buffer.Length) return copied;
            }
            return copied + await remainder.ReadAsync(buffer[copied..], cancellationToken);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
