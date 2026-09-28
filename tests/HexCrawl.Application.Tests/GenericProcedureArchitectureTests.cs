using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class GenericProcedureArchitectureTests
{
    [Fact]
    public void BuiltInPresetsMaterializeGenericCampaignOwnedSnapshots()
    {
        foreach (var preset in CrawlProcedureCatalog.All)
        {
            preset.Validate();

            var materialized = preset.MaterializeGeneric();
            var compatibility = CampaignProcedureCompatibilityProjector.Project(materialized.Procedure);

            Assert.Equal(preset.Materialize(), compatibility);
            Assert.Equal(1, materialized.Procedure.Revision);
            Assert.NotEqual(Guid.Empty, materialized.Procedure.ProcedureId);
            Assert.Equal(preset.Origin, materialized.Origin);
            Assert.NotEmpty(materialized.Procedure.Modules);
            Assert.All(materialized.Procedure.Modules, module =>
            {
                Assert.NotEmpty(module.Module.Key);
                Assert.NotEmpty(module.Mechanic.Key);
                Assert.True(module.Mechanic.Version > 0);
            });
        }
    }

    [Fact]
    public void RuntimeProjectionDoesNotRequirePresetOriginIdentity()
    {
        var materialized = CrawlProcedureCatalog.Resolve("alexandrian-advanced").MaterializeGeneric();
        var snapshot = materialized.Procedure with
        {
            Key = "campaign-owned-procedure",
            Name = "Campaign owned procedure"
        };

        var projected = CampaignProcedureCompatibilityProjector.Project(snapshot);

        Assert.Equal("campaign-owned-procedure", projected.Key);
        Assert.Equal("Campaign owned procedure", projected.Name);
        Assert.Equal(materialized.CompatibilityProfile.WatchLength, projected.WatchLength);
        Assert.Equal(materialized.CompatibilityProfile.TravelResolution, projected.TravelResolution);
        Assert.Equal(materialized.CompatibilityProfile.ResolutionHelpers, projected.ResolutionHelpers);
        Assert.Null(typeof(CampaignProcedure).GetProperty("PresetKey"));
    }

    [Fact]
    public void GenericOverrideCreatesNewRevisionWithoutMutatingPriorRevision()
    {
        var first = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric().Procedure;
        var sixHours = TimeSpan.FromHours(6).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var change = new CampaignProcedureOverride(
            "longer-travel-interval",
            GenericProcedureCatalog.TimeIntervalModule,
            null,
            null,
            new Dictionary<string, string>
            {
                ["durationTicks"] = sixHours
            },
            "Campaign-specific travel interval.");

        var second = CampaignProcedureMaterializer.CreateRevision(first, [change]);

        Assert.Equal(first.ProcedureId, second.ProcedureId);
        Assert.Equal(1, first.Revision);
        Assert.Equal(2, second.Revision);
        Assert.Equal(TimeSpan.FromHours(4), CampaignProcedureCompatibilityProjector.Project(first).WatchLength);
        Assert.Equal(TimeSpan.FromHours(6), CampaignProcedureCompatibilityProjector.Project(second).WatchLength);
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
    public async Task CampaignProcedureRevisionsRoundTripIndependentlyOfPresetCatalog()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new CampaignProcedureService(store);

        var first = await service.CreateFromPresetAsync("alice", "simple-fixed-distance", Guid.NewGuid());
        var change = new CampaignProcedureOverride(
            "six-hour-watch",
            GenericProcedureCatalog.TimeIntervalModule,
            null,
            null,
            new Dictionary<string, string>
            {
                ["durationTicks"] = TimeSpan.FromHours(6).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        var second = await service.CreateRevisionAsync("alice", first.ProcedureId, 1, [change]);

        var storedFirst = await service.GetAsync("alice", first.ProcedureId, 1);
        var latest = await service.GetLatestAsync("alice", first.ProcedureId);
        var history = await store.ListCampaignProcedureRevisionsAsync(first.ProcedureId, "alice");

        Assert.Equal(1, storedFirst.Revision);
        Assert.Equal(2, latest.Revision);
        Assert.Equal(2, history.Count);
        Assert.Equal(TimeSpan.FromHours(4), CampaignProcedureCompatibilityProjector.Project(storedFirst.Procedure).WatchLength);
        Assert.Equal(TimeSpan.FromHours(6), CampaignProcedureCompatibilityProjector.Project(latest.Procedure).WatchLength);
        Assert.Equal(first.ProcedureOrigin, storedFirst.ProcedureOrigin);
        Assert.Equal(first.ProcedureOrigin, latest.ProcedureOrigin);
    }

    [Fact]
    public async Task UnknownFutureMechanicSnapshotIsPreservedEvenWhenCurrentRuntimeCanNotProjectIt()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var materialized = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric();
        var movement = materialized.Procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule);
        var futureMechanicKey = "future-movement-resolution";
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
        var future = materialized.Procedure with
        {
            ProcedureId = Guid.NewGuid(),
            Modules = materialized.Procedure.Modules
                .Select(module => module.Module.Key == GenericProcedureCatalog.MovementResolutionModule
                    ? futureMovement
                    : module)
                .ToArray()
        };
        future.Validate();

        await store.CreateCampaignProcedureRevisionAsync(new StoredCampaignProcedureRevision(
            future,
            "alice",
            null,
            null,
            DateTimeOffset.UtcNow));
        var loaded = await store.GetCampaignProcedureRevisionAsync(future.ProcedureId, 1, "alice");

        Assert.NotNull(loaded);
        var loadedMovement = loaded!.Procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule);
        Assert.Equal(futureMechanicKey, loadedMovement.Mechanic.Key);
        Assert.Equal(42, loadedMovement.Mechanic.Version);
        Assert.Equal("future.runtime.handler", loadedMovement.Mechanic.ExecutionHandler);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CampaignProcedureCompatibilityProjector.Project(loaded.Procedure));
        Assert.Contains("can not be projected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NewCrawlSessionPinsFullGenericSnapshotAlongsideCompatibilityProfile()
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

        Assert.NotNull(started.CampaignProcedure);
        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.CampaignProcedure);
        Assert.Equal(started.CampaignProcedure!.ProcedureId, loaded.CampaignProcedure!.ProcedureId);
        Assert.Equal(started.Procedure, CampaignProcedureCompatibilityProjector.Project(loaded.CampaignProcedure));
        Assert.Equal("simple-fixed-distance", loaded.ProcedureOrigin?.PresetKey);
    }
}
