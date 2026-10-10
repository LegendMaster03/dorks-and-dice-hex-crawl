using System;
using System.Linq;
using HexCrawl.Application;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;
using Npgsql;

namespace HexCrawl.Application.Tests;

public sealed class Phase17PostgresWorldTests
{
    [Fact]
    public async Task GeneralizedUnfamiliarMotifRoundTripsWithoutAnImportedMapOrCatalog()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        const string unfamiliar = "<10:2 5 4 6 7 8 10,1 4 6 5 9 10,3 5 7 8 9 10:3 3 4,5 5>";
        var generated = DelaneyDressHarmonicMetricRealization.Construct(unfamiliar, 1, "unit");
        Assert.Equal("realized", generated.Status);
        var tiling = new PeriodicWorldTiling(Guid.NewGuid(), generated.Topology!,
            generated.Realization!, new WorldPoint(12, -8));
        var address = new PeriodicCellAddress(tiling.Topology.MotifCells[0].Id, new(-4, 7));
        var position = tiling.Resolve(address).Center;
        var feature = new PointFeature(Guid.NewGuid(), "Test spring", "water", position);
        var annotation = new EnvironmentAnnotation
        {
            Id = Guid.NewGuid(),
            Scope = new EnvironmentAnnotationScope
            {
                Kind = EnvironmentAnnotationScopeKind.Cell, Cell = new WorldCellId(tiling.Id, address)
            },
            Facts = [new EnvironmentFact
            {
                Id = Guid.NewGuid(), Dimension = "terrain",
                ValueKind = EnvironmentValueKind.Tag, Tag = "bog"
            }]
        };
        var original = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "No catalog", Tiling = tiling,
            Features = [feature], EnvironmentAnnotations = [annotation]
        };
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(original, "owner", 1, now, now));

        var restarted = new PostgresHexCrawlStore(database.ConnectionString);
        await restarted.InitializeAsync();
        var loaded = await restarted.GetOverworldAsync(original.Id, "owner");
        Assert.NotNull(loaded);
        Assert.Equal(original.Id, loaded!.World.Id);
        Assert.Equal(1, loaded.Version);
        Assert.False(loaded.World.HasLegacyHexGrid);
        Assert.Equal(tiling.Id, loaded.World.SpatialTiling.Id);
        Assert.Equal(tiling.Topology.TranslationDsSymbol,
            loaded.World.SpatialTiling.Topology.TranslationDsSymbol);
        Assert.Equal(tiling.Revision, loaded.World.SpatialTiling.Revision);
        Assert.Equal(tiling.Realization.Units, loaded.World.SpatialTiling.Realization.Units);
        Assert.Equal(tiling.Resolve(address).Polygon, loaded.World.SpatialTiling.Resolve(address).Polygon);
        Assert.Single(loaded.World.FeaturesIntersecting(address));
        Assert.Single(loaded.World.AnnotationsForCell(address));
        Assert.Null(await restarted.GetOverworldAsync(original.Id, "different-owner"));
        var renamed = await restarted.SaveOverworldAsync(
            loaded with { World = loaded.World with { Name = "Renamed" } }, loaded.Version);
        Assert.Equal(SaveOutcome.Saved, renamed.Outcome);
        Assert.Equal(SaveOutcome.Conflict, (await restarted.SaveOverworldAsync(loaded, 1)).Outcome);
    }

    [Fact]
    public async Task LegacyWorldWithExpeditionMapRegistrationAndEventSurvivesFormatOneReads()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var origin = new WorldPoint(4.5, -2.5);
        var grid = new HexGridDefinition
        {
            Id = Guid.NewGuid(), Orientation = HexOrientation.FlatTop,
            Origin = origin, RotationDegrees = 41, HexRadiusWorldUnits = 3.25,
            NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
        };
        var map = new SourceMapRepresentation(
            Guid.NewGuid(), "test", "Map", SourceMapRole.Gm, "unchanged-asset-key",
            false, MapRegistrationTransform.Affine(2, 0, 0, 2, -3, 7),
            [new WorldPoint(-5, -5), new WorldPoint(10, -5), new WorldPoint(10, 10)]);
        var feature = new PointFeature(Guid.NewGuid(), "Ruins", "ruins", new WorldPoint(1, 2));
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Old world", Grid = grid,
            SourceMaps = [map], Features = [feature]
        };
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(world, "owner", 1, now, now));
        // Represent a real pre-Phase-17 world_json with no format or tiling fields.
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE overworlds
                SET world_json = world_json - 'formatVersion' - 'tiling'
                WHERE id = @id;
                """;
            command.Parameters.AddWithValue("id", world.Id);
            await command.ExecuteNonQueryAsync();
        }
        var current = new HexCoordinate(-91, 47);
        var evt = new CrawlRuntimeEvent(1, 1, CrawlRuntimeEventKind.WatchStarted,
            TimeSpan.Zero, current, "An event must survive.");
        var expedition = new StoredExpedition(
            "Resumable", new ExpeditionState
            {
                Id = Guid.NewGuid(), Position = HexGeometry.HexToWorld(grid, current),
                PositionPrecision = WorldPositionPrecision.HexAnchor,
                Traversal = HexTraversalState.StartingIn(current, DistanceUnit.Miles),
                Navigation = new NavigationRuntimeState(false, 0),
                DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles),
                History = [evt]
            },
            new WorldBoundCrawlSessionContext(world.Id), null,
            CrawlProcedureCatalog.Resolve(CrawlProcedureCatalog.Dnd35PresetKey).MaterializeGeneric().Procedure,
            null, TimeSpan.Zero, "owner", 1, now, now);
        await store.CreateExpeditionAsync(expedition);

        var restarted = new PostgresHexCrawlStore(database.ConnectionString);
        await restarted.InitializeAsync();
        var loaded = await restarted.GetOverworldAsync(world.Id, "owner");
        Assert.NotNull(loaded);
        Assert.Equal(grid, loaded!.World.Grid);
        Assert.Equal(world.Id, loaded.World.Id);
        Assert.Equal(map.Id, Assert.Single(loaded.World.SourceMaps).Id);
        Assert.Equal(map.AssetKey, loaded.World.SourceMaps[0].AssetKey);
        Assert.Equal(map.Alignment, loaded.World.SourceMaps[0].Alignment);
        Assert.Equal(feature.Id, Assert.Single(loaded.World.Features).Id);
        Assert.Equal(grid.Id, loaded.World.SpatialTiling.Id);
        var address = LegacyHexTilingCompatibility.ToAddress(current);
        Assert.InRange(loaded.World.SpatialTiling.Resolve(address).Center.DistanceTo(
            HexGeometry.HexToWorld(grid, current)), 0, 1e-8);
        var savedExpedition = await restarted.GetExpeditionAsync(expedition.Id, "owner");
        Assert.NotNull(savedExpedition);
        Assert.Equal(current, savedExpedition!.State.CurrentHex);
        Assert.Equal(expedition.CampaignProcedure.Revision, savedExpedition.CampaignProcedure.Revision);
        Assert.Equal(evt.Message, Assert.Single(savedExpedition.State.History).Message);
        Assert.Equal(expedition.Id, savedExpedition.Id);
    }

    [Fact]
    public async Task WorldReadRejectsSnapshotIdMismatchWithoutRewritingData()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var original = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Identity mismatch",
            Grid = new HexGridDefinition
            {
                Id = Guid.NewGuid(), HexRadiusWorldUnits = 1,
                NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
            }
        };
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(original, "owner", 4, now, now));
        var wrongId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var corrupt = connection.CreateCommand())
        {
            corrupt.CommandText = """
                UPDATE overworlds
                SET world_json = jsonb_set(world_json, '{id}', to_jsonb(@wrongId::text))
                WHERE id = @id;
                """;
            corrupt.Parameters.AddWithValue("id", original.Id);
            corrupt.Parameters.AddWithValue("wrongId", wrongId.ToString());
            await corrupt.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.GetOverworldAsync(original.Id, "owner"));
        await using var verify = connection.CreateCommand();
        verify.CommandText = """
            SELECT world_json->>'id', version FROM overworlds WHERE id = @id;
            """;
        verify.Parameters.AddWithValue("id", original.Id);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(wrongId.ToString(), reader.GetString(0));
        Assert.Equal(4L, reader.GetInt64(1));
    }

    [Fact]
    public async Task FutureWorldSnapshotVersionFailsClosedAndDoesNotMutateRecord()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var world = new OverworldDefinition
        {
            Id = Guid.NewGuid(), Name = "Future format fixture",
            Grid = new HexGridDefinition
            {
                Id = Guid.NewGuid(), HexRadiusWorldUnits = 1,
                NeighborCenterDistance = new DistanceMeasure(12, DistanceUnit.Miles)
            }
        };
        var now = DateTimeOffset.UtcNow;
        await store.CreateOverworldAsync(new StoredOverworld(world, "owner", 1, now, now));
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE overworlds
                SET world_json = jsonb_set(world_json, '{formatVersion}', '77'::jsonb)
                WHERE id = @id;
                """;
            command.Parameters.AddWithValue("id", world.Id);
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => store.GetOverworldAsync(world.Id, "owner"));
        await using var count = connection.CreateCommand();
        count.CommandText = """
            SELECT world_json->>'formatVersion', version FROM overworlds WHERE id = @id;
            """;
        count.Parameters.AddWithValue("id", world.Id);
        await using var reader = await count.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("77", reader.GetString(0));
        Assert.Equal(1, reader.GetInt64(1));
    }
}
