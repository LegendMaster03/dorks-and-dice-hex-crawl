using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class ProcedureComposerStructuralCompositionTests
{
    [Fact]
    public async Task CustomDraftStartsNeutralUntilTheDmChoosesProcedureStructure()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = Composer(store);

        var draft = await service.CreateDraftAsync("alice", null, null, null, []);

        Assert.Null(draft.Origin);
        Assert.Empty(draft.Procedure.Modules);
        Assert.Empty(draft.Procedure.Overrides);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync("alice", null, [], []));
    }

    [Fact]
    public async Task StructuredAuthoringCanNameAndRenameProcedure()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = Composer(store);
        ProcedureModuleSelection[] selections =
        [
            new(GenericProcedureCatalog.TimeIntervalModule, true)
        ];

        var created = await service.CreateAsync(
            "alice",
            null,
            selections,
            [],
            cancellationToken: default,
            name: "North March procedure");
        Assert.Equal("North March procedure", created.Procedure.Name);

        var renamed = await service.CreateRevisionAsync(
            "alice",
            created.ProcedureId,
            created.Revision,
            [],
            [],
            cancellationToken: default,
            name: "Winter North March");
        Assert.Equal(2, renamed.Revision);
        Assert.Equal("Winter North March", renamed.Procedure.Name);

        var original = await service.GetAsync("alice", created.ProcedureId, 1);
        Assert.Equal("North March procedure", original.Procedure.Name);
    }

    [Fact]
    public async Task ModuleSelectionsCanAuthorAndReloadNoIntervalJourneyProcedure()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = Composer(store);
        ProcedureModuleSelection[] selections =
        [
            new(GenericProcedureCatalog.TimeIntervalModule, false),
            new(GenericProcedureCatalog.MovementBudgetModule, true),
            new(GenericProcedureCatalog.TerrainMovementModule, true),
            new(GenericProcedureCatalog.PartyActivitiesModule, true),
            new(GenericProcedureCatalog.JourneyProcessModule, true)
        ];
        CampaignProcedureOverride[] overrides =
        [
            new(
                "journey-progress-budget",
                GenericProcedureCatalog.MovementBudgetModule,
                GenericProcedureCatalog.JourneyProgressBudgetMechanic,
                1,
                new Dictionary<string, string>
                {
                    ["budgetModel"] = "journey-progress",
                    ["baseBudget"] = "1",
                    ["budgetUnit"] = "journey-leg",
                    ["limitingScope"] = "party"
                })
        ];

        var draft = await service.CreateDraftAsync("alice", null, null, null, selections, overrides);

        Assert.DoesNotContain(draft.Dependencies.Issues, issue => issue.Kind is
            ProcedureDependencyIssueKind.MissingRequiredModule
            or ProcedureDependencyIssueKind.MissingRequiredProducer);
        Assert.DoesNotContain(draft.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);
        Assert.Contains(draft.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.JourneyProcessModule);
        Assert.Equal(
            GenericProcedureCatalog.JourneyProgressBudgetMechanic,
            draft.Procedure.Modules.Single(module =>
                module.Module.Key == GenericProcedureCatalog.MovementBudgetModule).Mechanic.Key);

        var stored = await service.CreateAsync("alice", null, selections, overrides);
        var reloaded = await service.GetAsync("alice", stored.ProcedureId);

        Assert.Null(reloaded.ProcedureOrigin);
        Assert.DoesNotContain(reloaded.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);
        Assert.Contains(reloaded.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.JourneyProcessModule);
        Assert.Contains(reloaded.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.PartyActivitiesModule);
        Assert.Contains(reloaded.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.TerrainMovementModule);
    }

    [Fact]
    public async Task CustomProcedureCanAddAndPersistSurvivalExposure()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = Composer(store);
        ProcedureModuleSelection[] selections =
        [
            new(Phase11GenericProcedureCatalog.ExposureModule, true)
        ];

        var draft = await service.CreateDraftAsync("alice", null, null, null, selections, []);
        var exposure = Assert.Single(draft.Procedure.Modules);
        Assert.Equal(Phase11GenericProcedureCatalog.ExposureModule, exposure.Module.Key);
        Assert.Equal("resolved-check", exposure.Parameters["evaluationModel"]);
        Assert.Equal("party", exposure.Parameters["targetScope"]);

        var stored = await service.CreateAsync("alice", null, selections, []);
        var reloaded = await service.GetAsync("alice", stored.ProcedureId);
        Assert.Contains(reloaded.Procedure.Modules, module =>
            module.Module.Key == Phase11GenericProcedureCatalog.ExposureModule);
    }

    [Fact]
    public async Task SavedProcedureCanReAddPreviouslyOmittedGenericModule()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = Composer(store);
        var created = await service.CreateAsync("alice", null, [
            new ProcedureModuleSelection(GenericProcedureCatalog.TimeIntervalModule, true)
        ], []);

        Assert.Contains(created.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);
        Assert.DoesNotContain(created.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.EncounterCadenceModule);

        var revised = await service.CreateRevisionAsync(
            "alice",
            created.ProcedureId,
            created.Revision,
            [new ProcedureModuleSelection(GenericProcedureCatalog.EncounterCadenceModule, true)],
            []);

        Assert.Contains(revised.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.EncounterCadenceModule);
    }

    [Fact]
    public async Task RemovedCustomizedModuleDoesNotLeakOldOverrideIntoLaterReAdd()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = Composer(store);
        var created = await service.CreateAsync("alice", null, [
            new ProcedureModuleSelection(GenericProcedureCatalog.TimeIntervalModule, true)
        ], []);
        var customized = await service.CreateRevisionAsync(
            "alice",
            created.ProcedureId,
            created.Revision,
            [new ProcedureModuleSelection(GenericProcedureCatalog.EncounterCadenceModule, true)],
            [new CampaignProcedureOverride(
                "per-watch-encounters",
                GenericProcedureCatalog.EncounterCadenceModule,
                null,
                null,
                new Dictionary<string, string> { ["cadence"] = "PerWatch" })]);

        Assert.Contains(customized.Procedure.Overrides, value =>
            value.ModuleKey == GenericProcedureCatalog.EncounterCadenceModule);

        var removed = await service.CreateRevisionAsync(
            "alice",
            customized.ProcedureId,
            customized.Revision,
            [new ProcedureModuleSelection(GenericProcedureCatalog.EncounterCadenceModule, false)],
            []);

        Assert.DoesNotContain(removed.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.EncounterCadenceModule);
        Assert.DoesNotContain(removed.Procedure.Overrides, value =>
            value.ModuleKey == GenericProcedureCatalog.EncounterCadenceModule);

        var reAdded = await service.CreateRevisionAsync(
            "alice",
            removed.ProcedureId,
            removed.Revision,
            [new ProcedureModuleSelection(GenericProcedureCatalog.EncounterCadenceModule, true)],
            []);
        var cadence = reAdded.Procedure.Modules.Single(module =>
            module.Module.Key == GenericProcedureCatalog.EncounterCadenceModule);

        Assert.Equal("None", cadence.Parameters["cadence"]);
        Assert.DoesNotContain(reAdded.Procedure.Overrides, value =>
            value.ModuleKey == GenericProcedureCatalog.EncounterCadenceModule);
    }

    [Fact]
    public async Task RemovingRequiredProducerRemainsDraftableButCanNotBeSaved()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = Composer(store);
        ProcedureModuleSelection[] selections =
        [
            new(GenericProcedureCatalog.MovementResolutionModule, true),
            new(GenericProcedureCatalog.TimeIntervalModule, false)
        ];

        var draft = await service.CreateDraftAsync("alice", null, null, null, selections, []);

        Assert.True(draft.Dependencies.HasErrors);
        Assert.Contains(draft.Dependencies.Issues, issue =>
            issue.Kind == ProcedureDependencyIssueKind.MissingRequiredModule
            && issue.ModuleKey == GenericProcedureCatalog.MovementResolutionModule);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync("alice", null, selections, []));
    }

    private static ProcedureComposerService Composer(IHexCrawlStore store) =>
        new(new CampaignProcedureService(store));
}