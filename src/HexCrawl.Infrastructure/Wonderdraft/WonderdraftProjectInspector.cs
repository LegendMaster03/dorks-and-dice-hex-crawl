using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HexCrawl.Infrastructure.Wonderdraft;

public sealed record WonderdraftProjectSummary(
    int? FormatVersion,
    int PixelWidth,
    int PixelHeight,
    int SymbolCount,
    int LabelCount,
    int PathCount,
    int TerritoryCount,
    bool HasGrid,
    IReadOnlyList<string> IncludedPacks,
    IReadOnlyList<string> IncludedDefaultPacks);

public enum WonderdraftCandidateKind
{
    Label,
    Symbol,
    Path,
    Territory
}

public sealed record WonderdraftPixelPoint(double X, double Y);

public sealed record WonderdraftImportCandidate(
    string Key,
    WonderdraftCandidateKind Kind,
    string DisplayName,
    string? Descriptor,
    WonderdraftPixelPoint? Position,
    IReadOnlyList<WonderdraftPixelPoint> Points,
    string? Problem);

public sealed record WonderdraftProjectDocument(
    WonderdraftProjectSummary Summary,
    IReadOnlyList<WonderdraftImportCandidate> Candidates);

public static class WonderdraftProjectInspector
{
    public const long DefaultMaxDecodedBytes = 256L * 1024 * 1024;

    private const int MaxCandidateRecords = 50_000;
    private const int MaxCandidatePoints = 250_000;
    private static readonly Regex Vector2TextPattern = new(
        @"Vector2\s*\(\s*(?<x>[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)\s*,\s*(?<y>[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?)\s*\)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static async Task<WonderdraftProjectSummary> InspectAsync(
        Stream stream,
        long maxDecodedBytes = DefaultMaxDecodedBytes,
        CancellationToken cancellationToken = default) =>
        (await ReadAsync(stream, maxDecodedBytes, cancellationToken)).Summary;

