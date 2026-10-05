using System.Globalization;
using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class ProcedureComposerServiceTests
{
    [Fact]
    public async Task PresetDraftUsesMaterializedGenericProcedureAndOriginMetadata()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = Composer(store);

        var draft = await service.CreateDraftAsync("alice", CrawlProcedureCatalog.Dnd2024PresetKey, null, null, []);

        Assert.Equal(CrawlProcedureCatalog.Dnd2024PresetKey, draft.Origin?.PresetKey);
        Assert.Equal(1, draft.Procedure.Revision);
        Assert.Contains(draft.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.TerrainMovementModule
            && module.Parameters["terrainAdjustments"].Contains(
                "arctic=fast-if-appropriately-equipped",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task CustomDraftHasNoOriginAndExplicitSelectionsExposeGenericDependencyDiagnostics()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = Composer(store);

        var draft = await service.CreateDraftAsync(
            "alice",
            null,
            null,
            null,
            [new ProcedureModuleSelection(GenericProcedureCatalog.NavigationOutcomeModule, true)],
            []);

        Assert.Null(draft.Origin);
        Assert.DoesNotContain(draft.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);
        Assert.Contains(draft.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.NavigationOutcomeModule);
        Assert.DoesNotContain(draft.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.JourneyProcessModule);

        var unresolved = Assert.Single(draft.Dependencies.Issues, issue =>
            issue.Kind == ProcedureDependencyIssueKind.UnresolvedInput
            && issue.InputKey == "navigation.check-result");
        Assert.True((unresolved.AllowedInputSources & ProcedureInputSource.SelectedModule) != 0);
        Assert.True((unresolved.AllowedInputSources & ProcedureInputSource.Dm) != 0);
        Assert.True((unresolved.AllowedInputSources & ProcedureInputSource.OptionalProvider) != 0);
        Assert.True((unresolved.AllowedInputSources & ProcedureInputSource.ExternalState) != 0);
    }

    [Fact]
    public async Task DraftBehaviorReplacementUsesGenericAlternativeAndRefreshesDependencies()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = Composer(store);
        var original = await service.CreateDraftAsync(
            "alice",
            CrawlProcedureCatalog.OneRing2ePresetKey,
            null,
            null,
            []);

        var movement = original.Procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementBudgetModule);
        Assert.Equal(GenericProcedureCatalog.JourneyProgressBudgetMechanic, movement.Mechanic.Key);

        var change = new CampaignProcedureOverride(
            "composer-movement-budget",
            GenericProcedureCatalog.MovementBudgetModule,
            GenericProcedureCatalog.MovementBudgetMechanic,
            1,
            new Dictionary<string, string>
            {
                ["budgetModel"] = "fixed-per-interval",
                ["baseBudget"] = "1",
                ["budgetUnit"] = "interval",
                ["limitingScope"] = "party"
            });

        var changed = await service.CreateDraftAsync(
            "alice",
            CrawlProcedureCatalog.OneRing2ePresetKey,
            null,
            null,
            [change]);

        Assert.Equal(
            GenericProcedureCatalog.MovementBudgetMechanic,
            changed.Procedure.Modules.Single(module =>
                module.Module.Key == GenericProcedureCatalog.MovementBudgetModule).Mechanic.Key);
        Assert.Contains(changed.Dependencies.Issues, issue =>
            issue.Kind == ProcedureDependencyIssueKind.MissingRequiredProducer
            && issue.InputKey == "time.interval-duration");
        Assert.Single(changed.Procedure.Overrides);
    }

    [Fact]
    public async Task StructuredMapParameterRoundTripsThroughComposerRevision()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = Composer(store);

        var created = await service.CreateAsync(
            "alice",
            CrawlProcedureCatalog.Dnd2024PresetKey,
            []);
        const string terrainMap = "arctic=fast-if-appropriately-equipped;forest=slow;road=normal";
        var change = new CampaignProcedureOverride(
            "terrain-map-change",
            GenericProcedureCatalog.TerrainMovementModule,
            null,
            null,
            new Dictionary<string, string> { ["terrainAdjustments"] = terrainMap });

        var revised = await service.CreateRevisionAsync(
            "alice",
            created.ProcedureId,
            created.Revision,
            [change]);
        var reloaded = await service.GetAsync("alice", revised.ProcedureId, revised.Revision);

        Assert.Equal(
            terrainMap,
            reloaded.Procedure.Modules.Single(module =>
                module.Module.Key == GenericProcedureCatalog.TerrainMovementModule)
                .Parameters["terrainAdjustments"]);
    }

    [Fact]
    public async Task SavingRevisionLeavesPreviousRevisionAndPinnedExpeditionUnchanged()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var composer = Composer(store);
        var sessions = new CrawlSessionService(store);

        var pinned = await sessions.StartAsync(
            "alice",
            new StartStandaloneCrawlSessionCommand(
                "Pinned expedition",
                "simple-fixed-distance",
                new NonSpatialCrawlSessionContext("Procedure clock")));
        var originalPinned = pinned.CampaignProcedure;

        var created = await composer.CreateAsync("alice", "simple-fixed-distance", []);
        var sixHours = TimeSpan.FromHours(6).Ticks.ToString(CultureInfo.InvariantCulture);
        var change = new CampaignProcedureOverride(
            "six-hour-watch",
            GenericProcedureCatalog.TimeIntervalModule,
            null,
            null,
            new Dictionary<string, string> { ["durationTicks"] = sixHours });
        var revised = await composer.CreateRevisionAsync(
            "alice",
            created.ProcedureId,
            1,
            [change]);

        var first = await composer.GetAsync("alice", created.ProcedureId, 1);
        var latest = await composer.GetAsync("alice", created.ProcedureId);
        var reloadedExpedition = await store.GetExpeditionAsync(pinned.Id, "alice");

        Assert.Equal(1, first.Revision);
        Assert.Equal(2, revised.Revision);
        Assert.Equal(2, latest.Revision);
        Assert.Equal(
            TimeSpan.FromHours(4).Ticks.ToString(CultureInfo.InvariantCulture),
            first.Procedure.Modules.Single(module =>
                module.Module.Key == GenericProcedureCatalog.TimeIntervalModule)
                .Parameters["durationTicks"]);
        Assert.Equal(sixHours, latest.Procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.TimeIntervalModule)
            .Parameters["durationTicks"]);
        Assert.NotNull(reloadedExpedition);
        Assert.Equal(originalPinned, reloadedExpedition!.CampaignProcedure);
    }

    [Fact]
    public async Task NoOpRevisionIsRejectedWithoutCreatingRevisionHistory()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var composer = Composer(store);
        var created = await composer.CreateAsync("alice", "simple-fixed-distance", []);
        var time = created.Procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);
        var noOp = new CampaignProcedureOverride(
            "same-duration",
            GenericProcedureCatalog.TimeIntervalModule,
            null,
            null,
            new Dictionary<string, string>
            {
                ["durationTicks"] = time.Parameters["durationTicks"]
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            composer.CreateRevisionAsync("alice", created.ProcedureId, created.Revision, [noOp]));

        Assert.Single(await composer.ListRevisionsAsync("alice", created.ProcedureId));
    }

    [Fact]
    public async Task UnsupportedStoredMechanicIsPreservedWhenAnotherModuleChanges()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var composer = Composer(store);
        var materialized = CrawlProcedureCatalog.Resolve("simple-fixed-distance").MaterializeGeneric();
        var movement = materialized.Procedure.Modules.Single(module =>
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
        var future = materialized.Procedure with
        {
            ProcedureId = Guid.NewGuid(),
            Modules = materialized.Procedure.Modules
                .Select(module =>
                    module.Module.Key == GenericProcedureCatalog.MovementResolutionModule
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

        var change = new CampaignProcedureOverride(
            "time-change",
            GenericProcedureCatalog.TimeIntervalModule,
            null,
            null,
            new Dictionary<string, string>
            {
                ["durationTicks"] = TimeSpan.FromHours(5).Ticks.ToString(CultureInfo.InvariantCulture)
            });
        var revised = await composer.CreateRevisionAsync("alice", future.ProcedureId, 1, [change]);

        var preserved = revised.Procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.MovementResolutionModule);
        Assert.Equal(futureMechanicKey, preserved.Mechanic.Key);
        Assert.Equal(42, preserved.Mechanic.Version);
        Assert.Equal("future.runtime.handler", preserved.Mechanic.ExecutionHandler);
    }

    private static ProcedureComposerService Composer(IHexCrawlStore store) =>
        new(new CampaignProcedureService(store));
}
