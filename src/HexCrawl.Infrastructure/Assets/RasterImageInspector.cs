using System.Buffers.Binary;

namespace HexCrawl.Infrastructure.Assets;

public sealed record RasterImageInfo(string MediaType, int Width, int Height);

public static class RasterImageInspector
{
    private const uint PngIhdr = 0x49484452;
    private const uint PngIdat = 0x49444154;
    private const uint PngIend = 0x49454e44;
    private const uint WebPVp8X = 0x56503858;
    private const uint WebPVp8 = 0x56503820;
    private const uint WebPVp8L = 0x5650384c;
    private const uint WebPAnmf = 0x414e4d46;

    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] PngCrcTable = CreatePngCrcTable();

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

    private static async Task<RasterImageInfo> InspectPngAsync(Stream stream, CancellationToken cancellationToken)
    {
        var signature = new byte[8];
        await ReadExactlyAsync(stream, signature, cancellationToken);
        if (!signature.AsSpan().SequenceEqual(PngSignature)) throw new InvalidDataException("Malformed PNG signature.");

        var chunkHeader = new byte[8];
        var chunkBuffer = new byte[64 * 1024];
        var firstChunk = true;
        var sawIdat = false;
        var idatClosed = false;
        RasterImageInfo? image = null;

        while (true)
        {
            await ReadExactlyAsync(stream, chunkHeader, cancellationToken);
            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader.AsSpan(0, 4));
            var chunkType = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader.AsSpan(4, 4));
            var crc = UpdatePngCrc(0xffffffffu, chunkHeader.AsSpan(4, 4));

            if (firstChunk)
            {
                if (chunkType != PngIhdr || chunkLength != 13)
                {
                    throw new InvalidDataException("PNG image must begin with a 13-byte IHDR chunk.");
                }

                var ihdr = new byte[13];
                await ReadExactlyAsync(stream, ihdr, cancellationToken);
                crc = UpdatePngCrc(crc, ihdr);
                await ValidatePngCrcAsync(stream, crc, cancellationToken);
                ValidatePngIhdr(ihdr);
                var width = BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(0, 4));
                var height = BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(4, 4));
                image = Validated("image/png", width, height);
                firstChunk = false;
                continue;
            }

            if (chunkType == PngIhdr) throw new InvalidDataException("PNG contains more than one IHDR chunk.");
            if (chunkType == PngIdat)
            {
                if (idatClosed) throw new InvalidDataException("PNG IDAT chunks must be consecutive.");
                sawIdat = true;
            }
            else if (sawIdat)
            {
                idatClosed = true;
            }

            crc = await ConsumePngChunkDataAsync(stream, chunkLength, crc, chunkBuffer, cancellationToken);
            await ValidatePngCrcAsync(stream, crc, cancellationToken);

            if (chunkType != PngIend) continue;
            if (chunkLength != 0) throw new InvalidDataException("PNG IEND chunk must be empty.");
            if (!sawIdat) throw new InvalidDataException("PNG ended without image data.");
            if (stream.ReadByte() != -1) throw new InvalidDataException("PNG contains trailing data after IEND.");
            return image ?? throw new InvalidDataException("PNG dimensions were not found.");
        }
    }

    private static void ValidatePngIhdr(ReadOnlySpan<byte> ihdr)
    {
        var bitDepth = ihdr[8];
        var colorType = ihdr[9];
        var validBitDepth = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            4 => bitDepth is 8 or 16,
            6 => bitDepth is 8 or 16,
            _ => false
        };
        if (!validBitDepth || ihdr[10] != 0 || ihdr[11] != 0 || ihdr[12] > 1)
        {
            throw new InvalidDataException("PNG IHDR contains unsupported or malformed image parameters.");
        }
    }

    private static async Task<uint> ConsumePngChunkDataAsync(
        Stream stream,
        uint chunkLength,
        uint crc,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var remaining = (long)chunkLength;
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            await ReadExactlyAsync(stream, buffer.AsMemory(0, count), cancellationToken);
            crc = UpdatePngCrc(crc, buffer.AsSpan(0, count));
            remaining -= count;
        }
        return crc;
    }

    private static async Task ValidatePngCrcAsync(Stream stream, uint runningCrc, CancellationToken cancellationToken)
    {
        var bytes = new byte[4];
        await ReadExactlyAsync(stream, bytes, cancellationToken);
        var expected = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        var actual = runningCrc ^ 0xffffffffu;
        if (actual != expected) throw new InvalidDataException("PNG chunk CRC is invalid.");
    }

    private static uint UpdatePngCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = PngCrcTable[(int)((crc ^ value) & 0xff)] ^ (crc >> 8);
        }
        return crc;
    }

    private static uint[] CreatePngCrcTable()
    {
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var value = (uint)index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xedb88320u ^ (value >> 1) : value >> 1;
            }
            table[index] = value;
        }
        return table;
    }

    private static async Task<RasterImageInfo> InspectJpegAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (ReadByteOrThrow(stream, "JPEG ended before SOI was complete.") != 0xff
            || ReadByteOrThrow(stream, "JPEG ended before SOI was complete.") != 0xd8)
        {
            throw new InvalidDataException("Malformed JPEG SOI marker.");
        }

        RasterImageInfo? image = null;
        var sawScan = false;
        int? pendingMarker = null;

        while (true)
        {
            var marker = pendingMarker ?? ReadNextJpegMarker(stream);
            pendingMarker = null;

            if (marker == 0xd9)
            {
                if (image is null || !sawScan) throw new InvalidDataException("JPEG reached EOI before a complete frame and scan were found.");
                if (stream.ReadByte() != -1) throw new InvalidDataException("JPEG contains trailing data after EOI.");
                return image;
            }
            if (marker == 0xd8) throw new InvalidDataException("JPEG contains an unexpected second SOI marker.");
            if (marker is >= 0xd0 and <= 0xd7) throw new InvalidDataException("JPEG restart marker appeared outside entropy-coded scan data.");
            if (marker == 0x01) continue;

            var lengthBytes = new byte[2];
            await ReadExactlyAsync(stream, lengthBytes, cancellationToken);
            var length = BinaryPrimitives.ReadUInt16BigEndian(lengthBytes);
            if (length < 2) throw new InvalidDataException("Malformed JPEG segment length.");
            var payloadLength = length - 2;

            if (IsStartOfFrame(marker))
            {
                if (image is not null) throw new InvalidDataException("JPEG contains more than one frame header.");
                if (payloadLength < 6) throw new InvalidDataException("Malformed JPEG frame header.");
                var frame = new byte[6];
                await ReadExactlyAsync(stream, frame, cancellationToken);
                var components = frame[5];
                var expectedPayloadLength = 6 + (3 * components);
                if (components == 0 || payloadLength != expectedPayloadLength)
                {
                    throw new InvalidDataException("Malformed JPEG frame component table.");
                }
                var height = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(1, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(3, 2));
                image = Validated("image/jpeg", width, height);
                await SkipExactlyAsync(stream, payloadLength - frame.Length, cancellationToken);
                continue;
            }

            if (marker == 0xda)
            {
                if (image is null) throw new InvalidDataException("JPEG scan appeared before a valid frame header.");
                await ValidateJpegScanHeaderAsync(stream, payloadLength, cancellationToken);
                sawScan = true;
                pendingMarker = ReadMarkerAfterEntropyData(stream);
                continue;
            }

            await SkipExactlyAsync(stream, payloadLength, cancellationToken);
        }
    }

    private static int ReadNextJpegMarker(Stream stream)
    {
        if (ReadByteOrThrow(stream, "JPEG ended before EOI.") != 0xff)
        {
            throw new InvalidDataException("JPEG contains data outside a marker or entropy-coded scan.");
        }

        int marker;
        do
        {
            marker = ReadByteOrThrow(stream, "JPEG ended inside a marker.");
        } while (marker == 0xff);

        if (marker == 0x00) throw new InvalidDataException("JPEG byte stuffing appeared outside entropy-coded scan data.");
        return marker;
    }

    private static async Task ValidateJpegScanHeaderAsync(Stream stream, int payloadLength, CancellationToken cancellationToken)
    {
        if (payloadLength < 6) throw new InvalidDataException("Malformed JPEG scan header.");
        var componentCount = ReadByteOrThrow(stream, "JPEG ended inside a scan header.");
        var expectedPayloadLength = 4 + (2 * componentCount);
        if (componentCount == 0 || payloadLength != expectedPayloadLength)
        {
            throw new InvalidDataException("Malformed JPEG scan component table.");
        }
        await SkipExactlyAsync(stream, payloadLength - 1, cancellationToken);
    }

    private static int ReadMarkerAfterEntropyData(Stream stream)
    {
        while (true)
        {
            var value = ReadByteOrThrow(stream, "JPEG ended inside entropy-coded scan data before EOI.");
            if (value != 0xff) continue;

            int marker;
            do
            {
                marker = ReadByteOrThrow(stream, "JPEG ended inside entropy-coded scan data before EOI.");
            } while (marker == 0xff);

            if (marker == 0x00 || marker == 0x01 || marker is >= 0xd0 and <= 0xd7) continue;
            return marker;
        }
    }

    private static async Task<RasterImageInfo> InspectWebPAsync(Stream stream, CancellationToken cancellationToken)
    {
        var riffHeader = new byte[12];
        await ReadExactlyAsync(stream, riffHeader, cancellationToken);
        if (!riffHeader.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !riffHeader.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            throw new InvalidDataException("Malformed WebP RIFF header.");
        }

        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(riffHeader.AsSpan(4, 4));
        if (riffSize < 4) throw new InvalidDataException("WebP RIFF container length is invalid.");
        var containerLength = 8L + riffSize;
        var consumed = 12L;
        var firstChunk = true;
        var sawVp8x = false;
        byte vp8xFlags = 0;
        var hasImagePayload = false;
        RasterImageInfo? image = null;
        var chunkHeader = new byte[8];

        while (consumed < containerLength)
        {
            if (containerLength - consumed < chunkHeader.Length)
            {
                throw new InvalidDataException("WebP RIFF container ends inside a chunk header.");
            }

            await ReadExactlyAsync(stream, chunkHeader, cancellationToken);
            consumed += chunkHeader.Length;
            var chunkType = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader.AsSpan(0, 4));
            var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.AsSpan(4, 4));
            var paddedLength = (long)chunkLength + (chunkLength & 1u);
            if (paddedLength > containerLength - consumed)
            {
                throw new InvalidDataException("WebP chunk extends beyond the declared RIFF container.");
            }

            if (firstChunk && chunkType is not WebPVp8X and not WebPVp8 and not WebPVp8L)
            {
                throw new InvalidDataException("WebP image uses an unsupported or malformed primary chunk.");
            }

            if (chunkType == WebPVp8X)
            {
                if (!firstChunk || sawVp8x || chunkLength != 10)
                {
                    throw new InvalidDataException("Malformed VP8X header.");
                }
                var data = new byte[10];
                await ReadExactlyAsync(stream, data, cancellationToken);
                if ((data[0] & 0xc1) != 0 || data[1] != 0 || data[2] != 0 || data[3] != 0)
                {
                    throw new InvalidDataException("VP8X contains non-zero reserved bits.");
                }
                vp8xFlags = data[0];
                sawVp8x = true;
                var width = 1 + ReadUInt24LittleEndian(data.AsSpan(4, 3));
                var height = 1 + ReadUInt24LittleEndian(data.AsSpan(7, 3));
                image = Validated("image/webp", width, height);
            }
            else if (chunkType == WebPVp8)
            {
                var dimensions = await ReadVp8ChunkAsync(stream, chunkLength, cancellationToken);
                if (!sawVp8x) image = Validated("image/webp", dimensions.Width, dimensions.Height);
                if (hasImagePayload) throw new InvalidDataException("WebP contains more than one primary image payload.");
                hasImagePayload = true;
            }
            else if (chunkType == WebPVp8L)
            {
                var dimensions = await ReadVp8LChunkAsync(stream, chunkLength, cancellationToken);
                if (!sawVp8x) image = Validated("image/webp", dimensions.Width, dimensions.Height);
                if (hasImagePayload) throw new InvalidDataException("WebP contains more than one primary image payload.");
                hasImagePayload = true;
            }
            else if (chunkType == WebPAnmf)
            {
                if (!sawVp8x || (vp8xFlags & 0x02) == 0)
                {
                    throw new InvalidDataException("WebP animation frame appeared without the VP8X animation flag.");
                }
                if (await ValidateWebPAnimationFrameAsync(stream, chunkLength, cancellationToken)) hasImagePayload = true;
            }
            else
            {
                await SkipExactlyAsync(stream, chunkLength, cancellationToken);
            }

            consumed += chunkLength;
            if ((chunkLength & 1u) != 0)
            {
                _ = ReadByteOrThrow(stream, "WebP ended before a required chunk padding byte.");
                consumed++;
            }
            firstChunk = false;
        }

        if (consumed != containerLength) throw new InvalidDataException("WebP RIFF container length is inconsistent.");
        if (image is null || !hasImagePayload) throw new InvalidDataException("WebP container does not contain a complete image payload.");
        if (stream.ReadByte() != -1) throw new InvalidDataException("WebP contains trailing data beyond the declared RIFF container.");
        return image;
    }

    private static async Task<(int Width, int Height)> ReadVp8ChunkAsync(Stream stream, uint chunkLength, CancellationToken cancellationToken)
    {
        if (chunkLength < 10) throw new InvalidDataException("Malformed VP8 image chunk.");
        var data = new byte[10];
        await ReadExactlyAsync(stream, data, cancellationToken);
        if (!data.AsSpan(3, 3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }))
        {
            throw new InvalidDataException("Malformed VP8 frame signature.");
        }
        var width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6, 2)) & 0x3fff;
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8, 2)) & 0x3fff;
        _ = Validated("image/webp", width, height);
        await SkipExactlyAsync(stream, (long)chunkLength - data.Length, cancellationToken);
        return (width, height);
    }

    private static async Task<(int Width, int Height)> ReadVp8LChunkAsync(Stream stream, uint chunkLength, CancellationToken cancellationToken)
    {
        if (chunkLength < 5) throw new InvalidDataException("Malformed VP8L image chunk.");
        var data = new byte[5];
        await ReadExactlyAsync(stream, data, cancellationToken);
        if (data[0] != 0x2f) throw new InvalidDataException("Malformed VP8L signature.");
        var width = 1 + data[1] + ((data[2] & 0x3f) << 8);
        var height = 1 + (data[2] >> 6) + (data[3] << 2) + ((data[4] & 0x0f) << 10);
        _ = Validated("image/webp", width, height);
        await SkipExactlyAsync(stream, (long)chunkLength - data.Length, cancellationToken);
        return (width, height);
    }

    private static async Task<bool> ValidateWebPAnimationFrameAsync(Stream stream, uint chunkLength, CancellationToken cancellationToken)
    {
        if (chunkLength < 24) throw new InvalidDataException("Malformed WebP animation frame.");
        var frameHeader = new byte[16];
        await ReadExactlyAsync(stream, frameHeader, cancellationToken);
        var remaining = (long)chunkLength - frameHeader.Length;
        var hasImagePayload = false;
        var subchunkHeader = new byte[8];

        while (remaining > 0)
        {
            if (remaining < subchunkHeader.Length) throw new InvalidDataException("WebP animation frame ends inside a subchunk header.");
            await ReadExactlyAsync(stream, subchunkHeader, cancellationToken);
            remaining -= subchunkHeader.Length;
            var subchunkType = BinaryPrimitives.ReadUInt32BigEndian(subchunkHeader.AsSpan(0, 4));
            var subchunkLength = BinaryPrimitives.ReadUInt32LittleEndian(subchunkHeader.AsSpan(4, 4));
            var paddedLength = (long)subchunkLength + (subchunkLength & 1u);
            if (paddedLength > remaining) throw new InvalidDataException("WebP animation subchunk extends beyond its frame.");

            if (subchunkType == WebPVp8)
            {
                _ = await ReadVp8ChunkAsync(stream, subchunkLength, cancellationToken);
                hasImagePayload = true;
            }
            else if (subchunkType == WebPVp8L)
            {
                _ = await ReadVp8LChunkAsync(stream, subchunkLength, cancellationToken);
                hasImagePayload = true;
            }
            else
            {
                await SkipExactlyAsync(stream, subchunkLength, cancellationToken);
            }
            remaining -= subchunkLength;
            if ((subchunkLength & 1u) != 0)
            {
                _ = ReadByteOrThrow(stream, "WebP animation frame ended before subchunk padding.");
                remaining--;
            }
        }

        if (!hasImagePayload) throw new InvalidDataException("WebP animation frame does not contain image data.");
        return true;
    }

    private static RasterImageInfo Validated(string mediaType, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new InvalidDataException("Raster dimensions must be positive.");
        return new RasterImageInfo(mediaType, width, height);
    }

    private static bool IsStartOfFrame(int marker) =>
        marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf;

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes) => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);

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
