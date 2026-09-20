namespace HexCrawl.Web.Api;

public sealed class MapImportOptions
{
    public const string SectionName = "MapImport";
    public long MaxFileBytes { get; set; } = 100L * 1024 * 1024;
    public long MaxWonderdraftDecodedBytes { get; set; } = 1L * 1024 * 1024 * 1024;
    public long MaxPixelCount { get; set; } = 100_000_000;
    public int MaxDimension { get; set; } = 32_768;

    public void Validate()
    {
        if (MaxFileBytes <= 0) throw new InvalidOperationException("MapImport:MaxFileBytes must be positive.");
        if (MaxWonderdraftDecodedBytes <= 0) throw new InvalidOperationException("MapImport:MaxWonderdraftDecodedBytes must be positive.");
        if (MaxPixelCount <= 0) throw new InvalidOperationException("MapImport:MaxPixelCount must be positive.");
        if (MaxDimension <= 0) throw new InvalidOperationException("MapImport:MaxDimension must be positive.");
    }
}
