using HexCrawl.Infrastructure.Assets;

namespace HexCrawl.Application.Tests;

public sealed class MapAssetStoreTests
{
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
    public async Task RasterInspectorRecognizesPngJpegAndWebPFromContentSignatures()
    {
        var png = new byte[]
        {
            137,80,78,71,13,10,26,10, 0,0,0,13, 73,72,68,82,
            0,0,8,0, 0,0,4,0
        };
        var jpeg = new byte[] { 0xff,0xd8,0xff,0xc0,0x00,0x07,0x08,0x00,0x02,0x00,0x03,0x00 };
        var webp = new byte[]
        {
            82,73,70,70, 22,0,0,0, 87,69,66,80,
            86,80,56,88, 10,0,0,0,
            0,0,0,0, 0x0f,0,0, 0x07,0,0
        };

        var pngInfo = await RasterImageInspector.InspectAsync(new MemoryStream(png));
        var jpegInfo = await RasterImageInspector.InspectAsync(new MemoryStream(jpeg));
        var webpInfo = await RasterImageInspector.InspectAsync(new MemoryStream(webp));
        Assert.Equal(("image/png", 2048, 1024), (pngInfo.MediaType, pngInfo.Width, pngInfo.Height));
        Assert.Equal(("image/jpeg", 3, 2), (jpegInfo.MediaType, jpegInfo.Width, jpegInfo.Height));
        Assert.Equal(("image/webp", 16, 8), (webpInfo.MediaType, webpInfo.Width, webpInfo.Height));
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
