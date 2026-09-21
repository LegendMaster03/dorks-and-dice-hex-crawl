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

public static partial class WonderdraftProjectInspector
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


}

internal static class UInt32FlagExtensions
{
    public static bool HasFlag(this uint value, uint flag) => (value & flag) != 0;
}
