using System.Buffers.Binary;

namespace HexCrawl.Infrastructure.Assets;

public static partial class RasterImageInspector
{
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

    private static bool IsStartOfFrame(int marker) =>
        marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf;


}
