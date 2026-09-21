using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HexCrawl.Infrastructure.Wonderdraft;

public static partial class WonderdraftProjectInspector
{
    private sealed class VariantReader
    {
        private const uint Flag64 = 1u << 16;
        private const int MaxDepth = 100;
        private const int MaxCollectionEntries = 250_000;
        private const int MaxStringBytes = 4 * 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        private readonly GcpfPayloadReader _input;

        public VariantReader(GcpfPayloadReader input, uint payloadLength)
        {
            _input = input;
            Remaining = payloadLength;
        }

        public long Remaining { get; private set; }

        public async Task<WonderdraftValue> ReadValueAsync(int depth, CancellationToken cancellationToken)
        {
            if (depth > MaxDepth)
            {
                throw new InvalidDataException("Wonderdraft Variant nesting is unreasonably deep.");
            }

            var header = await ReadUInt32Async(cancellationToken);
            var type = (byte)(header & 0xff);
            var flags = header & ~0xffu;

            return type switch
            {
                0 => NilValue.Instance,
                1 => new BooleanValue(await ReadUInt32Async(cancellationToken) != 0),
                2 => new IntegerValue(flags.HasFlag(Flag64)
                    ? await ReadInt64Async(cancellationToken)
                    : await ReadInt32Async(cancellationToken)),
                3 => new RealValue(flags.HasFlag(Flag64)
                    ? await ReadDoubleAsync(cancellationToken)
                    : await ReadSingleAsync(cancellationToken)),
                4 => new StringValue(await ReadRawStringAsync(cancellationToken)),
                >= 5 and <= 14 => await ReadFixedValueAsync(type, cancellationToken),
                15 => await ReadNodePathAsync(cancellationToken),
                16 => OpaqueValue.Instance,
                17 => await ReadObjectAsync(flags, depth, cancellationToken),
                18 => await ReadDictionaryAsync(depth, cancellationToken),
                19 => await ReadArrayAsync(depth, cancellationToken),
                20 => await ReadPoolByteArrayAsync(cancellationToken),
                21 => await ReadPackedFixedArrayAsync(4, cancellationToken),
                22 => await ReadPackedFixedArrayAsync(4, cancellationToken),
                23 => await ReadPoolStringArrayAsync(cancellationToken),
                24 => await ReadPackedVectorArrayAsync(2, cancellationToken),
                25 => await ReadPackedVectorArrayAsync(3, cancellationToken),
                26 => await ReadPackedVectorArrayAsync(4, cancellationToken),
                _ => throw new InvalidDataException($"Wonderdraft Variant contains unsupported Godot type {type}.")
            };
        }

        private async Task<WonderdraftValue> ReadFixedValueAsync(byte type, CancellationToken cancellationToken)
        {
            var (kind, components) = type switch
            {
                5 => ("Vector2", 2),
                6 => ("Rect2", 4),
                7 => ("Vector3", 3),
                8 => ("Transform2D", 6),
                9 => ("Plane", 4),
                10 => ("Quat", 4),
                11 => ("AABB", 6),
                12 => ("Basis", 9),
                13 => ("Transform", 12),
                14 => ("Color", 4),
                _ => throw new InvalidOperationException()
            };
            var values = new double[components];
            for (var index = 0; index < components; index++)
            {
                values[index] = await ReadSingleAsync(cancellationToken);
            }
            return new VectorValue(kind, values);
        }

        private async Task<WonderdraftValue> ReadNodePathAsync(CancellationToken cancellationToken)
        {
            var nameCountField = await ReadUInt32Async(cancellationToken);
            if ((nameCountField & 0x8000_0000u) == 0)
            {
                throw new InvalidDataException("Wonderdraft Variant contains an unsupported old-format NodePath.");
            }

            var nameCount = checked((int)(nameCountField & 0x7fff_ffffu));
            var subnameCount = checked((int)await ReadUInt32Async(cancellationToken));
            var flags = await ReadUInt32Async(cancellationToken);
            if ((flags & 2u) != 0) subnameCount = checked(subnameCount + 1);
            ValidateCollectionCount(nameCount);
            ValidateCollectionCount(subnameCount);

            for (var index = 0; index < nameCount + subnameCount; index++)
            {
                _ = await ReadRawStringAsync(cancellationToken);
            }

            return OpaqueValue.Instance;
        }

        private async Task<WonderdraftValue> ReadObjectAsync(
            uint flags,
            int depth,
            CancellationToken cancellationToken)
        {
            if ((flags & Flag64) != 0)
            {
                _ = await ReadInt64Async(cancellationToken);
                return OpaqueValue.Instance;
            }

            var className = await ReadRawStringAsync(cancellationToken);
            if (className.Length == 0) return NilValue.Instance;

            var propertyCount = checked((int)await ReadUInt32Async(cancellationToken));
            ValidateCollectionCount(propertyCount);
            for (var index = 0; index < propertyCount; index++)
            {
                _ = await ReadRawStringAsync(cancellationToken);
                _ = await ReadValueAsync(depth + 1, cancellationToken);
            }

            return OpaqueValue.Instance;
        }

