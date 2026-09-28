using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;
using HexCrawl.Domain.World;
using HexCrawl.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace HexCrawl.Application.Tests;

public sealed class GenericProcedureReviewRegressionTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task MatchingGenericAndCompatibilitySnapshotsCanBeCreatedSavedAndLoaded()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new CrawlSessionService(store);

        var started = await service.StartAsync(
            "alice",
            new StartStandaloneCrawlSessionCommand(
                "Consistent",
                "simple-fixed-distance",
                new NonSpatialCrawlSessionContext("Travel clock")));

        Assert.NotNull(started.CampaignProcedure);
        Assert.Equal(started.Procedure, CampaignProcedureCompatibilityProjector.Project(started.CampaignProcedure!));

        var saved = await store.SaveExpeditionAsync(started with { Name = "Still consistent" }, started.Version);
        Assert.Equal(SaveOutcome.Saved, saved.Outcome);

        var loaded = await store.GetExpeditionAsync(started.Id, "alice");
        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.CampaignProcedure);
        Assert.Equal(loaded.Procedure, CampaignProcedureCompatibilityProjector.Project(loaded.CampaignProcedure!));
    }

    [Fact]
    public async Task MismatchedGenericAndCompatibilitySnapshotsAreRejectedBeforeCreateAndSave()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new CrawlSessionService(store);
        var started = await service.StartAsync(
            "alice",
            new StartStandaloneCrawlSessionCommand(
                "Consistent",
                "simple-fixed-distance",
                new NonSpatialCrawlSessionContext("Travel clock")));
        var startedProcedure = Assert.IsType<CrawlProcedureProfile>(started.Procedure);
        var mismatch = startedProcedure with { WatchLength = TimeSpan.FromHours(5) };

        var createMismatch = DuplicateStandalone(started, "Create mismatch") with { Procedure = mismatch };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateExpeditionAsync(createMismatch));

        var saveMismatch = started with { Procedure = mismatch };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveExpeditionAsync(saveMismatch, started.Version));
    }

    [Fact]
    public async Task ContradictoryPersistedSnapshotsAreRejectedOnLoad()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new CrawlSessionService(store);
        var started = await service.StartAsync(
            "alice",
            new StartStandaloneCrawlSessionCommand(
                "Persisted mismatch",
                "simple-fixed-distance",
                new NonSpatialCrawlSessionContext("Travel clock")));
        var startedProcedure = Assert.IsType<CrawlProcedureProfile>(started.Procedure);
        var mismatch = startedProcedure with { WatchLength = TimeSpan.FromHours(5) };

        await UpdateJsonAsync(database.ConnectionString, started.Id, "procedure_json", mismatch);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.GetExpeditionAsync(started.Id, "alice"));
        Assert.Contains("contradictory", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LegacyNullGenericSnapshotStillLoads()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new CrawlSessionService(store);
        var started = await service.StartAsync(
            "alice",
            new StartStandaloneCrawlSessionCommand(
                "Legacy snapshot",
                "simple-fixed-distance",
                new NonSpatialCrawlSessionContext("Travel clock")));

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE expeditions SET campaign_procedure_json = NULL WHERE id = @id;";
            command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, started.Id);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var loaded = await store.GetExpeditionAsync(started.Id, "alice");
        Assert.NotNull(loaded);
        Assert.Null(loaded!.CampaignProcedure);
        Assert.Equal(started.Procedure, loaded.Procedure);
    }

    [Fact]
    public async Task UnknownFutureMechanicExpeditionSnapshotIsPreservedWithoutForcedProjection()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new CrawlSessionService(store);
        var started = await service.StartAsync(
            "alice",
            new StartStandaloneCrawlSessionCommand(
                "Future source",
                "simple-fixed-distance",
                new NonSpatialCrawlSessionContext("Travel clock")));
        var future = WithFutureMovementMechanic(started.CampaignProcedure!);
        var futureExpedition = DuplicateStandalone(started, "Future snapshot") with
        {
            CampaignProcedure = future
        };

        var created = await store.CreateExpeditionAsync(futureExpedition);
        var loaded = await store.GetExpeditionAsync(created.Id, "alice");

        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.CampaignProcedure);
        var movement = loaded.CampaignProcedure!.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule);
        Assert.Equal("future-movement-resolution", movement.Mechanic.Key);
        Assert.Equal(42, movement.Mechanic.Version);
        Assert.Equal("future.runtime.handler", movement.Mechanic.ExecutionHandler);
        Assert.False(CampaignProcedureCompatibilityProjector.TryProject(loaded.CampaignProcedure, out _));
        Assert.Equal(started.Procedure, loaded.Procedure);
    }

    [Fact]
    public void MaterializationDeepCopiesRecipeAndCatalogOwnedCollections()
    {
        var source = CrawlProcedureCatalog.Resolve("simple-fixed-distance");
        var mutableSelections = source.Recipe.ModuleSelections
            .Select(selection => selection with
            {
                Parameters = selection.Parameters.ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.Ordinal)
            })
            .ToArray();
        var mutablePreset = source with
        {
            Recipe = source.Recipe with { ModuleSelections = mutableSelections }
        };

        var materialized = mutablePreset.MaterializeGeneric();
        var sourceTime = mutableSelections.Single(selection =>
            selection.ModuleKey == GenericProcedureCatalog.TimeIntervalModule);
        var snapshotTime = materialized.Procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);
        var sourceParameters = Assert.IsType<Dictionary<string, string>>(sourceTime.Parameters);
        var originalDuration = snapshotTime.Parameters["durationTicks"];
        sourceParameters["durationTicks"] = TimeSpan.FromHours(99).Ticks.ToString(CultureInfo.InvariantCulture);

        Assert.Equal(originalDuration, snapshotTime.Parameters["durationTicks"]);
        Assert.NotSame(sourceTime.Parameters, snapshotTime.Parameters);

        var catalogMovement = GenericProcedureCatalog.ResolveModule(GenericProcedureCatalog.MovementResolutionModule);
        var catalogMechanic = GenericProcedureCatalog.ResolveMechanic(GenericProcedureCatalog.MovementResolutionPolicyMechanic);
        var snapshotMovement = materialized.Procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule);
        Assert.NotSame(catalogMovement.Reads, snapshotMovement.Module.Reads);
        Assert.NotSame(catalogMovement.ConfigurationSchema, snapshotMovement.Module.ConfigurationSchema);
        Assert.NotSame(catalogMovement.PresentationMetadata, snapshotMovement.Module.PresentationMetadata);
        Assert.NotSame(catalogMechanic.InputContract, snapshotMovement.Mechanic.InputContract);
        Assert.NotSame(catalogMechanic.ParameterSchema, snapshotMovement.Mechanic.ParameterSchema);

        var catalogMetadata = Assert.IsType<Dictionary<string, string>>(catalogMovement.PresentationMetadata);
        var originalPhase = catalogMetadata["phase"];
        try
        {
            catalogMetadata["phase"] = "mutated-after-materialization";
            Assert.Equal(originalPhase, snapshotMovement.Module.PresentationMetadata["phase"]);
        }
        finally
        {
            catalogMetadata["phase"] = originalPhase;
        }
    }

    [Fact]
    public void NewRevisionOwnsIndependentNestedCollectionsAndOverrideParameters()
    {
        var first = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;
        var sixHours = TimeSpan.FromHours(6).Ticks.ToString(CultureInfo.InvariantCulture);
        var overrideParameters = new Dictionary<string, string>
        {
            ["durationTicks"] = sixHours
        };
        var change = new CampaignProcedureOverride(
            "six-hour-watch",
            GenericProcedureCatalog.TimeIntervalModule,
            null,
            null,
            overrideParameters);

        var second = CampaignProcedureMaterializer.CreateRevision(first, [change]);
        var firstTime = first.Modules.Single(module => module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);
        var secondTime = second.Modules.Single(module => module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);

        Assert.NotSame(firstTime.Module, secondTime.Module);
        Assert.NotSame(firstTime.Module.ConfigurationSchema, secondTime.Module.ConfigurationSchema);
        Assert.NotSame(firstTime.Mechanic.ParameterSchema, secondTime.Mechanic.ParameterSchema);
        Assert.NotSame(firstTime.Parameters, secondTime.Parameters);

        overrideParameters["durationTicks"] = TimeSpan.FromHours(12).Ticks.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(sixHours, Assert.Single(second.Overrides).Parameters["durationTicks"]);

        var secondParameters = Assert.IsType<Dictionary<string, string>>(secondTime.Parameters);
        secondParameters["durationTicks"] = TimeSpan.FromHours(8).Ticks.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(TimeSpan.FromHours(4), CampaignProcedureCompatibilityProjector.Project(first).WatchLength);
        Assert.Equal(TimeSpan.FromHours(8), CampaignProcedureCompatibilityProjector.Project(second).WatchLength);
    }

    [Fact]
    public async Task StandaloneAndWorldBoundCreationBothPinGenericMaterializationWithCompatibilityProjection()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();

        var standaloneService = new CrawlSessionService(store);
        var standalone = await standaloneService.StartAsync(
            "alice",
            new StartStandaloneCrawlSessionCommand(
                "Standalone",
                "simple-fixed-distance",
                new NonSpatialCrawlSessionContext("Travel clock")));

        var core = new HexCrawlService(store);
        var world = await core.CreateOverworldAsync("alice", WorldCommand());
        var worldBound = await core.StartExpeditionAsync(
            world.World.Id,
            "alice",
            new StartExpeditionCommand("World-bound", "simple-fixed-distance", new HexCoordinate(0, 0)));

        AssertPinned(standalone, "simple-fixed-distance");
        AssertPinned(worldBound, "simple-fixed-distance");

        var reloadedWorldBound = await store.GetExpeditionAsync(worldBound.Id, "alice");
        Assert.NotNull(reloadedWorldBound);
        AssertPinned(reloadedWorldBound!, "simple-fixed-distance");
        Assert.Equal(worldBound.CampaignProcedure!.ProcedureId, reloadedWorldBound!.CampaignProcedure!.ProcedureId);
    }

    private static void AssertPinned(StoredExpedition expedition, string presetKey)
    {
        Assert.NotNull(expedition.CampaignProcedure);
        Assert.Equal(expedition.Procedure, CampaignProcedureCompatibilityProjector.Project(expedition.CampaignProcedure!));
        Assert.Equal(presetKey, expedition.ProcedureOrigin?.PresetKey);
    }

    private static StoredExpedition DuplicateStandalone(StoredExpedition source, string name)
    {
        var runtime = Assert.IsType<NonSpatialSessionState>(source.Runtime) with { Id = Guid.NewGuid() };
        var now = DateTimeOffset.UtcNow;
        return source with
        {
            Name = name,
            Runtime = runtime,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static CampaignProcedure WithFutureMovementMechanic(CampaignProcedure source)
    {
        var movement = source.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule);
        const string futureMechanicKey = "future-movement-resolution";
        var futureMovement = movement with
        {
            Module = movement.Module with
            {
                CompatibleMechanicTypes = movement.Module.CompatibleMechanicTypes
                    .Concat([futureMechanicKey])
                    .ToArray()
            },
            Mechanic = movement.Mechanic with
            {
                Key = futureMechanicKey,
                DisplayName = "Future movement resolution",
                ExecutionHandler = "future.runtime.handler",
                Version = 42
            }
        };
        var future = source with
        {
            ProcedureId = Guid.NewGuid(),
            Modules = source.Modules
                .Select(module => module.Module.Key == GenericProcedureCatalog.MovementResolutionModule
                    ? futureMovement
                    : module)
                .ToArray()
        };
        future.Validate();
        return future;
    }

    private static async Task UpdateJsonAsync<T>(
        string connectionString,
        Guid expeditionId,
        string column,
        T value)
    {
        if (column is not ("procedure_json" or "campaign_procedure_json"))
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE expeditions SET {column} = @json WHERE id = @id;";
        command.Parameters.AddWithValue("json", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(value, JsonOptions));
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, expeditionId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static CreateOverworldCommand WorldCommand() => new(
        "World",
        HexOrientation.PointyTop,
        new WorldPoint(0, 0),
        0,
        1,
        12,
        DistanceUnit.Miles);
}
