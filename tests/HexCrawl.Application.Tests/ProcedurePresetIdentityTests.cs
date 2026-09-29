using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class ProcedurePresetIdentityTests
{
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
        var service = new CrawlSessionService(store);

        var started = await service.StartAsync(
            "alice",
            new StartStandaloneCrawlSessionCommand(
                "Origin independence",
                "simple-fixed-distance",
                new NonSpatialCrawlSessionContext("Procedure clock")));
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
        Assert.Equal(expected.HexProgress, actual.HexProgress);
        Assert.Equal(expected.Navigation, actual.Navigation);
        Assert.Equal(expected.Encounters, actual.Encounters);
        Assert.Equal(expected.ResolutionHelpers, actual.ResolutionHelpers);
    }
}
