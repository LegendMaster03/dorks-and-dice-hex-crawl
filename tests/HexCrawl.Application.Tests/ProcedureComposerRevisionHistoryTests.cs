using System.Globalization;
using HexCrawl.Domain.Procedure;
using HexCrawl.Infrastructure.Persistence;

namespace HexCrawl.Application.Tests;

public sealed class ProcedureComposerRevisionHistoryTests
{
    [Fact]
    public async Task SameModuleCanBeChangedAcrossSavedRevisionsWithUniqueOverrideHistory()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = new PostgresHexCrawlStore(database.ConnectionString);
        await store.InitializeAsync();
        var composer = new ProcedureComposerService(new CampaignProcedureService(store));

        var revision1 = await composer.CreateAsync("alice", "simple-fixed-distance", []);
        var firstOverrideId = $"composer-{GenericProcedureCatalog.TimeIntervalModule}-{Guid.NewGuid():N}";
        var revision2 = await composer.CreateRevisionAsync(
            "alice",
            revision1.ProcedureId,
            revision1.Revision,
            [TimeChange(firstOverrideId, 6)]);

        Assert.Equal(2, revision2.Revision);

        var loadedRevision2 = await composer.GetAsync("alice", revision1.ProcedureId, revision2.Revision);
        var secondOverrideId = $"composer-{GenericProcedureCatalog.TimeIntervalModule}-{Guid.NewGuid():N}";
        Assert.NotEqual(firstOverrideId, secondOverrideId);

        var revision3 = await composer.CreateRevisionAsync(
            "alice",
            revision1.ProcedureId,
            loadedRevision2.Revision,
            [TimeChange(secondOverrideId, 8)]);

        var reloadedRevision1 = await composer.GetAsync("alice", revision1.ProcedureId, 1);
        var reloadedRevision2 = await composer.GetAsync("alice", revision1.ProcedureId, 2);
        var reloadedRevision3 = await composer.GetAsync("alice", revision1.ProcedureId, 3);

        Assert.Equal(1, reloadedRevision1.Revision);
        Assert.Equal(2, reloadedRevision2.Revision);
        Assert.Equal(3, revision3.Revision);
        Assert.Equal(3, reloadedRevision3.Revision);

        Assert.Equal(DurationTicks(4), TimeDuration(reloadedRevision1.Procedure));
        Assert.Equal(DurationTicks(6), TimeDuration(reloadedRevision2.Procedure));
        Assert.Equal(DurationTicks(8), TimeDuration(reloadedRevision3.Procedure));

        Assert.Empty(reloadedRevision1.Procedure.Overrides);
        var revision2Override = Assert.Single(reloadedRevision2.Procedure.Overrides);
        Assert.Equal(firstOverrideId, revision2Override.OverrideId);

        Assert.Equal(2, reloadedRevision3.Procedure.Overrides.Count);
        Assert.Equal(firstOverrideId, reloadedRevision3.Procedure.Overrides[0].OverrideId);
        Assert.Equal(secondOverrideId, reloadedRevision3.Procedure.Overrides[1].OverrideId);
        Assert.Equal(
            2,
            reloadedRevision3.Procedure.Overrides
                .Select(value => value.OverrideId)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    private static CampaignProcedureOverride TimeChange(string overrideId, int hours) =>
        new(
            overrideId,
            GenericProcedureCatalog.TimeIntervalModule,
            null,
            null,
            new Dictionary<string, string>
            {
                ["durationTicks"] = DurationTicks(hours)
            });

    private static string TimeDuration(CampaignProcedure procedure) =>
        procedure.Modules.Single(module =>
                module.Module.Key == GenericProcedureCatalog.TimeIntervalModule)
            .Parameters["durationTicks"];

    private static string DurationTicks(int hours) =>
        TimeSpan.FromHours(hours).Ticks.ToString(CultureInfo.InvariantCulture);
}
