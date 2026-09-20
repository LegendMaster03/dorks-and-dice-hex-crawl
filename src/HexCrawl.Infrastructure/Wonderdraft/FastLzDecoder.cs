namespace HexCrawl.Infrastructure.Wonderdraft;

internal static class FastLzDecoder
{
    public static byte[] Decompress(ReadOnlySpan<byte> source, int expectedOutputSize)
    {
        if (expectedOutputSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedOutputSize));
        }

        if (source.IsEmpty)
        {
            if (expectedOutputSize == 0) return [];
            throw new InvalidDataException("FastLZ block is empty but decoded data was expected.");
        }

        var level = (source[0] >> 5) + 1;
        if (level is not 1 and not 2)
        {
            throw new InvalidDataException($"Unsupported FastLZ level {level}.");
        }

        var output = new byte[expectedOutputSize];
        var inputIndex = 1;
        var outputLength = 0;
        var control = source[0] & 31;

        while (true)
        {
            if (control >= 32)
            {
                var length = (control >> 5) - 1;
                var offset = (control & 31) << 8;
                var reference = outputLength - offset - 1;
                if (reference < 0)
                {
                    throw new InvalidDataException("FastLZ block contains an invalid backward reference.");
                }

                if (level == 1)
                {
                    if (length == 6)
                    {
                        length += ReadByte(source, ref inputIndex, "FastLZ block ended inside an extended length.");
                    }

                    reference -= ReadByte(source, ref inputIndex, "FastLZ block ended inside a backward offset.");
                    if (reference < 0)
                    {
                        throw new InvalidDataException("FastLZ block contains an invalid backward reference.");
                    }

                    length += 3;
                }
                else
                {
                    if (length == 6)
                    {
                        int extendedLengthCode;
                        do
                        {
                            extendedLengthCode = ReadByte(source, ref inputIndex, "FastLZ block ended inside an extended length.");
                            length += extendedLengthCode;
                        } while (extendedLengthCode == 255);
                    }

                    var offsetCode = ReadByte(source, ref inputIndex, "FastLZ block ended inside a backward offset.");
                    reference -= offsetCode;
                    if (reference < 0)
                    {
                        throw new InvalidDataException("FastLZ block contains an invalid backward reference.");
                    }

                    length += 3;
                    if (offsetCode == 255 && offset == (31 << 8))
                    {
                        var high = ReadByte(source, ref inputIndex, "FastLZ block ended inside a far-distance match.");
                        var low = ReadByte(source, ref inputIndex, "FastLZ block ended inside a far-distance match.");
                        offset = (high << 8) + low;
                        reference = outputLength - offset - 8192;
                        if (reference < 0)
                        {
                            throw new InvalidDataException("FastLZ block contains an invalid far-distance reference.");
                        }
                    }
                }

                if (outputLength + length > output.Length)
                {
                    throw new InvalidDataException("FastLZ block expands beyond its declared decoded size.");
                }

                for (var index = 0; index < length; index++)
                {
                    if ((uint)reference >= (uint)outputLength)
                    {
                        throw new InvalidDataException("FastLZ block contains an invalid match.");
                    }

                    output[outputLength++] = output[reference++];
                }
            }
            else
            {
                var count = control + 1;
                if (inputIndex + count > source.Length || outputLength + count > output.Length)
                {
                    throw new InvalidDataException("FastLZ block contains a truncated or oversized literal.");
                }

                source.Slice(inputIndex, count).CopyTo(output.AsSpan(outputLength, count));
                inputIndex += count;
                outputLength += count;
            }

            var done = level == 1
                ? inputIndex + 2 > source.Length
                : inputIndex >= source.Length;
            if (done) break;

            control = source[inputIndex++];
        }

        if (outputLength != expectedOutputSize)
        {
            throw new InvalidDataException(
                $"FastLZ block decoded to {outputLength} bytes; {expectedOutputSize} bytes were declared.");
        }

        return output;
    }

    private static int ReadByte(ReadOnlySpan<byte> source, ref int index, string error)
    {
        if ((uint)index >= (uint)source.Length) throw new InvalidDataException(error);
        return source[index++];
    }
}