    public static async Task<WonderdraftProjectDocument> ReadAsync(
        Stream stream,
        long maxDecodedBytes = DefaultMaxDecodedBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead) throw new ArgumentException("Wonderdraft input stream must be readable.", nameof(stream));
        if (maxDecodedBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxDecodedBytes));

        var payload = await GcpfPayloadReader.OpenAsync(stream, maxDecodedBytes, cancellationToken);
        if (payload.RawSize < 4)
        {
            throw new InvalidDataException("Wonderdraft GCPF payload is too short to contain a Variant.");
        }

        var variantLength = await payload.ReadUInt32Async(cancellationToken);
        if ((long)variantLength + 4 != payload.RawSize)
        {
            throw new InvalidDataException(
                $"Wonderdraft Variant length prefix declares {variantLength} bytes, but the GCPF payload contains {payload.RawSize - 4} bytes after the prefix.");
        }

        var variantReader = new VariantReader(payload, variantLength);
        var rootValue = await variantReader.ReadValueAsync(0, cancellationToken);
        if (variantReader.Remaining != 0)
        {
            throw new InvalidDataException("Wonderdraft Variant decoder did not consume the entire declared payload.");
        }

        await payload.EnsureCompleteAsync(cancellationToken);

        if (rootValue is not DictionaryValue root)
        {
            throw new InvalidDataException("Wonderdraft project root must be a Godot Dictionary.");
        }

        var summary = new WonderdraftProjectSummary(
            OptionalInteger(root.Get("version")),
            RequiredDimension(root.Get("map_width"), "map_width"),
            RequiredDimension(root.Get("map_height"), "map_height"),
            ArrayCount(root.Get("symbols")),
            ArrayCount(root.Get("labels")),
            ArrayCount(root.Get("paths")),
            TerritoryCount(root),
            root.Get("grid") is { } grid && grid is not NilValue,
            StringArray(root.Get("included_packs")),
            StringArray(root.Get("included_default_packs")));

        return new WonderdraftProjectDocument(summary, ExtractCandidates(root));
    }

    private static IReadOnlyList<WonderdraftImportCandidate> ExtractCandidates(DictionaryValue root)
    {
        var records = new List<WonderdraftImportCandidate>();
        AddPointCandidates(records, root.Get("labels"), WonderdraftCandidateKind.Label);
        AddPointCandidates(records, root.Get("symbols"), WonderdraftCandidateKind.Symbol);
        AddShapeCandidates(records, root.Get("paths"), WonderdraftCandidateKind.Path);
        if (root.Get("territories") is DictionaryValue territories)
        {
            AddShapeCandidates(records, territories.Get("territories"), WonderdraftCandidateKind.Territory);
        }

        if (records.Count > MaxCandidateRecords)
        {
            throw new InvalidDataException(
                $"Wonderdraft project exposes {records.Count} import candidates; the safety limit is {MaxCandidateRecords}.");
        }

        var pointCount = records.Sum(item => item.Points.Count);
        if (pointCount > MaxCandidatePoints)
        {
            throw new InvalidDataException(
                $"Wonderdraft project exposes {pointCount} path/territory points; the safety limit is {MaxCandidatePoints}.");
        }

        return records;
    }

    private static void AddPointCandidates(
        List<WonderdraftImportCandidate> destination,
        WonderdraftValue? source,
        WonderdraftCandidateKind kind)
    {
        if (source is not ArrayValue array) return;
        for (var index = 0; index < array.Values.Count; index++)
        {
            var key = $"{kind.ToString().ToLowerInvariant()}:{index}";
            if (array.Values[index] is not DictionaryValue record)
            {
                destination.Add(new WonderdraftImportCandidate(
                    key,
                    kind,
                    $"{kind} {index + 1}",
                    null,
                    null,
                    [],
                    "Wonderdraft record is not a dictionary."));
                continue;
            }

            var position = PixelPoint(record.Get("position"));
            var displayName = kind == WonderdraftCandidateKind.Label
                ? Text(record.Get("text")) ?? $"Label {index + 1}"
                : SymbolName(record.Get("texture"), index);
            var descriptor = kind == WonderdraftCandidateKind.Symbol ? Text(record.Get("texture")) : null;
            destination.Add(new WonderdraftImportCandidate(
                key,
                kind,
                displayName,
                descriptor,
                position,
                [],
                position is null ? "Wonderdraft record has no finite Vector2 position." : null));
        }
    }

    private static void AddShapeCandidates(
        List<WonderdraftImportCandidate> destination,
        WonderdraftValue? source,
        WonderdraftCandidateKind kind)
    {
        if (source is not ArrayValue array) return;
        var minimumPoints = kind == WonderdraftCandidateKind.Territory ? 3 : 2;
        for (var index = 0; index < array.Values.Count; index++)
        {
            var key = $"{kind.ToString().ToLowerInvariant()}:{index}";
            if (array.Values[index] is not DictionaryValue record)
            {
                destination.Add(new WonderdraftImportCandidate(
                    key,
                    kind,
                    $"{kind} {index + 1}",
                    null,
                    null,
                    [],
                    "Wonderdraft record is not a dictionary."));
                continue;
            }

            var localPoints = Points(record.Get("points"));
            var offset = PixelPoint(record.Get("position")) ?? new WonderdraftPixelPoint(0, 0);
            var positioned = localPoints
                .Select(point => new WonderdraftPixelPoint(point.X + offset.X, point.Y + offset.Y))
                .ToArray();
            var descriptor = kind == WonderdraftCandidateKind.Path
                ? Text(record.Get("style"))
                : null;
            destination.Add(new WonderdraftImportCandidate(
                key,
                kind,
                $"{kind} {index + 1}",
                descriptor,
                null,
                positioned,
                positioned.Length < minimumPoints
                    ? $"Wonderdraft {kind.ToString().ToLowerInvariant()} does not contain at least {minimumPoints} finite points."
                    : null));
        }
    }

    private static WonderdraftPixelPoint? PixelPoint(WonderdraftValue? value) =>
        value is VectorValue { Values.Count: >= 2 } vector
        && double.IsFinite(vector.Values[0])
        && double.IsFinite(vector.Values[1])
            ? new WonderdraftPixelPoint(vector.Values[0], vector.Values[1])
            : null;

    private static IReadOnlyList<WonderdraftPixelPoint> Points(WonderdraftValue? value)
    {
        switch (value)
        {
            case VectorArrayValue vectors:
                return vectors.Values
                    .Where(item => item.Count >= 2 && double.IsFinite(item[0]) && double.IsFinite(item[1]))
                    .Select(item => new WonderdraftPixelPoint(item[0], item[1]))
                    .ToArray();
            case ArrayValue array:
                return array.Values
                    .Select(PixelPoint)
                    .Where(point => point is not null)
                    .Cast<WonderdraftPixelPoint>()
                    .ToArray();
            case StringValue text:
                return Vector2TextPattern.Matches(text.Value)
                    .Cast<Match>()
                    .Select(match => ParseVectorMatch(match))
                    .Where(point => point is not null)
                    .Cast<WonderdraftPixelPoint>()
                    .ToArray();
            case DictionaryValue dictionary:
                foreach (var nested in dictionary.Values.Values)
                {
                    var points = Points(nested);
                    if (points.Count > 0) return points;
                }
                return [];
            default:
                return [];
        }
    }

    private static WonderdraftPixelPoint? ParseVectorMatch(Match match)
    {
        if (!double.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            || !double.IsFinite(x)
            || !double.IsFinite(y))
        {
            return null;
        }

        return new WonderdraftPixelPoint(x, y);
    }

    private static string? Text(WonderdraftValue? value) =>
        value is StringValue text && !string.IsNullOrWhiteSpace(text.Value)
            ? text.Value.Trim()
            : null;

    private static string SymbolName(WonderdraftValue? textureValue, int index)
    {
        var texture = Text(textureValue);
        if (texture is null) return $"Symbol {index + 1}";
        var normalized = texture.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        var name = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        return string.IsNullOrWhiteSpace(name) ? $"Symbol {index + 1}" : name;
    }

    private static int ArrayCount(WonderdraftValue? value) => value is ArrayValue array ? array.Values.Count : 0;

    private static int TerritoryCount(DictionaryValue root) =>
        root.Get("territories") is DictionaryValue territories
        && territories.Get("territories") is ArrayValue shapes
            ? shapes.Values.Count
            : 0;

    private static IReadOnlyList<string> StringArray(WonderdraftValue? value)
    {
        if (value is not ArrayValue array) return [];
        return array.Values
            .OfType<StringValue>()
            .Select(item => item.Value)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static int RequiredDimension(WonderdraftValue? value, string field)
    {
        var number = Numeric(value);
        if (number is null
            || !double.IsFinite(number.Value)
            || number.Value <= 0
            || number.Value > int.MaxValue
            || Math.Truncate(number.Value) != number.Value)
        {
            throw new InvalidDataException($"Wonderdraft {field} must be a positive whole-number pixel dimension.");
        }

        return (int)number.Value;
    }

    private static int? OptionalInteger(WonderdraftValue? value)
    {
        var number = Numeric(value);
        return number is not null
               && double.IsFinite(number.Value)
               && number.Value >= int.MinValue
               && number.Value <= int.MaxValue
               && Math.Truncate(number.Value) == number.Value
            ? (int)number.Value
            : null;
    }

    private static double? Numeric(WonderdraftValue? value) => value switch
    {
        IntegerValue integer => integer.Value,
        RealValue real => real.Value,
        _ => null
    };

    private abstract record WonderdraftValue;
    private sealed record NilValue : WonderdraftValue
    {
        public static NilValue Instance { get; } = new();
    }

    private sealed record BooleanValue(bool Value) : WonderdraftValue;
    private sealed record IntegerValue(long Value) : WonderdraftValue;
    private sealed record RealValue(double Value) : WonderdraftValue;
    private sealed record StringValue(string Value) : WonderdraftValue;
    private sealed record VectorValue(string Kind, IReadOnlyList<double> Values) : WonderdraftValue;
    private sealed record VectorArrayValue(string Kind, IReadOnlyList<IReadOnlyList<double>> Values) : WonderdraftValue;
    private sealed record ArrayValue(IReadOnlyList<WonderdraftValue> Values) : WonderdraftValue;
    private sealed record DictionaryValue(IReadOnlyDictionary<string, WonderdraftValue> Values) : WonderdraftValue
    {
        public WonderdraftValue? Get(string key) => Values.GetValueOrDefault(key);
    }

    private sealed record OpaqueValue : WonderdraftValue
    {
        public static OpaqueValue Instance { get; } = new();
    }

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

internal static class UInt32FlagExtensions
{
    public static bool HasFlag(this uint value, uint flag) => (value & flag) != 0;
}
