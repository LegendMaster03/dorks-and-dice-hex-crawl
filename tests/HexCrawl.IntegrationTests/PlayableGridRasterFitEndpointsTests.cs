using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HexCrawl.Application;
using HexCrawl.Domain.Spatial;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.IntegrationTests;

public sealed class PlayableGridRasterFitEndpointsTests
{
    [Theory]
    [InlineData(5, -2)]
    [InlineData(9, -4)]
    public async Task PreviewAndSaveFullBleedSquareMapPreserveCenteredExtentAndSourcePixels(
        int size, int min)
    {
        var database = TestWebHost.NewDatabasePath();
        try
        {
            Guid worldId;
            Guid sourceMapId;
            long version;
            var store = new PostgresHexCrawlStore(database);
            await store.InitializeAsync();
            var materialization = DelaneyDressHarmonicMetricRealization.Construct(
                "<1:1,1,1:4,4>", 1, "world-unit");
            Assert.Equal("realized", materialization.Status);
            var tiling = new PeriodicWorldTiling(
                Guid.NewGuid(), materialization.Topology!, materialization.Realization!,
                new WorldPoint(0, 0))
            {
                PlayableBounds = new PeriodicMapBounds(size, size, min, min)
            };
            var created = await new HexCrawlService(store).CreatePeriodicOverworldAsync(
                "alice", "Finite square map import", tiling);
            var maps = new SourceMapApplicationService(store);
            var uploaded = await maps.CreateUploadedAsync(created.World.Id, "alice",
                new UploadSourceMapCommand(
                    "session", "Square grid image", HexCrawl.Domain.World.SourceMapRole.Gm,
                    "maps/testing/reference", true, 1500, 1500, "image/png",
                    "full-grid.png", created.Version));
            worldId = uploaded.World.Id;
            version = uploaded.Version;
            sourceMapId = Assert.Single(uploaded.World.SourceMaps).Id;

            using (var host = TestWebHost.Create(database, "alice"))
            using (var client = host.CreateClient())
            {
                using var preview = await client.GetAsync(
                    $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/playable-grid-fit");
                preview.EnsureSuccessStatusCode();
                var proposal = await preview.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(version, proposal.GetProperty("worldVersion").GetInt64());
                Assert.Equal(size, proposal.GetProperty("playableBounds").GetProperty("width").GetInt32());
                Assert.Equal(min, proposal.GetProperty("playableBounds").GetProperty("minU").GetInt32());
                var alignment = proposal.GetProperty("alignment");
                Assert.Equal("Affine", alignment.GetProperty("kind").GetString());

                using (var otherHost = TestWebHost.Create(database, "bob"))
                using (var other = otherHost.CreateClient())
                {
                    using var deniedPreview = await other.GetAsync(
                        $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/playable-grid-fit");
                    Assert.Equal(HttpStatusCode.NotFound, deniedPreview.StatusCode);
                    using var deniedSave = await other.PutAsJsonAsync(
                        $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/playable-grid-fit",
                        new { expectedVersion = version });
                    Assert.Equal(HttpStatusCode.NotFound, deniedSave.StatusCode);
                }

                using var saved = await client.PutAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/playable-grid-fit",
                    new { expectedVersion = version });
                saved.EnsureSuccessStatusCode();
                var updated = await saved.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(version + 1, updated.GetProperty("version").GetInt64());
                Assert.Equal(alignment.GetRawText(), updated.GetProperty("sourceMaps")[0]
                    .GetProperty("alignment").GetRawText());
                Assert.Equal(4, updated.GetProperty("sourceMaps")[0]
                    .GetProperty("worldCoverageBoundary").GetArrayLength());
                Assert.Equal(min, updated.GetProperty("tiling")
                    .GetProperty("playableBounds").GetProperty("minU").GetInt32());

                using var stale = await client.PutAsJsonAsync(
                    $"/api/overworlds/{worldId:D}/source-maps/{sourceMapId:D}/playable-grid-fit",
                    new { expectedVersion = version });
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            }

            var reopened = new PostgresHexCrawlStore(database);
            await reopened.InitializeAsync();
            var persisted = await reopened.GetOverworldAsync(worldId, "alice");
            Assert.NotNull(persisted);
            Assert.Equal(new PeriodicMapBounds(size, size, min, min),
                persisted!.World.SpatialTiling.PlayableBounds);
            var source = Assert.Single(persisted.World.SourceMaps);
            Assert.Equal("full-grid.png", source.OriginalFileName);
            Assert.Equal(1500, source.PixelWidth);
            Assert.NotNull(source.Alignment);
        }
        finally { TestWebHost.DeleteDatabase(database); }
    }
}
