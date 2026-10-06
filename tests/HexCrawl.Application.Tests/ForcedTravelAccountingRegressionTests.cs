using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Runtime;

namespace HexCrawl.Application.Tests;

public sealed class ForcedTravelAccountingRegressionTests
{
    [Fact]
    public void SyntheticMixedProcedureConvertsWatchBudgetToWatchesForcedTravelUnit()
    {
        var procedure = SyntheticProcedureFixtures.MixedProcedure();
        var runtimeId = Guid.NewGuid();
        var before = new NonSpatialSessionState
        {
            Id = runtimeId,
            ElapsedTime = TimeSpan.Zero,
            CompletedWatches = 0
        };
        var after = before with
        {
            ElapsedTime = TimeSpan.FromHours(4),
            CompletedWatches = 1
        };
        var now = DateTimeOffset.UtcNow;
        var expedition = new StoredExpedition(
            "plural-unit-regression",
            before,
            new NonSpatialCrawlSessionContext("plural-unit-regression"),
            null,
            procedure,
            null,
            TimeSpan.Zero,
            "owner",
            1,
            now,
            now);

        var result = ForcedTravelAccounting.AccountTravelMutation(
            expedition,
            before,
            after,
            expedition.Version,
            "watch",
            new ExpeditionConsequenceProvenance(
                ExpeditionConsequenceSourceKind.Procedure,
                "plural-unit-regression"));

        Assert.True(result.StateChanged);
        Assert.Equal(SurvivalOperationStatus.Applied, result.Status);
        Assert.Equal(1, result.Survival.ForcedTravel.AmountSinceReset);
        Assert.Equal("watches", result.Survival.ForcedTravel.Unit);
        Assert.Single(result.Survival.ForcedTravel.AccountedTravelOccurrences);
    }
}