        private async Task<WonderdraftValue> ReadDictionaryAsync(int depth, CancellationToken cancellationToken)
        {
            var count = checked((int)((await ReadUInt32Async(cancellationToken)) & 0x7fff_ffffu));
            ValidateCollectionCount(count);
            var values = new Dictionary<string, WonderdraftValue>(count, StringComparer.Ordinal);
            for (var index = 0; index < count; index++)
            {
                var key = await ReadValueAsync(depth + 1, cancellationToken);
                var value = await ReadValueAsync(depth + 1, cancellationToken);
                if (key is StringValue stringKey) values[stringKey.Value] = value;
            }

            return new DictionaryValue(values);
        }

        private async Task<WonderdraftValue> ReadArrayAsync(int depth, CancellationToken cancellationToken)
        {
            var count = checked((int)((await ReadUInt32Async(cancellationToken)) & 0x7fff_ffffu));
            ValidateCollectionCount(count);
            var values = new List<WonderdraftValue>(count);
            for (var index = 0; index < count; index++)
            {
                values.Add(await ReadValueAsync(depth + 1, cancellationToken));
            }

            return new ArrayValue(values);
        }

        private async Task<WonderdraftValue> ReadPoolByteArrayAsync(CancellationToken cancellationToken)
        {
            var length = await ReadUInt32Async(cancellationToken);
            await SkipAsync(length, cancellationToken);
            await SkipAsync((4 - (length % 4)) % 4, cancellationToken);
            return OpaqueValue.Instance;
        }

        private async Task<WonderdraftValue> ReadPackedFixedArrayAsync(
            int bytesPerElement,
            CancellationToken cancellationToken)
        {
            var count = await ReadUInt32Async(cancellationToken);
            await SkipAsync(checked((long)count * bytesPerElement), cancellationToken);
            return OpaqueValue.Instance;
        }

        private async Task<WonderdraftValue> ReadPoolStringArrayAsync(CancellationToken cancellationToken)
        {
            var count = checked((int)await ReadUInt32Async(cancellationToken));
            ValidateCollectionCount(count);
            for (var index = 0; index < count; index++)
            {
                _ = await ReadRawStringAsync(cancellationToken);
            }

            return OpaqueValue.Instance;
        }

        private async Task<WonderdraftValue> ReadPackedVectorArrayAsync(
            int components,
            CancellationToken cancellationToken)
        {
            var count = checked((int)await ReadUInt32Async(cancellationToken));
            ValidateCollectionCount(count);
            if (components != 2)
            {
                await SkipAsync(checked((long)count * components * 4), cancellationToken);
                return OpaqueValue.Instance;
            }

            var values = new List<IReadOnlyList<double>>(count);
            for (var index = 0; index < count; index++)
            {
                values.Add([
                    await ReadSingleAsync(cancellationToken),
                    await ReadSingleAsync(cancellationToken)
                ]);
            }

            return new VectorArrayValue("PoolVector2Array", values);
        }

        private async Task<string> ReadRawStringAsync(CancellationToken cancellationToken)
        {
            var length = await ReadUInt32Async(cancellationToken);
            if (length > MaxStringBytes)
            {
                throw new InvalidDataException($"Wonderdraft Variant string exceeds the {MaxStringBytes} byte safety limit.");
            }

            var bytes = new byte[(int)length];
            await ReadExactlyAsync(bytes, cancellationToken);
            await SkipAsync((4 - (length % 4)) % 4, cancellationToken);
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("Wonderdraft Variant contains invalid UTF-8.", exception);
            }
        }

        private async Task<uint> ReadUInt32Async(CancellationToken cancellationToken)
        {
            var bytes = new byte[4];
            await ReadExactlyAsync(bytes, cancellationToken);
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        }

        private async Task<int> ReadInt32Async(CancellationToken cancellationToken)
        {
            var bytes = new byte[4];
            await ReadExactlyAsync(bytes, cancellationToken);
            return BinaryPrimitives.ReadInt32LittleEndian(bytes);
        }

        private async Task<long> ReadInt64Async(CancellationToken cancellationToken)
        {
            var bytes = new byte[8];
            await ReadExactlyAsync(bytes, cancellationToken);
            return BinaryPrimitives.ReadInt64LittleEndian(bytes);
        }

        private async Task<float> ReadSingleAsync(CancellationToken cancellationToken)
        {
            var bytes = new byte[4];
            await ReadExactlyAsync(bytes, cancellationToken);
            return BinaryPrimitives.ReadSingleLittleEndian(bytes);
        }

        private async Task<double> ReadDoubleAsync(CancellationToken cancellationToken)
        {
            var bytes = new byte[8];
            await ReadExactlyAsync(bytes, cancellationToken);
            return BinaryPrimitives.ReadDoubleLittleEndian(bytes);
        }

        private async Task ReadExactlyAsync(Memory<byte> destination, CancellationToken cancellationToken)
        {
            if (destination.Length > Remaining)
            {
                throw new InvalidDataException("Wonderdraft Variant ended unexpectedly.");
            }

            await _input.ReadExactlyAsync(destination, cancellationToken);
            Remaining -= destination.Length;
        }

        private async Task SkipAsync(long count, CancellationToken cancellationToken)
        {
            if (count < 0 || count > Remaining)
            {
                throw new InvalidDataException("Wonderdraft Variant ended unexpectedly.");
            }

            await _input.SkipAsync(count, cancellationToken);
            Remaining -= count;
        }

        private static void ValidateCollectionCount(int count)
        {
            if (count < 0 || count > MaxCollectionEntries)
            {
                throw new InvalidDataException(
                    $"Wonderdraft Variant collection exceeds the {MaxCollectionEntries} entry safety limit.");
            }
        }
    }


}
