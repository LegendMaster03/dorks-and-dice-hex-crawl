using System.Buffers.Binary;

namespace HexCrawl.Infrastructure.Assets;

public static partial class RasterImageInspector
{
    private const uint WebPVp8X = 0x56503858;
    private const uint WebPVp8 = 0x56503820;
    private const uint WebPVp8L = 0x5650384c;
    private const uint WebPAnmf = 0x414e4d46;

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

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes) => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);


}
