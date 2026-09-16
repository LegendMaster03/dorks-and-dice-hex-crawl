using System.Buffers.Binary;

namespace HexCrawl.Infrastructure.Assets;

public sealed record RasterImageInfo(string MediaType, int Width, int Height);

public static class RasterImageInspector
{
    public static async Task<RasterImageInfo> InspectAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var prefix = new byte[12];
        await ReadExactlyAsync(stream, prefix, cancellationToken);

        if (prefix.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            var rest = new byte[12];
            await ReadExactlyAsync(stream, rest, cancellationToken);
            if (!rest.AsSpan(0, 4).SequenceEqual("IHDR"u8)) throw new InvalidDataException("PNG image does not begin with an IHDR chunk.");
            var width = BinaryPrimitives.ReadInt32BigEndian(rest.AsSpan(4, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(rest.AsSpan(8, 4));
            return Validated("image/png", width, height);
        }

        if (prefix[0] == 0xff && prefix[1] == 0xd8)
        {
            return await InspectJpegAsync(stream, prefix, cancellationToken);
        }

        if (prefix.AsSpan(0, 4).SequenceEqual("RIFF"u8) && prefix.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return await InspectWebPAsync(stream, cancellationToken);
        }

        throw new InvalidDataException("Only PNG, JPEG, and WebP raster images are supported.");
    }

    private static async Task<RasterImageInfo> InspectJpegAsync(Stream stream, byte[] prefix, CancellationToken cancellationToken)
    {
        using var combined = new PrefixStream(prefix, stream);
        if (combined.ReadByte() != 0xff || combined.ReadByte() != 0xd8) throw new InvalidDataException("Malformed JPEG SOI marker.");
        var scanned = 2L;
        while (scanned < 4 * 1024 * 1024)
        {
            var markerPrefix = combined.ReadByte();
            scanned++;
            if (markerPrefix < 0) break;
            if (markerPrefix != 0xff) continue;
            int marker;
            do
            {
                marker = combined.ReadByte();
                scanned++;
            } while (marker == 0xff);
            if (marker < 0) break;
            if (marker is 0xd8 or 0xd9 || marker is >= 0xd0 and <= 0xd7) continue;

            var lengthBytes = new byte[2];
            await ReadExactlyAsync(combined, lengthBytes, cancellationToken);
            scanned += 2;
            var length = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);
            if (length < 2) throw new InvalidDataException("Malformed JPEG segment length.");
            var payloadLength = length - 2;
            if (IsStartOfFrame(marker))
            {
                if (payloadLength < 5) throw new InvalidDataException("Malformed JPEG frame header.");
                var frame = new byte[5];
                await ReadExactlyAsync(combined, frame, cancellationToken);
                var height = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(1, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(3, 2));
                return Validated("image/jpeg", width, height);
            }

            await SkipExactlyAsync(combined, payloadLength, cancellationToken);
            scanned += payloadLength;
        }
        throw new InvalidDataException("JPEG dimensions could not be found in a valid frame header.");
    }

    private static async Task<RasterImageInfo> InspectWebPAsync(Stream stream, CancellationToken cancellationToken)
    {
        var chunkHeader = new byte[8];
        await ReadExactlyAsync(stream, chunkHeader, cancellationToken);
        var chunk = System.Text.Encoding.ASCII.GetString(chunkHeader, 0, 4);
        var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.AsSpan(4, 4));
        if (chunkLength > 16 * 1024 * 1024) throw new InvalidDataException("WebP header chunk is unreasonably large.");

        if (chunk == "VP8X")
        {
            if (chunkLength < 10) throw new InvalidDataException("Malformed VP8X header.");
            var data = new byte[10];
            await ReadExactlyAsync(stream, data, cancellationToken);
            var width = 1 + ReadUInt24LittleEndian(data.AsSpan(4, 3));
            var height = 1 + ReadUInt24LittleEndian(data.AsSpan(7, 3));
            return Validated("image/webp", width, height);
        }

        if (chunk == "VP8 ")
        {
            if (chunkLength < 10) throw new InvalidDataException("Malformed VP8 header.");
            var data = new byte[10];
            await ReadExactlyAsync(stream, data, cancellationToken);
            if (!data.AsSpan(3, 3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a })) throw new InvalidDataException("Malformed VP8 frame signature.");
            var width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6, 2)) & 0x3fff;
            var height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8, 2)) & 0x3fff;
            return Validated("image/webp", width, height);
        }

        if (chunk == "VP8L")
        {
            if (chunkLength < 5) throw new InvalidDataException("Malformed VP8L header.");
            var data = new byte[5];
            await ReadExactlyAsync(stream, data, cancellationToken);
            if (data[0] != 0x2f) throw new InvalidDataException("Malformed VP8L signature.");
            var width = 1 + data[1] + ((data[2] & 0x3f) << 8);
            var height = 1 + (data[2] >> 6) + (data[3] << 2) + ((data[4] & 0x0f) << 10);
            return Validated("image/webp", width, height);
        }

        throw new InvalidDataException("WebP image uses an unsupported or malformed primary chunk.");
    }

    private static RasterImageInfo Validated(string mediaType, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new InvalidDataException("Raster dimensions must be positive.");
        return new RasterImageInfo(mediaType, width, height);
    }

    private static bool IsStartOfFrame(int marker) =>
        marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf;

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes) => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0) throw new InvalidDataException("Raster file ended before its header was complete.");
            offset += read;
        }
    }

    private static async Task SkipExactlyAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(8192, Math.Max(1, count))];
        var remaining = count;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0) throw new InvalidDataException("Raster file ended inside a JPEG segment.");
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
