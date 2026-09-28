using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class ProcedurePresetIdentityTests
{
    [Fact]
    public void ChoosingPresetMaterializesIndependentExecutableState()
    {
        var preset = CrawlProcedureCatalog.Resolve("simple-fixed-distance");

        var materialized = preset.Materialize();

        Assert.Equal(preset.ExecutableProcedureTemplate, materialized);
        Assert.NotSame(preset.ExecutableProcedureTemplate, materialized);
        Assert.Equal("simple-fixed-distance", preset.Origin.PresetKey);
        Assert.Equal(preset.DisplayName, preset.Origin.PresetDisplayName);
        Assert.Equal(preset.PresetRevision, preset.Origin.PresetRevision);
    }

    [Fact]
    public void CustomizedProcedureIdentityDoesNotRewritePresetOrigin()
    {
        var preset = CrawlProcedureCatalog.Resolve("simple-fixed-distance");
        var customized = preset.ExecutableProcedureTemplate with
        {
            Key = "campaign-owned-procedure",
            Name = "Campaign-owned procedure",
            WatchLength = TimeSpan.FromHours(6)
        };

        var materialized = preset.Materialize(customized);

        Assert.Equal("campaign-owned-procedure", materialized.Key);
        Assert.Equal("Campaign-owned procedure", materialized.Name);
        Assert.Equal(TimeSpan.FromHours(6), materialized.WatchLength);
        Assert.Equal("simple-fixed-distance", preset.Origin.PresetKey);
    }

    [Fact]
    public async Task RemovingOriginMetadataDoesNotChangePersistedProcedureExecutionState()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var service = new CrawlSessionService(store);
        var preset = CrawlProcedureCatalog.Resolve("simple-fixed-distance");
        var customized = preset.ExecutableProcedureTemplate with
        {
            Key = "campaign-owned-procedure",
            Name = "Campaign-owned procedure",
            WatchLength = TimeSpan.FromHours(6)
        };

        var started = await service.StartAsync(
            "alice",
            new StartStandaloneCrawlSessionCommand(
                "Origin independence",
                "simple-fixed-distance",
                new NonSpatialCrawlSessionContext("Procedure clock"),
                ProcedureSnapshot: customized));

        Assert.Equal("simple-fixed-distance", started.ProcedureOrigin?.PresetKey);
        Assert.Equal(customized, started.Procedure);

        var save = await store.SaveExpeditionAsync(
            started with { ProcedureOrigin = null },
            started.Version);
        Assert.Equal(SaveOutcome.Saved, save.Outcome);

        var loaded = await store.GetExpeditionAsync(started.Id, "alice");
        Assert.NotNull(loaded);
        Assert.Null(loaded!.ProcedureOrigin);
        Assert.Equal(customized, loaded.Procedure);
    }
}
