using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class ProcedurePresetIdentityTests
{
    [Fact]
    public void CatalogRegistersAllIndividualPresetsInStableOrder()
    {
        string[] expected =
        [
            "alexandrian-advanced",
            "simple-fixed-distance",
            "simple-hex-step",
            CrawlProcedureCatalog.BxPresetKey,
            CrawlProcedureCatalog.OseClassicPresetKey,
            CrawlProcedureCatalog.Adnd2ePresetKey,
            CrawlProcedureCatalog.Dnd35PresetKey,
            CrawlProcedureCatalog.Dnd2014PresetKey,
            CrawlProcedureCatalog.Dnd2024PresetKey,
            CrawlProcedureCatalog.Pathfinder2eHexplorationPresetKey,
            CrawlProcedureCatalog.ForbiddenLandsPresetKey,
            CrawlProcedureCatalog.WorldsWithoutNumberPresetKey,
            CrawlProcedureCatalog.OneRing2ePresetKey
        ];

        var actual = CrawlProcedureCatalog.Catalog.Select(preset => preset.PresetKey).ToArray();
        Assert.Equal(expected, actual);
        Assert.Equal(actual.Length, actual.Distinct(StringComparer.Ordinal).Count());

        foreach (var preset in CrawlProcedureCatalog.Catalog)
        {
            preset.Validate();
            var materialized = preset.MaterializeGeneric();
            materialized.Procedure.Validate();
            Assert.Equal(preset.PresetKey, materialized.Origin?.PresetKey);
        }
    }

    [Fact]
    public void ChoosingPresetMaterializesIndependentCampaignOwnedSnapshots()
    {
        var preset = CrawlProcedureCatalog.Resolve("simple-fixed-distance");

        var first = preset.MaterializeGeneric();
        var second = preset.MaterializeGeneric();

        Assert.NotEqual(first.Procedure.ProcedureId, second.Procedure.ProcedureId);
        Assert.Equal(first.Procedure.Key, second.Procedure.Key);
        Assert.Equal(first.Procedure.Name, second.Procedure.Name);
        Assert.Equal(first.Procedure.Modules, second.Procedure.Modules);
        Assert.Equal("simple-fixed-distance", preset.Origin.PresetKey);
        Assert.Equal(preset.DisplayName, preset.Origin.PresetDisplayName);
        Assert.Equal(preset.PresetRevision, preset.Origin.PresetRevision);
    }

    [Fact]
    public void GenericStartingPointsAreExplicitProductionChoicesAndMixedProofIsNot()
    {
        var generic = CrawlProcedureCatalog.Catalog
            .Where(preset => preset.Category == "Generic starting points")
            .Select(preset => preset.PresetKey)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["simple-fixed-distance", "simple-hex-step"], generic);
        Assert.Equal("Generic starting points", CrawlProcedureCatalog.Resolve("simple-fixed-distance").Category);
        Assert.Equal("Generic starting points", CrawlProcedureCatalog.Resolve("simple-hex-step").Category);
        Assert.DoesNotContain(CrawlProcedureCatalog.Catalog, preset =>
            preset.PresetKey == "mixed-house-rule");
    }

    [Fact]
    public void CampaignOwnedIdentityDoesNotRewritePresetOrigin()
    {
        var preset = CrawlProcedureCatalog.Resolve("simple-fixed-distance");
        var materialized = preset.MaterializeGeneric();
        var campaignOwned = materialized.Procedure with
        {
            Key = "campaign-owned-procedure",
            Name = "Campaign-owned procedure"
        };

        campaignOwned.Validate();

        Assert.Equal("campaign-owned-procedure", campaignOwned.Key);
        Assert.Equal("Campaign-owned procedure", campaignOwned.Name);
        Assert.Equal("simple-fixed-distance", materialized.Origin?.PresetKey);
    }

    [Fact]
    public async Task RemovingOriginMetadataDoesNotChangePersistedProcedureExecutionState()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new ExpeditionStartService(store, new HexCrawlService(store), new CampaignProcedureService(store));

        var started = await service.StartNonSpatialAsync("alice", "Origin independence", new ProcedureStartSelection(PresetKey: "simple-fixed-distance"), new NonSpatialCrawlSessionContext("Procedure clock"));
        var pinned = started.CampaignProcedure;
        var runtimeBefore = GenericProcedureRuntime.Bind(pinned);

        Assert.Equal("simple-fixed-distance", started.ProcedureOrigin?.PresetKey);

        var save = await store.SaveExpeditionAsync(
            started with { ProcedureOrigin = null },
            started.Version);
        Assert.Equal(SaveOutcome.Saved, save.Outcome);

        var loaded = await store.GetExpeditionAsync(started.Id, "alice");
        Assert.NotNull(loaded);
        Assert.Null(loaded!.ProcedureOrigin);
        Assert.Equal(pinned, loaded.CampaignProcedure);
        AssertRuntimeEquivalent(runtimeBefore, GenericProcedureRuntime.Bind(loaded.CampaignProcedure));
    }

    private static void AssertRuntimeEquivalent(GenericProcedureRuntime expected, GenericProcedureRuntime actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Time, actual.Time);
        Assert.Equal(expected.Movement, actual.Movement);
        Assert.Equal(expected.TurnPolicy, actual.TurnPolicy);
        Assert.Equal(expected.Navigation, actual.Navigation);
        Assert.Equal(expected.Encounters, actual.Encounters);
        Assert.Equal(expected.ResolutionHelpers, actual.ResolutionHelpers);
    }
}
