namespace HexCrawl.Application.Assets;

public sealed record MapAssetWriteResult(string AssetKey, long Length);
public sealed record MapAssetInfo(string AssetKey, long Length, DateTimeOffset LastModified);

public interface IMapAssetStore
{
    Task<MapAssetWriteResult> WriteAsync(Stream source, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string assetKey, CancellationToken cancellationToken = default);
    Task<MapAssetInfo?> GetInfoAsync(string assetKey, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string assetKey, CancellationToken cancellationToken = default);
}
