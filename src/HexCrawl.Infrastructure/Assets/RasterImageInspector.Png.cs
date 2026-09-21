using System.Buffers.Binary;

namespace HexCrawl.Infrastructure.Assets;

public static partial class RasterImageInspector
{
    private const uint PngIhdr = 0x49484452;
    private const uint PngIdat = 0x49444154;
    private const uint PngIend = 0x49454e44;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] PngCrcTable = CreatePngCrcTable();

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


}
