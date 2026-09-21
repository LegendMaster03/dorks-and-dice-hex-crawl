using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HexCrawl.Infrastructure.Wonderdraft;

public static partial class WonderdraftProjectInspector
{
    private sealed class GcpfPayloadReader
    {
        private static readonly byte[] Magic = "GCPF"u8.ToArray();
        private const uint MaxBlockSize = 16 * 1024 * 1024;
        private const int MaxBlockCount = 1_000_000;

        private readonly Stream _source;
        private readonly uint _blockSize;
        private readonly uint[] _packedSizes;
        private byte[] _currentBlock = [];
        private int _currentOffset;
        private int _blockIndex;
        private bool _trailerValidated;

        private GcpfPayloadReader(Stream source, uint blockSize, uint rawSize, uint[] packedSizes)
        {
            _source = source;
            _blockSize = blockSize;
            RawSize = rawSize;
            _packedSizes = packedSizes;
        }

        public long RawSize { get; }
        public long Position { get; private set; }

        public static async Task<GcpfPayloadReader> OpenAsync(
            Stream source,
            long maxDecodedBytes,
            CancellationToken cancellationToken)
        {
            var header = new byte[16];
            await ReadSourceExactlyAsync(source, header, cancellationToken);
            if (!header.AsSpan(0, 4).SequenceEqual(Magic))
            {
                throw new InvalidDataException("Input is not a Wonderdraft GCPF container.");
            }

            var mode = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
            var blockSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4));
            var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4));
            if (mode != 0)
            {
                throw new InvalidDataException($"Wonderdraft GCPF mode {mode} is unsupported.");
            }
            if (blockSize == 0 || blockSize > MaxBlockSize)
            {
                throw new InvalidDataException("Wonderdraft GCPF block size is invalid or exceeds the safety limit.");
            }
            if (rawSize > maxDecodedBytes)
            {
                throw new InvalidDataException(
                    $"Wonderdraft project expands to {rawSize} bytes, exceeding the {maxDecodedBytes} byte decoded-data safety limit.");
            }

            var blockCountLong = ((long)rawSize / blockSize) + 1;
            if (blockCountLong > MaxBlockCount)
            {
                throw new InvalidDataException("Wonderdraft GCPF contains an unreasonable number of blocks.");
            }

            var blockCount = checked((int)blockCountLong);
            var packedSizes = new uint[blockCount];
            var sizeBuffer = new byte[4];
            for (var index = 0; index < blockCount; index++)
            {
                await ReadSourceExactlyAsync(source, sizeBuffer, cancellationToken);
                var packedSize = BinaryPrimitives.ReadUInt32LittleEndian(sizeBuffer);
                var expected = ExpectedBlockLength(rawSize, blockSize, blockCount, index);
                var maximumPackedSize = expected == 0
                    ? 0L
                    : expected + ((expected + 31L) / 32L) + 64L;
                if (packedSize > maximumPackedSize || packedSize > int.MaxValue)
                {
                    throw new InvalidDataException("Wonderdraft GCPF block has an implausible compressed size.");
                }

                packedSizes[index] = packedSize;
            }

            return new GcpfPayloadReader(source, blockSize, rawSize, packedSizes);
        }

        public async Task<uint> ReadUInt32Async(CancellationToken cancellationToken)
        {
            var bytes = new byte[4];
            await ReadExactlyAsync(bytes, cancellationToken);
            return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        }

        public async Task ReadExactlyAsync(Memory<byte> destination, CancellationToken cancellationToken)
        {
            while (!destination.IsEmpty)
            {
                if (_currentOffset >= _currentBlock.Length)
                {
                    if (!await LoadNextBlockAsync(cancellationToken))
                    {
                        throw new InvalidDataException("Wonderdraft GCPF payload ended unexpectedly.");
                    }

                    if (_currentBlock.Length == 0) continue;
                }

                var count = Math.Min(destination.Length, _currentBlock.Length - _currentOffset);
                _currentBlock.AsMemory(_currentOffset, count).CopyTo(destination[..count]);
                _currentOffset += count;
                Position += count;
                if (Position > RawSize)
                {
                    throw new InvalidDataException("Wonderdraft GCPF expands beyond its declared size.");
                }

                destination = destination[count..];
            }
        }

        public async Task SkipAsync(long count, CancellationToken cancellationToken)
        {
            if (count < 0 || Position + count > RawSize)
            {
                throw new InvalidDataException("Wonderdraft GCPF payload ended unexpectedly.");
            }

            var buffer = new byte[16 * 1024];
            while (count > 0)
            {
                var take = (int)Math.Min(buffer.Length, count);
                await ReadExactlyAsync(buffer.AsMemory(0, take), cancellationToken);
                count -= take;
            }
        }

        public async Task EnsureCompleteAsync(CancellationToken cancellationToken)
        {
            if (Position != RawSize)
            {
                throw new InvalidDataException(
                    $"Wonderdraft Variant consumed {Position} decoded bytes; the GCPF container declares {RawSize}.");
            }

            if (_currentOffset != _currentBlock.Length)
            {
                throw new InvalidDataException("Wonderdraft GCPF decoder stopped inside a block.");
            }

            while (_blockIndex < _packedSizes.Length)
            {
                if (!await LoadNextBlockAsync(cancellationToken)) break;
                if (_currentBlock.Length != 0)
                {
                    throw new InvalidDataException("Wonderdraft GCPF contains decoded data after its declared payload length.");
                }
            }

            if (_trailerValidated) return;
            var trailer = new byte[4];
            await ReadSourceExactlyAsync(_source, trailer, cancellationToken);
            if (!trailer.AsSpan().SequenceEqual(Magic))
            {
                throw new InvalidDataException("Wonderdraft GCPF trailer magic is missing.");
            }

            var extra = new byte[1];
            if (await _source.ReadAsync(extra, cancellationToken) != 0)
            {
                throw new InvalidDataException("Wonderdraft GCPF contains trailing bytes after its trailer.");
            }

            _trailerValidated = true;
        }

        private async Task<bool> LoadNextBlockAsync(CancellationToken cancellationToken)
        {
            if (_blockIndex >= _packedSizes.Length) return false;

            var index = _blockIndex++;
            var packedSize = checked((int)_packedSizes[index]);
            var packed = new byte[packedSize];
            await ReadSourceExactlyAsync(_source, packed, cancellationToken);
            var expected = checked((int)ExpectedBlockLength(
                checked((uint)RawSize),
                _blockSize,
                _packedSizes.Length,
                index));
            _currentBlock = FastLzDecoder.Decompress(packed, expected);
            _currentOffset = 0;
            return true;
        }

        private static long ExpectedBlockLength(uint rawSize, uint blockSize, int blockCount, int index) =>
            index + 1 == blockCount
                ? (long)rawSize - ((long)blockSize * (blockCount - 1L))
                : blockSize;

        private static async Task ReadSourceExactlyAsync(
            Stream source,
            Memory<byte> destination,
            CancellationToken cancellationToken)
        {
            while (!destination.IsEmpty)
            {
                var read = await source.ReadAsync(destination, cancellationToken);
                if (read == 0) throw new InvalidDataException("Wonderdraft file ended unexpectedly.");
                destination = destination[read..];
            }
        }
    }
}
