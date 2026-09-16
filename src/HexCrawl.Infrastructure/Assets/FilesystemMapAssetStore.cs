using System.Text.RegularExpressions;
using HexCrawl.Application.Assets;

namespace HexCrawl.Infrastructure.Assets;

public sealed partial class FilesystemMapAssetStore : IMapAssetStore
{
    private readonly string _root;
    private readonly string _mapsRoot;
    private readonly string _temporaryRoot;

    public FilesystemMapAssetStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Asset root is required.", nameof(root));
        _root = Path.GetFullPath(root);
        _mapsRoot = Path.Combine(_root, "maps");
        _temporaryRoot = Path.Combine(_root, ".tmp");
        Directory.CreateDirectory(_mapsRoot);
        Directory.CreateDirectory(_temporaryRoot);
    }

    public async Task<MapAssetWriteResult> WriteAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var id = Guid.NewGuid().ToString("N");
        var assetKey = $"maps/{id}";
        var finalPath = Resolve(assetKey);
        var temporaryPath = Path.Combine(_temporaryRoot, $"{Guid.NewGuid():N}.upload");
        long length = 0;
        try
        {
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    length += read;
                }
                await target.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, finalPath, overwrite: false);
            return new MapAssetWriteResult(assetKey, length);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string assetKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(assetKey);
        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task<MapAssetInfo?> GetInfoAsync(string assetKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(assetKey);
        var file = new FileInfo(path);
        return Task.FromResult<MapAssetInfo?>(file.Exists
            ? new MapAssetInfo(assetKey, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero))
            : null);
    }

    public Task<bool> DeleteAsync(string assetKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(assetKey);
        if (!File.Exists(path)) return Task.FromResult(false);
        File.Delete(path);
        return Task.FromResult(true);
    }

    private string Resolve(string assetKey)
    {
        if (string.IsNullOrWhiteSpace(assetKey) || !AssetKeyPattern().IsMatch(assetKey))
        {
            throw new ArgumentException("AssetKey is not valid for the filesystem map-asset provider.", nameof(assetKey));
        }

        var relative = assetKey.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        var mapsPrefix = _mapsRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _mapsRoot
            : _mapsRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(mapsPrefix, comparison) || string.Equals(full, _mapsRoot, comparison))
        {
            throw new ArgumentException("AssetKey resolves outside the map-asset root.", nameof(assetKey));
        }
        return full;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // The original write failure remains authoritative; startup never performs destructive cleanup.
        }
    }

    [GeneratedRegex("^maps/[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex AssetKeyPattern();
}
