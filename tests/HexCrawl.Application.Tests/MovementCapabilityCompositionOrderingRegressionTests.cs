using HexCrawl.Application.Persistence;
using HexCrawl.Domain.Procedure;
using HexCrawl.Domain.Runtime;
using HexCrawl.Domain.Spatial;

namespace HexCrawl.Application.Tests;

public sealed class MovementCapabilityCompositionOrderingRegressionTests
{
    [Fact]
    public void PinnedTerrainRunsBeforePersistentEffectCap()
    {
        var walker = new CrawlPartyMember(Guid.NewGuid(), "Walker", CountsTowardPartyMovement: true);
        var party = new CrawlPartySheet
        {
            Members = [walker],
            MovementContributors =
            [
                new MovementCapabilityContributor
                {
                    Id = Guid.NewGuid(),
                    Kind = MovementCapabilityContributorKind.Participant,
                    Key = "walker",
                    Operation = MovementCapabilityOperation.Base,
                    Scope = MovementCapabilityScope.Participant,
                    Value = 4,
                    Unit = "mi",
                    PerUnit = "hour",
                    DistanceUnit = DistanceUnit.Miles,
                    ParticipantId = walker.Id
                },
                new MovementCapabilityContributor
                {
                    Id = Guid.NewGuid(),
                    Kind = MovementCapabilityContributorKind.PersistentEffect,
                    Key = "persistent-cap",
                    Operation = MovementCapabilityOperation.Cap,
                    Scope = MovementCapabilityScope.Party,
                    Value = 3,
                    Unit = "mi",
                    PerUnit = "hour",
                    DistanceUnit = DistanceUnit.Miles
                }
            ]
        };

        var result = MovementCapabilityComposer.Compose(
            Expedition(Procedure(CrawlProcedureCatalog.Dnd35PresetKey), party),
            new MovementCompositionInput(TerrainKey: "difficult"));

        Assert.Equal(MovementCompositionStatus.Resolved, result.Status);
        Assert.Equal(2, result.EffectiveValue);
        Assert.Equal(
            ["difficult", "persistent-cap"],
            result.Contributors
                .Where(value => value.Applied
                    && value.Kind is MovementCapabilityContributorKind.TerrainRoute
                        or MovementCapabilityContributorKind.PersistentEffect)
                .Select(value => value.Key)
                .ToArray());
    }

    private static CampaignProcedure Procedure(string presetKey) =>
        CrawlProcedureCatalog.Resolve(presetKey).MaterializeGeneric().Procedure;

    private static StoredExpedition Expedition(
        CampaignProcedure procedure,
        CrawlPartySheet party)
    {
        var now = DateTimeOffset.UtcNow;
        var interval = FocusedIntervalPolicyResolver.Resolve(procedure).IntervalDuration ?? TimeSpan.Zero;
        return new StoredExpedition(
            "Movement ordering regression",
            new ExpeditionState
            {
                Id = Guid.NewGuid(),
                Traversal = HexTraversalState.StartingIn(new HexCoordinate(0, 0), DistanceUnit.Miles),
                DistanceTraveled = new DistanceMeasure(0, DistanceUnit.Miles)
            },
            new AbstractHexCrawlSessionContext(
                "Movement ordering regression",
                HexOrientation.PointyTop,
                new CrawlRuntimeContext(new DistanceMeasure(12, DistanceUnit.Miles))),
            null,
            procedure,
            null,
            interval,
            "owner",
            1,
            now,
            now)
        {
            Party = party
        };
    }
}
