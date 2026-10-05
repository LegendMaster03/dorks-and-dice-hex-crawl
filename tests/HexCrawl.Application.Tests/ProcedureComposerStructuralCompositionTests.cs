using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class ProcedureComposerStructuralCompositionTests
{
    [Fact]
    public async Task CustomDraftStartsFromSmallGenericShapeInsteadOfEveryCatalogSubsystem()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = Composer(store);

        var draft = await service.CreateDraftAsync("alice", null, null, null, []);

        Assert.Null(draft.Origin);
        var only = Assert.Single(draft.Procedure.Modules);
        Assert.Equal(GenericProcedureCatalog.TimeIntervalModule, only.Module.Key);
        Assert.DoesNotContain(draft.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.ResourceConsumptionModule);
        Assert.DoesNotContain(draft.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.JourneyProcessModule);
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
            new(GenericProcedureCatalog.JourneyProcessModule, true)
        ];

        var draft = await service.CreateDraftAsync("alice", null, null, null, selections, []);

        Assert.DoesNotContain(draft.Dependencies.Issues, issue => issue.Kind is
            ProcedureDependencyIssueKind.MissingRequiredModule
            or ProcedureDependencyIssueKind.MissingRequiredProducer);
        Assert.DoesNotContain(draft.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);
        Assert.Contains(draft.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.JourneyProcessModule);

        var stored = await service.CreateAsync("alice", null, selections, []);
        var reloaded = await service.GetAsync("alice", stored.ProcedureId);

        Assert.Null(reloaded.ProcedureOrigin);
        Assert.DoesNotContain(reloaded.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.TimeIntervalModule);
        Assert.Contains(reloaded.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.JourneyProcessModule);
    }

    [Fact]
    public async Task SavedProcedureCanReAddPreviouslyOmittedGenericModule()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = Composer(store);
        var created = await service.CreateAsync(
            "alice",
            null,
            [new ProcedureModuleSelection(GenericProcedureCatalog.JourneyProcessModule, true)],
            []);

        var revised = await service.CreateRevisionAsync(
            "alice",
            created.ProcedureId,
            created.Revision,
            [new ProcedureModuleSelection(GenericProcedureCatalog.ResourceConsumptionModule, true)],
            []);

        Assert.Contains(revised.Procedure.Modules, module =>
            module.Module.Key == GenericProcedureCatalog.ResourceConsumptionModule);
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