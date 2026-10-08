using System.Globalization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class GenericProcedureArchitectureTests
{
    [Fact]
    public void BuiltInPresetsMaterializeCampaignOwnedSnapshots()
    {
        foreach (var preset in CrawlProcedureCatalog.All)
        {
            preset.Validate();
            var materialized = preset.MaterializeGeneric();

            Assert.Equal(1, materialized.Procedure.Revision);
            Assert.NotEqual(Guid.Empty, materialized.Procedure.ProcedureId);
            Assert.Equal(CampaignProcedureSchema.CurrentVersion, materialized.Procedure.SchemaVersion);
            Assert.Equal(CampaignProcedureSchema.CurrentHexTilingGjhNotation, preset.Recipe.TilingGjhNotation);
            Assert.Equal(CampaignProcedureSchema.CurrentHexTilingGjhNotation, materialized.Procedure.TilingGjhNotation);
            Assert.Equal(preset.Origin, materialized.Origin);
            Assert.NotEmpty(materialized.Procedure.Modules);
            Assert.All(materialized.Procedure.Modules, module =>
            {
                Assert.NotEmpty(module.Module.Key);
                Assert.NotEmpty(module.Mechanic.Key);
                Assert.True(module.Mechanic.Version > 0);
                Assert.NotEmpty(module.Mechanic.ExecutionHandler);
            });
        }
    }

    [Fact]
    public void RuntimeBindingDoesNotDependOnPresetIdentity()
    {
        var procedure = CrawlProcedureCatalog.Resolve("alexandrian-advanced").MaterializeGeneric().Procedure;
        var renamed = procedure with
        {
            Key = "campaign-owned-procedure",
            Name = "Campaign owned procedure"
        };

        var originalRuntime = GenericProcedureRuntime.Bind(procedure);
        var renamedRuntime = GenericProcedureRuntime.Bind(renamed);

        Assert.Equal(originalRuntime.Time, renamedRuntime.Time);
        Assert.Equal(originalRuntime.Movement, renamedRuntime.Movement);
        Assert.Equal(originalRuntime.HexProgress, renamedRuntime.HexProgress);
        Assert.Equal(originalRuntime.Navigation, renamedRuntime.Navigation);
        Assert.Equal(originalRuntime.Encounters, renamedRuntime.Encounters);
        Assert.Equal(originalRuntime.ResolutionHelpers, renamedRuntime.ResolutionHelpers);
        Assert.Null(typeof(CampaignProcedure).GetProperty("PresetKey"));
    }

    [Fact]
    public void GenericOverrideCreatesNewRevisionWithoutMutatingPriorRevision()
    {
        var first = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;
        var sixHours = TimeSpan.FromHours(6).Ticks.ToString(CultureInfo.InvariantCulture);
        var change = new CampaignProcedureOverride(
            "longer-travel-interval",
            GenericProcedureCatalog.TimeIntervalModule,
            null,
            null,
            new Dictionary<string, string> { ["durationTicks"] = sixHours },
            "Campaign-specific travel interval.");

        var second = CampaignProcedureMaterializer.CreateRevision(first, [change]);

        Assert.Equal(first.ProcedureId, second.ProcedureId);
        Assert.Equal(1, first.Revision);
        Assert.Equal(2, second.Revision);
        Assert.Equal(TimeSpan.FromHours(4).Ticks.ToString(CultureInfo.InvariantCulture), Parameter(first, GenericProcedureCatalog.TimeIntervalModule, "durationTicks"));
        Assert.Equal(sixHours, Parameter(second, GenericProcedureCatalog.TimeIntervalModule, "durationTicks"));
        Assert.Single(second.Overrides);
    }

    [Fact]
    public void DependencyReportIdentifiesMissingRequiredModules()
    {
        var procedure = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;
        var withoutTime = procedure with
        {
            Modules = procedure.Modules
                .Where(module => module.Module.Key != GenericProcedureCatalog.TimeIntervalModule)
                .ToArray()
        };

        var report = withoutTime.EvaluateDependencies();

        Assert.True(report.HasErrors);
        Assert.Contains(report.Issues, issue =>
            issue.Kind == ProcedureDependencyIssueKind.MissingRequiredModule
            && issue.ModuleKey == GenericProcedureCatalog.MovementResolutionModule);
        Assert.Contains(report.Issues, issue =>
            issue.Kind == ProcedureDependencyIssueKind.MissingRequiredModule
            && issue.ModuleKey == GenericProcedureCatalog.EncounterCadenceModule);
    }

    [Fact]
    public async Task CampaignProcedureRevisionsRoundTripWithoutPresetCatalogReconstruction()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new CampaignProcedureService(store);

        var first = await service.CreateFromPresetAsync("alice", "simple-fixed-distance", Guid.NewGuid());
        var sixHours = TimeSpan.FromHours(6).Ticks.ToString(CultureInfo.InvariantCulture);
        var change = new CampaignProcedureOverride(
            "six-hour-watch",
            GenericProcedureCatalog.TimeIntervalModule,
            null,
            null,
            new Dictionary<string, string> { ["durationTicks"] = sixHours });
        var second = await service.CreateRevisionAsync("alice", first.ProcedureId, 1, [change]);

        var storedFirst = await service.GetAsync("alice", first.ProcedureId, 1);
        var latest = await service.GetLatestAsync("alice", first.ProcedureId);
        var history = await store.ListCampaignProcedureRevisionsAsync(first.ProcedureId, "alice");

        Assert.Equal(1, storedFirst.Revision);
        Assert.Equal(2, latest.Revision);
        Assert.Equal(2, history.Count);
        Assert.Equal(TimeSpan.FromHours(4).Ticks.ToString(CultureInfo.InvariantCulture), Parameter(storedFirst.Procedure, GenericProcedureCatalog.TimeIntervalModule, "durationTicks"));
        Assert.Equal(sixHours, Parameter(latest.Procedure, GenericProcedureCatalog.TimeIntervalModule, "durationTicks"));
        Assert.Equal(first.ProcedureOrigin, storedFirst.ProcedureOrigin);
        Assert.Equal(first.ProcedureOrigin, latest.ProcedureOrigin);
    }

    [Fact]
    public async Task UnknownFutureMechanicSnapshotIsPreservedAndRejectedByRuntimeBinding()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var materialized = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric();
        var movement = materialized.Procedure.Modules.Single(module => module.Module.Key == GenericProcedureCatalog.MovementResolutionModule);
        const string futureMechanicKey = "future-movement-resolution";
        var futureMovement = movement with
        {
            Module = movement.Module with
            {
                CompatibleMechanicTypes = movement.Module.CompatibleMechanicTypes.Concat([futureMechanicKey]).ToArray()
            },
            Mechanic = movement.Mechanic with
            {
                Key = futureMechanicKey,
                DisplayName = "Future movement resolution",
                ExecutionHandler = "future.runtime.handler",
                Version = 42
            }
        };
        var future = materialized.Procedure with
        {
            ProcedureId = Guid.NewGuid(),
            Modules = materialized.Procedure.Modules
                .Select(module => module.Module.Key == GenericProcedureCatalog.MovementResolutionModule ? futureMovement : module)
                .ToArray()
        };
        future.Validate();

        await store.CreateCampaignProcedureRevisionAsync(new StoredCampaignProcedureRevision(
            future, "alice", null, null, DateTimeOffset.UtcNow));
        var loaded = await store.GetCampaignProcedureRevisionAsync(future.ProcedureId, 1, "alice");

        Assert.NotNull(loaded);
        var loadedMovement = loaded!.Procedure.Modules.Single(module => module.Module.Key == GenericProcedureCatalog.MovementResolutionModule);
        Assert.Equal(futureMechanicKey, loadedMovement.Mechanic.Key);
        Assert.Equal(42, loadedMovement.Mechanic.Version);
        Assert.Equal("future.runtime.handler", loadedMovement.Mechanic.ExecutionHandler);
        Assert.Throws<UnsupportedProcedureMechanicException>(() => GenericProcedureRuntime.Bind(loaded.Procedure));
    }

    [Fact]
    public async Task NewCrawlSessionPinsExactCampaignProcedureSnapshot()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new CrawlSessionService(store);

        var started = await service.StartAsync(
            "alice",
            new StartStandaloneCrawlSessionCommand(
                "Pinned snapshot",
                "simple-fixed-distance",
                new NonSpatialCrawlSessionContext("Travel clock")));
        var loaded = await store.GetExpeditionAsync(started.Id, "alice");

        Assert.NotNull(loaded);
        Assert.Equal(started.CampaignProcedure, loaded!.CampaignProcedure);
        Assert.Equal("simple-fixed-distance", loaded.ProcedureOrigin?.PresetKey);
    }

    private static string Parameter(CampaignProcedure procedure, string moduleKey, string parameterKey) =>
        procedure.Modules.Single(module => module.Module.Key == moduleKey).Parameters[parameterKey];
}
