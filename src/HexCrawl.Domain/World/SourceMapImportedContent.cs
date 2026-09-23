using HexCrawl.Domain.Spatial;

namespace HexCrawl.Domain.World;

public enum SourceMapContentKind
{
    Label,
    Symbol,
    Line,
    Region
}

public sealed record SourceMapContentElement(
    string SourceRecordKey,
    SourceMapContentKind Kind,
    string DisplayName,
    string? Descriptor,
    WorldPoint? SourcePosition,
    IReadOnlyList<WorldPoint> SourcePoints,
    IReadOnlyDictionary<string, string> Properties);

public sealed record SourceMapImportProvenance(
    string SourceType,
    string SourceFingerprint,
    DateTimeOffset ImportedAt,
    int SourceRecordCount);


public sealed record SourceMapSourceArchive(
    string AssetKey,
    long Length,
    string MediaType,
    string? OriginalFileName);
