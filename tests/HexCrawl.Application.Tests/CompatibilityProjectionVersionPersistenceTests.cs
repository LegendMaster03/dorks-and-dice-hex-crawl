using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class CompatibilityProjectionVersionPersistenceTests
{
    [Fact]
    public void TryProjectRejectsFutureVersionBeforeParsingCurrentParameterSemantics()
    {
        var original = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;
        var movement = original.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule);
        var futureMovement = movement with
        {
            Mechanic = movement.Mechanic with { Version = 99 },
            Parameters = movement.Parameters
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
        };
        Assert.IsType<Dictionary<string, string>>(futureMovement.Parameters)["travelResolution"] = "FutureDistanceMode";
        var future = original with
        {
            Modules = original.Modules
                .Select(module => module.Module.Key == GenericProcedureCatalog.MovementResolutionModule
                    ? futureMovement
                    : module)
                .ToArray()
        };
        future.Validate();

        Assert.False(CampaignProcedureCompatibilityProjector.TryProject(future, out _));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CampaignProcedureCompatibilityProjector.Project(future));
        Assert.Contains("version 99", exception.Message, StringComparison.Ordinal);
        Assert.Contains("compatibility projector", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TravelResolutionMode", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KnownHandlerFutureVersionPersistsWithoutUnsafeCompatibilityProjection()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new CrawlSessionService(store);
        var started = await service.StartAsync(
            "alice",
            new StartStandaloneCrawlSessionCommand(
                "Future version source",
                "simple-fixed-distance",
                new NonSpatialCrawlSessionContext("Travel clock")));

        var originalMovement = started.CampaignProcedure!.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule);
        var originalHandler = originalMovement.Mechanic.ExecutionHandler;
        var futureProcedure = WithFutureMovementVersion(started.CampaignProcedure, 99);
        var futureExpedition = DuplicateStandalone(started, "Future version snapshot") with
        {
            CampaignProcedure = futureProcedure
        };

        Assert.False(CampaignProcedureCompatibilityProjector.TryProject(futureProcedure, out _));
        var projectionException = Assert.Throws<InvalidOperationException>(() =>
            CampaignProcedureCompatibilityProjector.Project(futureProcedure));
        Assert.Contains(originalHandler, projectionException.Message, StringComparison.Ordinal);
        Assert.Contains("version 99", projectionException.Message, StringComparison.Ordinal);
        Assert.Contains("compatibility projector", projectionException.Message, StringComparison.OrdinalIgnoreCase);
        AssertFutureMovement(futureProcedure, originalHandler, 99);
        Assert.Equal(started.Procedure, futureExpedition.Procedure);

        var created = await store.CreateExpeditionAsync(futureExpedition);
        var loaded = await store.GetExpeditionAsync(created.Id, "alice");

        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.CampaignProcedure);
        Assert.Equal(started.Procedure, loaded.Procedure);
        AssertFutureMovement(loaded.CampaignProcedure!, originalHandler, 99);
        Assert.False(CampaignProcedureCompatibilityProjector.TryProject(loaded.CampaignProcedure!, out _));

        var reloadedProjectionException = Assert.Throws<InvalidOperationException>(() =>
            CampaignProcedureCompatibilityProjector.Project(loaded.CampaignProcedure!));
        Assert.Contains(originalHandler, reloadedProjectionException.Message, StringComparison.Ordinal);
        Assert.Contains("version 99", reloadedProjectionException.Message, StringComparison.Ordinal);

        var nativeException = Assert.Throws<UnsupportedProcedureMechanicException>(() =>
            GenericProcedureRuntime.Bind(loaded.CampaignProcedure!));
        Assert.Equal(originalHandler, nativeException.ExecutionHandler);
        Assert.Equal(99, nativeException.MechanicVersion);
        Assert.Contains(originalHandler, nativeException.Message, StringComparison.Ordinal);
        Assert.Contains("version 99", nativeException.Message, StringComparison.Ordinal);

        AssertFutureMovement(loaded.CampaignProcedure!, originalHandler, 99);
        Assert.Equal(started.Procedure, loaded.Procedure);
    }

    private static CampaignProcedure WithFutureMovementVersion(CampaignProcedure source, int version)
    {
        var movement = source.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule);
        var futureMovement = movement with
        {
            Mechanic = movement.Mechanic with { Version = version }
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

    private static void AssertFutureMovement(
        CampaignProcedure procedure,
        string expectedHandler,
        int expectedVersion)
    {
        var movement = procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule);
        Assert.Equal(GenericProcedureCatalog.MovementResolutionPolicyMechanic, movement.Mechanic.Key);
        Assert.Equal(expectedHandler, movement.Mechanic.ExecutionHandler);
        Assert.Equal(expectedVersion, movement.Mechanic.Version);
    }
}
