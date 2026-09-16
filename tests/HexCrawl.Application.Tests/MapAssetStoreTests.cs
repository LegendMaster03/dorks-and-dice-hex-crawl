using HexCrawl.Infrastructure.Assets;

namespace HexCrawl.Application.Tests;

public sealed class MapAssetStoreTests
{
    private static readonly byte[] CompletePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAMAAAACCAIAAAASFvFNAAAAFUlEQVR4nGPkEpFjYGBgYGBgYoABAARgAEB5qHZqAAAAAElFTkSuQmCC");

    private static readonly byte[] CompleteJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAACAAMDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDxGiiitjI//9k=");

    private static readonly byte[] CompleteWebP = Convert.FromBase64String(
        "UklGRh4AAABXRUJQVlA4TBEAAAAvD8ABAAdQiirUo/+BiOh/AAA=");

    [Fact]
    public async Task GeneratedAssetKeyDoesNotUseUserPathContentAndRoundTripsByStream()
    {
        var root = NewRoot();
        try
        {
            var store = new FilesystemMapAssetStore(root);
            var bytes = Enumerable.Range(0, 1024 * 1024).Select(index => (byte)(index % 251)).ToArray();
            await using var input = new MemoryStream(bytes, writable: false);
            var written = await store.WriteAsync(input);
            Assert.Matches("^maps/[0-9a-f]{32}$", written.AssetKey);
            Assert.DoesNotContain("..", written.AssetKey);
            Assert.Equal(bytes.Length, written.Length);

            await using var output = await store.OpenReadAsync(written.AssetKey);
            Assert.IsType<FileStream>(output);
            using var copy = new MemoryStream();
            await output.CopyToAsync(copy, 8192);
            Assert.Equal(bytes, copy.ToArray());
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DeleteRemovesAssetAndMissingAssetHasDeterministicBehavior()
    {
        var root = NewRoot();
        try
        {
            var store = new FilesystemMapAssetStore(root);
            await using var input = new MemoryStream([1, 2, 3]);
            var written = await store.WriteAsync(input);
            Assert.True(await store.DeleteAsync(written.AssetKey));
            Assert.Null(await store.GetInfoAsync(written.AssetKey));
            Assert.False(await store.DeleteAsync(written.AssetKey));
            await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            {
                await using var _ = await store.OpenReadAsync(written.AssetKey);
            });
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("maps/../../outside")]
    [InlineData("/tmp/outside")]
    [InlineData("maps/not-a-generated-id")]
    public async Task PathTraversalAndUntrustedKeysCanNotEscapeAssetRoot(string assetKey)
    {
        var root = NewRoot();
        try
        {
            var store = new FilesystemMapAssetStore(root);
            await Assert.ThrowsAsync<ArgumentException>(() => store.GetInfoAsync(assetKey));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task FailedWriteLeavesNoFinalOrTemporaryPartialAsset()
    {
        var root = NewRoot();
        try
        {
            var store = new FilesystemMapAssetStore(root);
            await using var failing = new FailingReadStream();
            await Assert.ThrowsAsync<IOException>(() => store.WriteAsync(failing));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "maps")));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, ".tmp")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task RasterInspectorAcceptsCompletePngAndExtractsDimensions()
    {
        var info = await RasterImageInspector.InspectAsync(new MemoryStream(CompletePng, writable: false));
        Assert.Equal(("image/png", 3, 2), (info.MediaType, info.Width, info.Height));
    }

    [Fact]
    public async Task RasterInspectorRejectsPngWithCompleteIhdrButNoImageOrIend()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            RasterImageInspector.InspectAsync(new MemoryStream(CompletePng[..33], writable: false)));
    }

    [Fact]
    public async Task RasterInspectorAcceptsCompleteJpegAndExtractsDimensions()
    {
        var info = await RasterImageInspector.InspectAsync(new MemoryStream(CompleteJpeg, writable: false));
        Assert.Equal(("image/jpeg", 3, 2), (info.MediaType, info.Width, info.Height));
    }

    [Fact]
    public async Task RasterInspectorRejectsJpegWithCompleteSofButNoScanOrEoi()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            RasterImageInspector.InspectAsync(new MemoryStream(CompleteJpeg[..177], writable: false)));
    }

    [Fact]
    public async Task RasterInspectorAcceptsCompleteWebPAndExtractsDimensions()
    {
        var info = await RasterImageInspector.InspectAsync(new MemoryStream(CompleteWebP, writable: false));
        Assert.Equal(("image/webp", 16, 8), (info.MediaType, info.Width, info.Height));
    }

    [Fact]
    public async Task RasterInspectorRejectsWebPWithValidPrimaryHeaderButTruncatedContainer()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            RasterImageInspector.InspectAsync(new MemoryStream(CompleteWebP[..25], writable: false)));
    }

    [Fact]
    public async Task RasterInspectorRejectsUnsupportedAndMalformedInputs()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => RasterImageInspector.InspectAsync(new MemoryStream("GIF89a-not-a-raster-we-support"u8.ToArray())));
        await Assert.ThrowsAsync<InvalidDataException>(() => RasterImageInspector.InspectAsync(new MemoryStream(new byte[] { 137,80,78,71,13,10,26,10,0,0,0,13 })));
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), $"hex-crawl-assets-{Guid.NewGuid():N}");

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class FailingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("synthetic read failure");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(new IOException("synthetic read failure"));
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
